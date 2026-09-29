using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Jarvis.Api.Errors;

internal sealed class JarvisExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<JarvisExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        var mapped = ExceptionProblemMapper.Map(exception);
        if (mapped.Status >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled API exception mapped to {Code}.", mapped.Code);
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
