namespace Supermarket.Api;

public sealed class DeviceRegistrationLoggingMiddleware(
    RequestDelegate next, ILogger<DeviceRegistrationLoggingMiddleware> logger)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method) ||
            !string.Equals(context.Request.Path.Value?.TrimEnd('/'), "/api/me/devices", StringComparison.OrdinalIgnoreCase))
            return next(context);

        var requestId = context.TraceIdentifier;
        logger.LogInformation("Device registration POST /api/me/devices received (RequestId={RequestId})", requestId);
        context.Response.OnStarting(() =>
        {
            logger.LogInformation("Device registration POST /api/me/devices responding (StatusCode={StatusCode}, RequestId={RequestId})",
                context.Response.StatusCode, requestId);
            return Task.CompletedTask;
        });
        return next(context);
    }
}
