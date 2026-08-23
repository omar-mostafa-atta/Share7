using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Share7.Application.Auth.Interfaces;
using Share7.Application.Commerce.Interfaces;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Economy.Interfaces;
using Share7.Application.Equipment.Interfaces;
using Share7.Application.Equipment.Models;
using Share7.Application.Games.Interfaces;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Application.Progress.Interfaces;
using Share7.Application.Rewards.Interfaces;
using Share7.Application.Users.Interfaces;
using Share7.Infrastructure.Commerce;
using Share7.Infrastructure.Curriculum;
using Share7.Infrastructure.Economy;
using Share7.Infrastructure.Equipment;
using Share7.Infrastructure.Users;
using Share7.Infrastructure.Games;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Multiplayer;
using Share7.Infrastructure.Identity.ExternalAuth;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Leaderboards.Models;
using Share7.Infrastructure.Leaderboards;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Progress;
using Share7.Infrastructure.Rewards;

namespace Share7.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));
        var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>() ?? new JwtSettings();

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
                    ClockSkew = TimeSpan.Zero
                };
            });

        services.AddHttpClient();

        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IExternalLoginValidator, GoogleLoginValidator>();
        services.AddScoped<IExternalLoginValidator, FacebookLoginValidator>();
        services.AddScoped<IGradeService, GradeService>();
        services.AddScoped<ILanguageService, LanguageService>();
        services.AddScoped<ICurriculumService, CurriculumService>();
        services.AddScoped<ICurriculumAdminService, CurriculumAdminService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<ILessonQuestionService, LessonQuestionService>();
        services.AddScoped<IQuestionImportService, QuestionImportService>();
        services.AddScoped<ILessonRecoveryQuestionService, LessonRecoveryQuestionService>();
        services.AddScoped<IRecoveryQuestionImportService, RecoveryQuestionImportService>();
        services.AddScoped<IGameService, GameService>();
        services.AddScoped<IGameAdminService, GameAdminService>();
        services.AddScoped<IUnlockService, UnlockService>();
        services.AddScoped<IProgressService, ProgressService>();
        services.AddScoped<IWalletService, WalletService>();
        services.AddScoped<ICurrencyAdminService, CurrencyAdminService>();
        services.AddScoped<IRewardService, RewardService>();
        services.AddScoped<IRewardAdminService, RewardAdminService>();
        services.AddScoped<IEntitlementService, EntitlementService>();
        services.AddScoped<IProductKindAdminService, ProductKindAdminService>();
        services.AddScoped<IProductAdminService, ProductAdminService>();
        services.AddScoped<IProductGrantAdminService, ProductGrantAdminService>();
        services.AddScoped<IOfferService, OfferService>();
        services.AddScoped<IOfferAdminService, OfferAdminService>();
        services.AddScoped<IPurchaseService, PurchaseService>();
        services.AddScoped<IAccountDeletionService, AccountDeletionService>();
        services.AddScoped<IUserProfileService, UserProfileService>();

        services.Configure<EquipmentOptions>(configuration.GetSection(EquipmentOptions.SectionName));
        services.AddScoped<IEquipmentService, EquipmentService>();

        services.Configure<MultiplayerOptions>(configuration.GetSection(MultiplayerOptions.SectionName));
        services.AddScoped<MultiplayerRequestLogStore>();

        // Registered concretely and then forwarded, so matchmaking and the interface resolve to the
        // *same* scoped instance. Matchmaking reuses the session service's seating path rather than
        // owning a second copy of the capacity rules — two implementations is how the direct join
        // and the matchmade join would come to disagree about what "full" means.
        services.AddScoped<MultiplayerSessionService>();
        services.AddScoped<IMultiplayerSessionService>(sp => sp.GetRequiredService<MultiplayerSessionService>());
        services.AddScoped<IMatchmakingService, MatchmakingService>();
        services.AddScoped<IMultiplayerAdminService, MultiplayerAdminService>();

        services.AddScoped<IMultiplayerSweepService, MultiplayerSweepService>();
        services.AddHostedService<MultiplayerSessionSweeper>();

        // Leaderboards. Note there is no ILeaderboardWriteService and there must never be one:
        // ranking is projected from results the server graded, so the only write seam is
        // IGameResultRecorder, which no controller can reach.
        services.Configure<LeaderboardOptions>(configuration.GetSection(LeaderboardOptions.SectionName));
        services.AddScoped<IDisplayNameService, DisplayNameService>();
        services.AddScoped<IPlausibilityGuard, PlausibilityGuard>();
        services.AddScoped<IGameResultRecorder, GameResultRecorder>();
        services.AddScoped<ILeaderboardProjector, LeaderboardProjector>();
        services.AddScoped<ILeaderboardRolloverService, LeaderboardRolloverService>();
        services.AddScoped<ILeaderboardSettlementService, LeaderboardSettlementService>();
        services.AddScoped<ILeaderboardJobRunner, LeaderboardJobRunner>();
        services.AddScoped<ILeaderboardService, LeaderboardService>();
        services.AddScoped<ILeaderboardAdminService, LeaderboardAdminService>();

        return services;
    }
}
