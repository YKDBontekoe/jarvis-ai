using Jarvis.Application;
using Jarvis.ServiceDefaults;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Sentry;

namespace Jarvis.Api.Errors;

internal sealed class JarvisExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<JarvisExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        var mapped = ExceptionProblemMapper.Map(exception, httpContext);
        if (mapped.Status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled API exception mapped to {Code}.", mapped.Code);
            if (JarvisSentryExceptions.ShouldCapture(exception, httpContext))
                SentrySdk.CaptureException(exception);
        }
        else
            logger.LogWarning(exception, "API exception mapped to {Code}.", mapped.Code);

        var problem = JarvisProblemDetails.Create(mapped.Code, mapped.Title, mapped.Status, mapped.Detail,
            httpContext);
        JarvisProblemDetails.ApplyProductionRedaction(problem, environment.IsDevelopment());

        var context = new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        };

        httpContext.Response.StatusCode = mapped.Status;
        return await problemDetailsService.TryWriteAsync(context);
    }
}

internal static class JarvisSentryExceptions
{
    public static bool ShouldCapture(Exception exception, HttpContext? httpContext = null)
    {
        // A used-up model plan is an account limit the owner is already told about, not a defect.
        if (Jarvis.Api.Conversations.AgentFailureMessage.IsRateLimited(exception)) return false;
        // A cancellation with no timeout behind it was asked for: the client left or the run was stopped.
        // Model clients turn their own deadlines into TimeoutException, which is still captured.
        if (CancellationExceptions.Unwrap(exception) is not null && !HasTimeout(exception)) return false;
        return JarvisSentry.ShouldCaptureHttpStatus(ExceptionProblemMapper.Map(exception, httpContext).Status);
    }

    private static bool HasTimeout(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is TimeoutException) return true;
        return false;
    }
}
