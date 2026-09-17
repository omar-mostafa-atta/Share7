using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
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
builder.Services.AddShare7RateLimiting(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
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
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var path = context.Context.Request.Path.Value;

        if (path is not null &&
            (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)))
        {
            context.Context.Response.Headers.CacheControl = "no-cache";
        }
    }
});

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
app.MapFallbackToFile("{*path:nonfile}", "/index.html");

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.MigrateAsync();

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

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var seedConfig = app.Configuration.GetSection("SeedAdmin");
    var adminUsername = seedConfig["Username"] ?? "admin";
    var adminEmail = seedConfig["Email"] ?? "admin@admin.com";
    var adminPassword = seedConfig["Password"] ?? "Admin123";

    var adminUser = await userManager.FindByNameAsync(adminUsername);

    if (adminUser is null)
    {
        var legacyAdmin = await userManager.FindByNameAsync(adminEmail);
        if (legacyAdmin is not null)
        {
            await userManager.SetUserNameAsync(legacyAdmin, adminUsername);
            adminUser = legacyAdmin;
        }
    }

    if (adminUser is null)
    {
        adminUser = new ApplicationUser
        {
            UserName = adminUsername,
            Email = adminEmail,
            EmailConfirmed = true,
            PreferredLanguageId = LanguageIds.English
        };

        var createResult = await userManager.CreateAsync(adminUser, adminPassword);
        if (createResult.Succeeded)
            await userManager.AddToRoleAsync(adminUser, Roles.Admin);
    }
    else
    {
        if (adminUser.PreferredLanguageId is null)
        {
            adminUser.PreferredLanguageId = LanguageIds.English;
            await userManager.UpdateAsync(adminUser);
        }

        if (!await userManager.IsInRoleAsync(adminUser, Roles.Admin))
            await userManager.AddToRoleAsync(adminUser, Roles.Admin);
    }
}

app.Run();
