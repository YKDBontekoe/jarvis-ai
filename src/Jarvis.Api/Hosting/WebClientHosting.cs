using Microsoft.AspNetCore.StaticFiles;

namespace Jarvis.Api.Hosting;

internal static class WebClientHosting
{
    internal static string CacheControlFor(PathString path)
    {
        // Release files live at /r/<git sha>/. Their URLs change every deploy.
        // The homepage stays revalidated so <base href> can point at the new path.
        var value = path.Value ?? "";
        return value.StartsWith("/r/", StringComparison.Ordinal)
            ? "public, max-age=86400"
            : "no-cache";
    }

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
                var cacheControl = CacheControlFor(context.Context.Request.Path);
                context.Context.Response.Headers.CacheControl = cacheControl;
                if (cacheControl == "no-cache")
                {
                    // Keep the homepage out of Cloudflare even if a cache-everything rule is added.
                    context.Context.Response.Headers["CDN-Cache-Control"] = "no-store";
                    context.Context.Response.Headers["Cloudflare-CDN-Cache-Control"] = "no-store";
                }
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
