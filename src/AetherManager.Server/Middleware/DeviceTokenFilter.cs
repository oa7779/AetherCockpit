using AetherManager.Server.Services;

namespace AetherManager.Server.Middleware;

internal sealed class DeviceTokenFilter(IManagerStore store) : IEndpointFilter
{
    internal static readonly object DeviceContextKey = new();

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var token = AdminTokenFilter.GetBearerToken(context.HttpContext.Request);
        if (token is null)
            return TypedResults.Unauthorized();

        var device = await store.FindDeviceAsync(token, context.HttpContext.RequestAborted);
        if (device is null)
            return TypedResults.Unauthorized();
        if (device.State != "approved")
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);

        context.HttpContext.Items[DeviceContextKey] = device;
        return await next(context);
    }
}
