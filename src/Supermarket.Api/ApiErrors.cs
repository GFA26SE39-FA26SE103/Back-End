using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Domain;
using AppError = Supermarket.Application.ApplicationException;
namespace Supermarket.Api;

public sealed class ApiErrors(IProblemDetailsService problems, ILogger<ApiErrors> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, code, message) = exception switch
        {
            DomainException e => (422, e.Code, e.Message),
            AppError e => (e.Status, e.Code, e.Message),
            _ => (500, "INTERNAL_ERROR", "An unexpected error occurred.")
        };
        // Log error types only: adapter/SQL exception messages can contain secrets.
        if (status == 500)
            logger.LogError("Request {RequestId} failed ({ErrorType})", context.TraceIdentifier, exception.GetType().Name);
        context.Response.StatusCode = status;
        await problems.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = new ProblemDetails { Status = status, Title = code, Detail = message, Extensions = { { "code", code }, { "requestId", context.TraceIdentifier } } } });
        return true;
    }
}
