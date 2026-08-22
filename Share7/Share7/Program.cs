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
using Share7.Infrastructure.Persistence;

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
// `/` resolves to wwwroot/index.html — the admin console. Must precede UseStaticFiles.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// After authentication on purpose: the limiter partitions by user id where there is one, and a
// caller whose token has not been read yet is indistinguishable from an anonymous one.
app.UseShare7RateLimiting();

app.MapControllers();

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
