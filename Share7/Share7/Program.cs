using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Share7.API.Authorization;
using Share7.API.Hosting;
using Share7.API.RateLimiting;
using Share7.API.Services;
using Share7.Application;
using Share7.Application.Common.Interfaces;
using Share7.Domain.Constants;
using Share7.Infrastructure;
using Share7.Infrastructure.Identity;
using Share7.Application.Telemetry.Interfaces;
using Share7.Infrastructure.Persistence;
using Share7.Application.Admin.Interfaces;
using Share7.Application.Admin.Models;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.CustomSchemaIds(SchemaIds.For);
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Who the audit trail names for a request. Registered after AddInfrastructure, so it replaces the
// platform-as-actor default there; outside a request it reads as the platform anyway.
builder.Services.AddScoped<Share7.Application.Audit.Interfaces.IAuditActor, HttpAuditActor>();
builder.Services.AddShare7RateLimiting(builder.Configuration);

// Named policies for the endpoints more than the admins can reach — see Authorization/Policies.cs.
builder.Services.AddShare7Authorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

// The Content Studio at /studio, when it has been built into wwwroot-studio. Before the console's
// static files and fallback, so /studio never answers with the Admin Console. See StudioHosting.
app.UseStudioHosting();

// `/` resolves to wwwroot/index.html — the console's SPA shell. Must precede UseStaticFiles.
app.UseDefaultFiles();

// `Cache-Control: no-cache` on the console's own assets — which means "revalidate", not "do not
// store". Without it these responses carry only ETag and Last-Modified, and a response with no
// explicit freshness lets a browser invent one: roughly a tenth of the file's age, so an admin
// who loads a three-day-old nav.js is served it from disk for the next several hours without
// the server ever being asked. That is indistinguishable from a deploy that did not take, and it
// cost an afternoon once already.
//
// Revalidation is nearly free because the ETag is still sent: the browser asks, and the answer is
// a 304 with no body until the file genuinely changes. Applied only to the console's own source —
// anything fingerprinted can be cached hard, but nothing here is.
//
// Held in a variable because MapFallbackToFile below must be handed the SAME options. Without
// them it builds its own static-file pipeline with defaults, so the SPA shell served for `/`,
// `/login` and every other client route went out with no Cache-Control at all — only
// `/index.html` asked for by name got `no-cache`. The browser then kept serving a shell that
// pointed at a days-old bundle, which is how a content-team account typing `/content` once met a
// sign-in screen from before the portal existed and was told it was "not an Admin or SuperAdmin".
// (That route now lands on the page saying authoring moved; the caching bug it exposed is what
// this block fixes.) Keyed on the file served rather than the request path for the same reason —
// on the fallback the path is the client route.
var consoleStaticFiles = new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var name = context.File.Name;

        if (name.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers.CacheControl = "no-cache";
        }
    }
};

app.UseStaticFiles(consoleStaticFiles);

app.UseAuthentication();
app.UseAuthorization();

// After authentication on purpose: the limiter partitions by user id where there is one, and a
// caller whose token has not been read yet is indistinguishable from an anonymous one.
app.UseShare7RateLimiting();

app.MapControllers();

// The console (Share7.Web) builds into wwwroot and routes on the client, so a hard refresh on
// /currencies asks the server for a file that does not exist. Without this it is a 404; with it the
// SPA shell is returned and the router resolves the path.
//
// Unscoped now that this is the only console — it used to be limited to /app/ so that the
// hand-written one at / was left alone. `:nonfile` still keeps real assets going to UseStaticFiles
// above, and MapControllers has already claimed /api, so neither is swallowed by the shell.
app.MapFallbackToFile("{*path:nonfile}", "/index.html", consoleStaticFiles);

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // The engine backfill (20260922145343_EngineAuthoritative) copies every existing lesson into
    // the node tree and the item bank in one statement. On a database with real content that is
    // minutes of work, and the provider's 30-second default kills it part way through — the
    // migration rolls back, the process exits, and the next start tries the whole thing again.
    // Migrations are a startup step nothing is waiting on, so they get an hour.
    dbContext.Database.SetCommandTimeout(TimeSpan.FromHours(1));
    await dbContext.Database.MigrateAsync();
    dbContext.Database.SetCommandTimeout(null);

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
    foreach (var roleName in Roles.All)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
            await roleManager.CreateAsync(new ApplicationRole(roleName));
    }

    // The telemetry vocabulary, before the first client can connect. Without it every event on a
    // fresh database lands as "unregistered" — stored, but folded into no rollup — and the console
    // shows an empty dashboard next to a full raw table. Additive: a name that already has a row is
    // left exactly as an operator authored it.
    var telemetrySchemas = scope.ServiceProvider.GetRequiredService<ITelemetrySchemaService>();
    await telemetrySchemas.SeedAsync(CancellationToken.None);

    // Content: catalogues, curriculum, questions. Gated twice over — the master switch and an
    // explicit opt-in to running it here — because a fresh production database needs this and a
    // deployment that already has content must never have it appear underneath. When RunOnStartup
    // is off the seeder is still reachable at POST /api/admin/seed, which is what a deployment
    // usually wants: a hundred thousand inserts do not belong on the path to the first request.
    var seedOptions = scope.ServiceProvider.GetRequiredService<IOptions<ContentSeedOptions>>().Value;
    if (seedOptions is { Enabled: true, RunOnStartup: true })
    {
        var contentSeeder = scope.ServiceProvider.GetRequiredService<IContentSeeder>();
        await contentSeeder.SeedAsync(CancellationToken.None);
    }

    // The platform's own accounts — see IdentitySeeder. In Production the seed admin is never
    // created with the built-in default password, and the first SuperAdmin comes from the one-time
    // SeedSuperAdmin section, which is ignored as soon as any SuperAdmin exists.
    var identitySeeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();

    await identitySeeder.SeedAdminAsync(
        app.Configuration.GetSection("SeedAdmin").Get<SeedAccountOptions>() ?? new SeedAccountOptions(),
        isProduction: app.Environment.IsProduction());

    await identitySeeder.BootstrapSuperAdminAsync(
        app.Configuration.GetSection("SeedSuperAdmin").Get<SeedAccountOptions>());
}

app.Run();
