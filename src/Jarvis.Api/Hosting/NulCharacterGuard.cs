using System.Text.Json;
using System.Text.Json.Serialization;
using Jarvis.Api.Errors;

namespace Jarvis.Api.Hosting;

/// <summary>
/// PostgreSQL cannot hold the NUL character, and a query that compares against one fails like a write does. Text in a
/// JSON body loses its NULs on the way in; a path or query string that carries one is turned away with a 400.
/// </summary>
internal sealed class NulStrippingStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value is not null && value.Contains('\0') ? value.Replace("\0", "", StringComparison.Ordinal) : value;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

internal static class NulCharacterGuard
{
    public static IServiceCollection AddNulCharacterGuard(this IServiceCollection services) =>
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new NulStrippingStringConverter()));

    public static IApplicationBuilder UseNulCharacterGuard(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (HasNul(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ApiProblemResults.Validation(context, "request", "The address contains a character that is not allowed.")
                    .ExecuteAsync(context);
                return;
            }

            await next(context);
        });

    internal static bool HasNul(HttpRequest request) =>
        request.Path.Value?.Contains('\0') == true ||
        request.QueryString.Value?.Contains("%00", StringComparison.OrdinalIgnoreCase) == true;
}
