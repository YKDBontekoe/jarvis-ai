using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Api.Errors;

internal sealed class JarvisApiProblemResponseMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IProblemDetailsService problemDetailsService,
        IHostEnvironment environment)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        if (context.Response.StatusCode < StatusCodes.Status400BadRequest)
        {
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
            return;
        }

        var contentType = context.Response.ContentType ?? "";
        if (contentType.Contains("application/problem+json", StringComparison.OrdinalIgnoreCase))
        {
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
            return;
        }

        buffer.Position = 0;
        string? bodyText = null;
        if (buffer.Length > 0)
        {
            using var reader = new StreamReader(buffer, leaveOpen: true);
            bodyText = await reader.ReadToEndAsync(context.RequestAborted);
        }

        if (LegacyErrorResponseParser.AlreadyProblemContract(bodyText))
        {
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
            return;
        }

        var defaults = StatusCodeProblemDefaults.For(context.Response.StatusCode);
        var detail = LegacyErrorResponseParser.ExtractClientDetail(bodyText) ?? defaults.Detail;
        ProblemDetails problem;
        if (LegacyErrorResponseParser.TryParseValidationErrors(bodyText, out var validationErrors))
        {
            problem = JarvisProblemDetails.CreateValidation(validationErrors, context, detail);
            problem.Status = context.Response.StatusCode;
        }
        else
        {
            problem = JarvisProblemDetails.Create(defaults.Code, defaults.Title, context.Response.StatusCode, detail,
                context);
        }

        JarvisProblemDetails.ApplyProductionRedaction(problem, environment.IsDevelopment());

        context.Response.Body = originalBody;
        context.Response.ContentLength = null;
        context.Response.ContentType = "application/problem+json";

        var problemContext = new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
        };

        if (!await problemDetailsService.TryWriteAsync(problemContext))
        {
            await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
        }
    }
}

internal static class JarvisApiProblemResponseMiddlewareExtensions
{
    public static IApplicationBuilder UseJarvisApiProblemResponses(this IApplicationBuilder app) =>
        app.UseMiddleware<JarvisApiProblemResponseMiddleware>();
}
