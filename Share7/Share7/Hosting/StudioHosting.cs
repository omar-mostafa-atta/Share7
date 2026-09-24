using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Share7.Infrastructure.Staff;

namespace Share7.API.Hosting;

/// <summary>
/// Serves the Content Studio (<c>Share7.Studio</c>, built into <c>wwwroot-studio</c>) on its own
/// host name — <c>Studio:Host</c>, e.g. <c>studio.example.com</c> — from this same process.
/// <para>
/// <b>Its own origin, on purpose.</b> The browser keeps each origin's storage apart, so a bug in the
/// Admin Console cannot read a Studio sign-in or the reverse. Requests for <c>/api</c> on the
/// Studio's host fall through to the ordinary pipeline, which keeps the Studio's API calls
/// same-origin: no CORS anywhere, as in the rest of the solution.
/// </para>
/// <para>
/// When <c>Studio:Host</c> is empty nothing here runs — in development the Vite dev server serves
/// the Studio and proxies <c>/api</c> — and the Studio can equally be hosted elsewhere, as long as
/// <c>/api</c> on its origin reaches this API.
/// </para>
/// </summary>
public static class StudioHosting
{
    public const string BuildFolder = "wwwroot-studio";

    /// <summary>
    /// Tight by default: nothing but this origin, plus the Google Fonts the console already uses.
    /// No inline script, no framing, and no referrer — setup links carry their secret in the URL
    /// fragment, and the Studio should never leak even the path it was on.
    /// </summary>
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'none'; " +
        "form-action 'self'; " +
        "object-src 'none'";

    public static WebApplication UseStudioHosting(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<StudioOptions>>().Value;
        if (string.IsNullOrWhiteSpace(options.Host))
            return app;

        var host = options.Host.Trim();
        var root = Path.Combine(app.Environment.ContentRootPath, BuildFolder);

        if (!Directory.Exists(root))
        {
            app.Logger.LogWarning(
                "Studio:Host is {Host} but {Folder} does not exist; build Share7.Studio before serving it from here.",
                host, root);
            return app;
        }

        var files = new PhysicalFileProvider(root);

        app.MapWhen(
            context => string.Equals(context.Request.Host.Host, host, StringComparison.OrdinalIgnoreCase)
                       && !context.Request.Path.StartsWithSegments("/api"),
            studio =>
            {
                studio.Use(async (context, next) =>
                {
                    var headers = context.Response.Headers;
                    headers.ContentSecurityPolicy = ContentSecurityPolicy;
                    headers.XContentTypeOptions = "nosniff";
                    headers.XFrameOptions = "DENY";
                    headers["Referrer-Policy"] = "no-referrer";
                    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
                    headers["Cross-Origin-Opener-Policy"] = "same-origin";
                    await next();
                });

                studio.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = files,
                    OnPrepareResponse = context =>
                    {
                        // Vite fingerprints everything under /assets, so those never change under a
                        // name; the shell must always be revalidated or a deploy "does not take".
                        context.Context.Response.Headers.CacheControl =
                            context.Context.Request.Path.StartsWithSegments("/assets")
                                ? "public, max-age=31536000, immutable"
                                : "no-cache";
                    }
                });

                // Client-side routes (/activate, /sign-in, /account…) all resolve to the shell.
                studio.Run(async context =>
                {
                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.Headers.CacheControl = "no-cache";
                    await context.Response.SendFileAsync(files.GetFileInfo("index.html"));
                });
            });

        return app;
    }
}
