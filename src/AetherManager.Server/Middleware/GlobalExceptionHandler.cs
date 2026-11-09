using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using AetherManager.Server.Services;

namespace AetherManager.Server.Middleware;

internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled AETHER Manager request failure");
        var status = exception switch
        {
            BadHttpRequestException request => request.StatusCode,
            GraphIntegrationException graph => graph.StatusCode,
            ArgumentException => StatusCodes.Status400BadRequest,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            InvalidOperationException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = exception is GraphIntegrationException graphException ? graphException.Title :
                status == 500 ? "Internal Server Error" : "Management request rejected",
            Detail = exception is GraphIntegrationException graphDetail ? graphDetail.Detail :
                status == 500 ? "The request could not be completed." :
                    "Check the device state, permissions, action identifier and request limits.",
            Instance = httpContext.Request.Path
        }, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
