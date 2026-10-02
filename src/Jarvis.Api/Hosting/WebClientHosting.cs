using Microsoft.AspNetCore.StaticFiles;

namespace Jarvis.Api.Hosting;

internal static class WebClientHosting
{
    public static void UseJarvisWebClient(this WebApplication app)
    {
        // Backend-only local builds have no web distribution. Release images
        // carry the Flutter build in wwwroot alongside the API assembly.
        var root = app.Environment.WebRootPath;
        if (root is null || !File.Exists(Path.Combine(root, "index.html"))) return;

        var contentTypes = new FileExtensionContentTypeProvider();
        contentTypes.Mappings[".wasm"] = "application/wasm";
        contentTypes.Mappings[".mjs"] = "text/javascript";

        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = contentTypes,
            OnPrepareResponse = context =>
            {
                // Flutter uses stable asset names; revalidate across releases.
                context.Context.Response.Headers.CacheControl = "no-cache";
                context.Context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Context.Response.Headers["Referrer-Policy"] = "same-origin";
                context.Context.Response.Headers["Content-Security-Policy"] =
                    "frame-ancestors 'self'; object-src 'none'; base-uri 'self'";
            }
        });
        // No catch-all HTML fallback: missing API/hub routes must stay errors.
        // Flutter's default hash navigation requires only the root document.
    }
}
