using System.Security.Cryptography;
using System.Text;
using AetherManager.Server.Services;
using AetherManager.Server.Models;
using AetherOperations;

namespace AetherManager.Server.Middleware;

internal sealed class AdminTokenFilter(IManagerStore store, IAccountService accounts) : IEndpointFilter
{
    internal const string CookieName = "__Host-AetherSession";
    internal const string SessionKey = "Aether.Session";
    internal const string ActorKey = "Aether.Actor";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        http.Response.Headers.CacheControl = "no-store";
        var cookie = http.Request.Cookies[CookieName];
        var session = await accounts.ValidateAsync(cookie, http.RequestAborted);
        if (session is not null && http.Request.IsHttps)
        {
            http.Items[SessionKey] = session;
            http.Items[ActorKey] = LocalAccountService.Actor(session.Account);
            if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method) &&
                (!SameOrigin(http.Request) || !SecretEquals(http.Request.Headers["X-Aether-CSRF"].ToString(), session.CsrfToken)))
                return TypedResults.Problem(statusCode: 403, title: "CSRF validation failed");
            if (!CanAccess(session.Account.Role, http.Request.Method, http.Request.Path,
                    context.Arguments.OfType<CreateManagedJobRequest>().FirstOrDefault()))
                return TypedResults.Problem(statusCode: 403, title: "Role does not permit this operation");
            return await next(context);
        }
        // Never fall back from a browser/expired session to the administrator capability.
        if (cookie is not null || IsBrowser(http.Request) || http.Request.Path.StartsWithSegments("/api/v1/admin/accounts") ||
            http.Request.Path.StartsWithSegments("/api/v1/admin/sessions") || http.Request.Path.StartsWithSegments("/api/v1/admin/account-audit"))
            return TypedResults.Problem(statusCode: 401, title: "Sign in required");
        var bootstrapped = (await accounts.GetAccountsAsync(http.RequestAborted)).Length > 0;
        if (bootstrapped && !string.Equals(Environment.GetEnvironmentVariable("AETHER_MANAGER_LEGACY_ADMIN_API"), "true", StringComparison.OrdinalIgnoreCase))
            return TypedResults.Problem(statusCode: 401, title: "Legacy administrator API disabled");
        var supplied = GetBearerToken(http.Request);
        var expected = await store.GetAdminTokenAsync(context.HttpContext.RequestAborted);
        if (supplied is null || !SecretEquals(supplied, expected))
            return TypedResults.Problem(statusCode: 401, title: "Authentication required");
        http.Items[ActorKey] = "legacy-admin-api";
        return await next(context);
    }

    internal static bool CanAccess(AccountRole role, string method, PathString path, CreateManagedJobRequest? job = null)
    {
        if (!Enum.IsDefined(role)) return false;
        if (role == AccountRole.Administrator) return true;
        if (path.StartsWithSegments("/api/v1/admin/accounts") || path.StartsWithSegments("/api/v1/admin/sessions"))
            return false;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)) return true;
        if (role != AccountRole.Operator || !HttpMethods.IsPost(method) ||
            !path.Value!.StartsWith("/api/v1/admin/management/devices/", StringComparison.Ordinal) ||
            !path.Value.EndsWith("/jobs", StringComparison.Ordinal) || job is null || job.ConfirmChanges ||
            job.Update is not null || job.SourceJobId is not null) return false;
        return ManagedActionCatalog.All.Any(x => x.Id == job.ActionId && !x.ChangesSystem);
    }

    internal static bool SecretEquals(string supplied, string expected) => supplied.Length <= 1024 &&
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    internal static bool IsBrowser(HttpRequest request) => request.Headers.ContainsKey("Origin") ||
        request.Headers.ContainsKey("Sec-Fetch-Site") || request.Headers.UserAgent.ToString().Contains("Mozilla/", StringComparison.Ordinal);
    internal static bool SameOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (origin.Length != 0)
            return Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                Uri.TryCreate($"{request.Scheme}://{request.Host}", UriKind.Absolute, out var expected) &&
                uri.UserInfo.Length == 0 && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
                uri.Scheme.Equals(expected.Scheme, StringComparison.OrdinalIgnoreCase) &&
                uri.IdnHost.Equals(expected.IdnHost, StringComparison.OrdinalIgnoreCase) && uri.Port == expected.Port;
        return request.Headers["Sec-Fetch-Site"] == "same-origin";
    }
    internal static string Actor(HttpContext context) => context.Items[ActorKey]?.ToString() ?? "unknown";

    internal static string? GetBearerToken(HttpRequest request)
    {
        var value = request.Headers.Authorization.ToString();
        return value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? value["Bearer ".Length..].Trim()
            : null;
    }
}
