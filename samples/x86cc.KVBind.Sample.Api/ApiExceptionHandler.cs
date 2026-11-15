using JasperFx;
using Microsoft.AspNetCore.Diagnostics;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.Sample.Api;

// Turns the exceptions that mean "the request was wrong" or "someone else saved first" into 400 / 409
// responses with the same { error } body the rebase endpoints already return. Anything else, including an
// InvalidOperationException or JsonException from a defect or from stored data, stays a 500 and is logged,
// without its message reaching the client.
public sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // A commit whose data breaks the definition's rules (e.g. on create): 422 with the errors.
        if (exception is KVChangeSetValidationException invalid)
        {
            httpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            await httpContext.Response.WriteAsJsonAsync(
                new
                {
                    error = "The data does not pass validation.",
                    errors = invalid.Errors.Select(e => new { e.Path, displayPath = e.Path, e.Code, message = e.Message ?? e.Code })
                },
                cancellationToken);
            return true;
        }

        var status = exception switch
        {
            ConcurrencyException => StatusCodes.Status409Conflict,
            ApiRequestException => StatusCodes.Status400BadRequest,
            BadHttpRequestException badRequest => badRequest.StatusCode,
            _ => (int?)null,
        };

        if (status is null)
        {
            return false;
        }

        var message = exception is ConcurrencyException
            ? "The aggregate or draft was changed by another request. Reload it and try again."
            : exception.Message;

        httpContext.Response.StatusCode = status.Value;
        await httpContext.Response.WriteAsJsonAsync(new { error = message }, cancellationToken);
        return true;
    }
}
