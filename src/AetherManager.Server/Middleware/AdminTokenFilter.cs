using System.Security.Cryptography;
using System.Text;
using AetherManager.Server.Services;

namespace AetherManager.Server.Middleware;

internal sealed class AdminTokenFilter(IManagerStore store) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        var supplied = GetBearerToken(context.HttpContext.Request);
        var expected = await store.GetAdminTokenAsync(context.HttpContext.RequestAborted);
        if (supplied is null || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
                SHA256.HashData(Encoding.UTF8.GetBytes(expected))))
            return TypedResults.Unauthorized();
        return await next(context);
    }

    internal static string? GetBearerToken(HttpRequest request)
    {
        var value = request.Headers.Authorization.ToString();
        return value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? value["Bearer ".Length..].Trim()
            : null;
    }
}
