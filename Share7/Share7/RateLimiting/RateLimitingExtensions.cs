using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Share7.Application.Common.Models;

namespace Share7.API.RateLimiting;

/// <summary>
/// Request throttling: a generous global backstop, plus two tighter policies applied by attribute
/// to the routes worth attacking.
/// <para>
/// **Partitioned by user id wherever the caller is authenticated**, and by address only when there
/// is nobody to attribute the request to. Addresses are the weaker key — a household shares one,
/// and a determined caller can change theirs — so they are used only on the anonymous routes,
/// where there is no alternative.
/// </para>
/// <para>
/// This is a defence against volume, not against forgery. It does not stop one bad request; it
/// stops the same one arriving ten thousand times, which is what turns a replayed capture or a
/// stolen password list into a working attack.
/// </para>
/// </summary>
public static class RateLimitingExtensions
{
    public static IServiceCollection AddShare7RateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(RateLimitOptions.SectionName);
        services.Configure<RateLimitOptions>(section);

        // Read once here as well as binding it: the partitioners are built at startup and hold
        // their limits for the lifetime of the process, so there is nothing inside them for a
        // later configuration reload to reach. Changing a limit means a restart.
        var options = section.Get<RateLimitOptions>() ?? new RateLimitOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Fixed window rather than sliding: the worst case is a caller getting two windows'
            // worth of requests across a boundary, and at this limit that is still ordinary
            // traffic. The auth policy below is the one place that margin matters.
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"global:{PartitionKeyFor(context, options)}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.GlobalPermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),

                        // Refuse immediately rather than holding the request. Queueing would turn a
                        // flood into latency for every caller behind it, and a client that is being
                        // throttled needs the 429 to know to back off.
                        QueueLimit = 0
                    }));

            // Sliding here, unlike the two fixed windows. At twenty a minute, the burst a fixed
            // window permits across its boundary is forty back-to-back attempts, which is a real
            // head start on a password list. Four segments cost little and cut it to twenty-five.
            limiter.AddPolicy(RateLimitPolicies.Auth, context =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    $"auth:{ClientAddressFor(context, options)}",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = options.AuthPermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 4,
                        QueueLimit = 0
                    }));

            limiter.AddPolicy(RateLimitPolicies.Writes, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"write:{PartitionKeyFor(context, options)}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.WritePermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // Fixed window, like Writes. A telemetry batch is not latency-sensitive and the client
            // already backs off on a 429 — there is nothing here worth the extra segments the auth
            // policy pays for.
            limiter.AddPolicy(RateLimitPolicies.Telemetry, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"telemetry:{PartitionKeyFor(context, options)}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.TelemetryPermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                // The limiter knows when the next permit frees up; falling back to the whole window
                // is never an under-estimate. Sent as the standard header and in the body both,
                // because the Unity client reads every refusal through the error envelope.
                var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? (int)Math.Ceiling(retryAfter.TotalSeconds)
                    : 60;

                context.HttpContext.Response.Headers.RetryAfter =
                    retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        code = ApiErrors.RateLimited.Code,
                        messageKey = ApiErrors.RateLimited.MessageKey,
                        details = new Dictionary<string, object?> { ["retryAfterSeconds"] = retryAfterSeconds }
                    },
                    cancellationToken);
            };
        });

        return services;
    }

    /// <summary>
    /// Adds the middleware unless configuration switched it off. **Must sit after
    /// <c>UseAuthentication</c>** — the partitioners read <c>HttpContext.User</c>, and before
    /// authentication has run every caller looks anonymous and shares one address partition.
    /// </summary>
    public static IApplicationBuilder UseShare7RateLimiting(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

        // With the middleware absent, the [EnableRateLimiting] attributes are simply inert — so
        // switching this off needs no other change anywhere.
        return options.Enabled ? app.UseRateLimiter() : app;
    }

    /// <summary>
    /// The user id when there is one, the address otherwise. Prefixed so the two can never
    /// collide: an address is not a Guid today, but a partition key should not depend on that
    /// staying true.
    /// </summary>
    private static string PartitionKeyFor(HttpContext context, RateLimitOptions options) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } userId
            ? $"user:{userId}"
            : $"addr:{ClientAddressFor(context, options)}";

    private static string ClientAddressFor(HttpContext context, RateLimitOptions options)
    {
        if (options.TrustForwardedForHeader
            && context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            // Leftmost entry is the original client; the rest are proxy hops appended on the way in.
            var client = forwarded.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(client))
                return client;
        }

        // A null address means no socket, which in practice means an in-memory test host. Everything
        // without one shares a partition: over-restrictive for a case that does not arise in
        // production, rather than a hole that opens whenever the address is unavailable.
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
