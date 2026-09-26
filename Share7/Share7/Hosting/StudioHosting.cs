using Microsoft.Extensions.FileProviders;

namespace Share7.API.Hosting;

/// <summary>
/// Serves the Content Studio (<c>Share7.Studio</c>, built into <c>wwwroot-studio</c>) at one fixed
/// address on this same process: <c>/studio</c>. Every content-team member, whatever their role,
/// signs in there — <c>https://&lt;this site&gt;/studio</c> — and the Admin Console keeps the root.
/// <para>
/// <b>A path, not a host name (decided 2026-09-26).</b> The Studio used to be served only on a host
/// name of its own (<c>Studio:Host</c>), which kept its browser storage apart from the Admin
/// Console's. The hosting the platform runs on gives the site one address, and the product owner
/// wanted one constant address to hand to the content team, so the Studio moved under this path.
/// Its sign-in never shared the console's anyway: the Studio keeps its access token in memory and its
/// refresh token in an HttpOnly cookie scoped to <c>/api/studio/auth</c>, and it accepts only
/// Studio-audience tokens.
/// </para>
/// <para>
/// In development the Vite dev server serves the Studio at <c>http://localhost:5174/studio</c> and
/// proxies <c>/api</c>; nothing here runs unless the Studio has been built into this folder.
/// </para>
/// </summary>
public static class StudioHosting
{
    public const string BuildFolder = "wwwroot-studio";

    /// <summary>Where the Studio lives on this site. Must match <c>base</c> in Share7.Studio/vite.config.ts.</summary>
    public const string PathBase = "/studio";

    /// <summary>
    /// Tight by default: nothing but this origin, plus the Google Fonts the console already uses.
    /// No inline script, no framing, and no referrer.
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
        var root = Path.Combine(app.Environment.ContentRootPath, BuildFolder);

        if (!Directory.Exists(root) || !File.Exists(Path.Combine(root, "index.html")))
        {
            app.Logger.LogInformation(
                "{Folder} has no build, so the Studio is not served at {Path}; build Share7.Studio before publishing.",
                root, PathBase);
            return app;
        }

        var files = new PhysicalFileProvider(root);

        // /studio and everything under it, whatever the case ("/Studio" too). Before the console's
        // static files and fallback in Program.cs, so the console never answers here.
        app.Map(PathBase, studio =>
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

            // Client-side routes (/studio/sign-in, /studio/curriculum…) all resolve to the shell.
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
