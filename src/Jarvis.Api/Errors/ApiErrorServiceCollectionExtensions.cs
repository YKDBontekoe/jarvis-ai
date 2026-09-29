using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jarvis.Api.Errors;

internal static class ApiErrorServiceCollectionExtensions
{
    public static IServiceCollection AddJarvisApiErrors(this IServiceCollection services)
    {
        services.AddExceptionHandler<JarvisExceptionHandler>();
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                var problem = context.ProblemDetails;
                var httpContext = context.HttpContext;
                if (!problem.Extensions.ContainsKey("traceId"))
                    problem.Extensions["traceId"] = JarvisProblemDetails.ResolveTraceId(httpContext);

                if (!problem.Extensions.ContainsKey("apiErrorVersion"))
                    problem.Extensions["apiErrorVersion"] = ApiErrorCodes.ContractVersion;

                if (!problem.Extensions.ContainsKey("code"))
                {
                    problem.Extensions["code"] = problem.Status switch
                    {
                        StatusCodes.Status400BadRequest => ApiErrorCodes.ValidationFailed,
                        StatusCodes.Status401Unauthorized => ApiErrorCodes.AuthenticationRequired,
                        StatusCodes.Status403Forbidden => ApiErrorCodes.AuthorizationForbidden,
                        StatusCodes.Status404NotFound => ApiErrorCodes.ResourceNotFound,
                        StatusCodes.Status409Conflict => ApiErrorCodes.Conflict,
                        StatusCodes.Status429TooManyRequests => ApiErrorCodes.RateLimited,
                        StatusCodes.Status499ClientClosedRequest => ApiErrorCodes.RequestCancelled,
                        StatusCodes.Status502BadGateway or StatusCodes.Status503ServiceUnavailable =>
                            ApiErrorCodes.DependencyUnavailable,
                        StatusCodes.Status504GatewayTimeout => ApiErrorCodes.Timeout,
                        StatusCodes.Status422UnprocessableEntity => ApiErrorCodes.MalwareRejected,
                        _ => problem.Status >= StatusCodes.Status500InternalServerError
                            ? ApiErrorCodes.InternalError
                            : ApiErrorCodes.ValidationFailed,
                    };
                }

                if (string.IsNullOrEmpty(problem.Type))
                    problem.Type = JarvisProblemDetails.TypeUriFor(problem.Extensions["code"]!.ToString()!);

                if (string.IsNullOrEmpty(problem.Instance))
                    problem.Instance = httpContext.Request.Path;

                var environment = httpContext.RequestServices.GetRequiredService<IHostEnvironment>();
                JarvisProblemDetails.ApplyProductionRedaction(problem, environment.IsDevelopment());
            };
        });

        return services;
    }

    public static JwtBearerEvents CreateJarvisJwtBearerEvents(JwtBearerEvents? existing = null)
    {
        existing ??= new JwtBearerEvents();
        var previousChallenge = existing.OnChallenge;
        existing.OnChallenge = async context =>
        {
            if (previousChallenge is not null)
                await previousChallenge(context);

            if (context.Handled || context.Response.HasStarted) return;

            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";

            var problem = JarvisProblemDetails.Create(
                ApiErrorCodes.AuthenticationRequired,
                "Authentication required",
                StatusCodes.Status401Unauthorized,
                "Authentication is required.",
                context.HttpContext);

            var jsonOptions = context.HttpContext.RequestServices
                .GetService<IOptions<JsonOptions>>()?.Value?.SerializerOptions ?? JsonSerializerOptions.Web;
            await context.Response.WriteAsJsonAsync(problem, problem.GetType(), jsonOptions);
        };
        return existing;
    }
}
