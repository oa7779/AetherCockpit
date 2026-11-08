using AetherManager.Server.Middleware;
using AetherManager.Server.Models;
using AetherManager.Server.Services;

namespace AetherManager.Server.Endpoints;

internal static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/auth/bootstrap", async Task<IResult> (CreateAccountRequest request, HttpContext context,
            IAccountService accounts, IManagerStore store, CancellationToken token) =>
        {
            if (!context.Request.IsHttps)
                return TypedResults.Problem(statusCode: 400, title: "Account provisioning requires HTTPS");
            if (AdminTokenFilter.IsBrowser(context.Request))
                return TypedResults.Problem(statusCode: 403, title: "Bootstrap requires the protected local administrator token via an administrative API tool");
            var supplied = AdminTokenFilter.GetBearerToken(context.Request);
            if (supplied is null || !AdminTokenFilter.SecretEquals(supplied, await store.GetAdminTokenAsync(token)))
                return TypedResults.Problem(statusCode: 401, title: "Privileged bootstrap token required");
            var account = await accounts.BootstrapAsync(request, token);
            return TypedResults.Created($"/api/v1/admin/accounts/{account.Id}", account);
        }).RequireRateLimiting("login").WithSummary("Provision the initial local administrator exactly once with the existing protected token");

        app.MapPost("/api/v1/auth/login", async Task<IResult> (LoginRequest request, HttpContext context,
            IAccountService accounts, CancellationToken token) =>
        {
            if (!context.Request.IsHttps)
                return TypedResults.Problem(statusCode: 400, title: "Account sessions require HTTPS, including loopback");
            if (!AdminTokenFilter.SameOrigin(context.Request) || context.Request.Headers["X-Aether-Login"] != "1")
                return TypedResults.Problem(statusCode: 403, title: "Same-origin login required");
            var login = await accounts.LoginAsync(request, token);
            if (login is null) return TypedResults.Problem(statusCode: 401, title: "Sign-in failed or temporarily locked");
            context.Response.Cookies.Append(AdminTokenFilter.CookieName, login.Value.Cookie, CookieOptions(login.Value.Response.ExpiresAt));
            return TypedResults.Ok(login.Value.Response);
        }).RequireRateLimiting("login").WithSummary("Sign in for an absolute 30-minute revocable session");

        app.MapGet("/api/v1/auth/session", async Task<IResult> (HttpContext context, IAccountService accounts, CancellationToken token) =>
        {
            var session = context.Request.IsHttps ? await accounts.ValidateAsync(context.Request.Cookies[AdminTokenFilter.CookieName], token) : null;
            return session is null ? TypedResults.Problem(statusCode: 401, title: "Session expired or revoked") : TypedResults.Ok(session);
        }).RequireRateLimiting("management").WithSummary("Read the current authenticated session and CSRF token");

        app.MapPost("/api/v1/auth/logout", async Task<IResult> (HttpContext context, IAccountService accounts, CancellationToken token) =>
        {
            var session = await accounts.ValidateAsync(context.Request.Cookies[AdminTokenFilter.CookieName], token);
            if (session is null || !context.Request.IsHttps)
                return TypedResults.Problem(statusCode: 401, title: "Session expired or revoked");
            if (!AdminTokenFilter.SameOrigin(context.Request) ||
                !AdminTokenFilter.SecretEquals(context.Request.Headers["X-Aether-CSRF"].ToString(), session.CsrfToken))
                return TypedResults.Problem(statusCode: 403, title: "CSRF validation failed");
            await accounts.RevokeAsync(session.SessionId, LocalAccountService.Actor(session.Account), token);
            context.Response.Cookies.Delete(AdminTokenFilter.CookieName, CookieOptions(null));
            return TypedResults.NoContent();
        }).RequireRateLimiting("management").WithSummary("Revoke the current session and clear its secure cookie");

        var admin = app.MapGroup("/api/v1/admin").AddEndpointFilter<AdminTokenFilter>().RequireRateLimiting("management");
        admin.MapGet("/accounts", async (IAccountService accounts, CancellationToken token) =>
            TypedResults.Ok(await accounts.GetAccountsAsync(token))).WithSummary("List local users without credentials");
        admin.MapGet("/accounts/{id:guid}", async Task<IResult> (Guid id, IAccountService accounts, CancellationToken token) =>
        {
            var account = (await accounts.GetAccountsAsync(token)).FirstOrDefault(x => x.Id == id);
            return account is null ? TypedResults.NotFound() : TypedResults.Ok(account);
        }).WithSummary("Read local user metadata");
        admin.MapPost("/accounts", async (CreateAccountRequest request, HttpContext context, IAccountService accounts, CancellationToken token) =>
        {
            var account = await accounts.CreateAsync(request, AdminTokenFilter.Actor(context), token);
            return TypedResults.Created($"/api/v1/admin/accounts/{account.Id}", account);
        }).WithSummary("Create a local management user");
        admin.MapPut("/accounts/{id:guid}", async (Guid id, UpdateAccountRequest request, HttpContext context, IAccountService accounts, CancellationToken token) =>
            TypedResults.Ok(await accounts.UpdateAsync(id, request, AdminTokenFilter.Actor(context), token)))
            .WithSummary("Change role, disable or reset password; revoke all user sessions; preserve the last administrator");
        admin.MapGet("/sessions", async (IAccountService accounts, CancellationToken token) =>
            TypedResults.Ok(await accounts.GetSessionsAsync(token))).WithSummary("List live revocable sessions");
        admin.MapPost("/sessions/{id:guid}/revoke", async (Guid id, HttpContext context, IAccountService accounts, CancellationToken token) =>
        {
            await accounts.RevokeAsync(id, AdminTokenFilter.Actor(context), token);
            return TypedResults.NoContent();
        }).WithSummary("Revoke a local user session");
        admin.MapGet("/account-audit", async (IAccountService accounts, CancellationToken token) =>
            TypedResults.Ok(await accounts.GetAuditAsync(token))).WithSummary("Read the latest account security transitions");
    }

    private static CookieOptions CookieOptions(DateTimeOffset? expiry) => new()
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", Expires = expiry, IsEssential = true
    };
}
