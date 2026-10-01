using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Share7.Application.Feed;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Organizations;
using Share7.Infrastructure.Feed;
using Share7.Infrastructure.Multiplayer;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Social;
using MSOptions = Microsoft.Extensions.Options.Options;

namespace Share7.Tests.Infrastructure;

/// <summary>The social layer and the feed, wired as production wires them.</summary>
public static class SocialTest
{
    /// <summary>An active school with one active class holding these learners (and, optionally, a teacher).</summary>
    public static async Task<Guid> ClassAsync(ApplicationDbContext context, Guid[] learners, Guid? teacher = null)
    {
        var now = DateTime.UtcNow;
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            OrgKey = $"school_{Guid.NewGuid():N}"[..30],
            Kind = OrganizationKind.School,
            Name = "Test School",
            CountryCode = "EG",
            Status = OrganizationStatus.Active,
            CreatedAtUtc = now
        };

        var cohort = new Cohort
        {
            Id = Guid.NewGuid(),
            OrgId = org.Id,
            Name = "6B",
            AcademicPeriod = "2026/2027",
            Status = CohortStatus.Active,
            CreatedAtUtc = now
        };

        context.Organizations.Add(org);
        context.Cohorts.Add(cohort);

        foreach (var learner in learners)
            context.CohortMemberships.Add(new CohortMembership
            {
                Id = Guid.NewGuid(), CohortId = cohort.Id, UserId = learner, Role = CohortRole.Learner, JoinedAtUtc = now
            });

        if (teacher is { } teacherId)
            context.CohortMemberships.Add(new CohortMembership
            {
                Id = Guid.NewGuid(), CohortId = cohort.Id, UserId = teacherId, Role = CohortRole.Teacher, JoinedAtUtc = now
            });

        await context.SaveChangesAsync();
        return cohort.Id;
    }

    /// <summary>Two classmates, freshly created.</summary>
    public static async Task<(Guid A, Guid B)> ClassmatesAsync(ApplicationDbContext context)
    {
        var a = await TestData.CreateUserAsync(context);
        var b = await TestData.CreateUserAsync(context);
        await ClassAsync(context, [a, b]);
        return (a, b);
    }

    public static SocialPolicy Policy(ApplicationDbContext context) =>
        new(context, new BlockList(context), new FriendGraph(context, new SocialConsent(context)));

    public static FriendService Friends(ApplicationDbContext context)
    {
        var options = MSOptions.Create(MultiplayerTest.Options());

        return new FriendService(
            context, new SocialConsent(context), new BlockList(context), MultiplayerTest.Names(context, options),
            Publisher(context), NullLogger<FriendService>.Instance);
    }

    /// <summary>A verified guardian link granting (or not) <c>SocialPlay</c> for this learner.</summary>
    public static async Task GuardianConsentAsync(ApplicationDbContext context, Guid learner, bool socialPlay = true)
    {
        var guardian = await TestData.CreateUserAsync(context);

        context.GuardianLinks.Add(new GuardianLink
        {
            Id = Guid.NewGuid(),
            GuardianUserId = guardian,
            LearnerUserId = learner,
            Relationship = GuardianRelationship.Parent,
            VerifiedAtUtc = DateTime.UtcNow,
            ConsentScope = socialPlay ? GuardianConsentScope.SocialPlay : GuardianConsentScope.ViewProgress,
            CreatedAtUtc = DateTime.UtcNow
        });

        await context.SaveChangesAsync();
    }

    public static PresenceService Presence(ApplicationDbContext context, MultiplayerOptions? options = null) =>
        new(context, new MemoryCache(new MemoryCacheOptions()), MSOptions.Create(options ?? MultiplayerTest.Options()));

    public static PlayerEventPublisher Publisher(ApplicationDbContext context, MultiplayerOptions? options = null) =>
        new(context, MSOptions.Create(options ?? MultiplayerTest.Options()));

    public static SocialService Social(ApplicationDbContext context)
    {
        var options = MSOptions.Create(MultiplayerTest.Options());

        return new SocialService(
            context, Policy(context), Presence(context), MultiplayerTest.Names(context, options),
            Publisher(context), NullLogger<SocialService>.Instance);
    }

    public static SessionInvitationService Invitations(ApplicationDbContext context, MultiplayerOptions? options = null)
    {
        var resolved = options ?? MultiplayerTest.Options();
        var wrapped = MSOptions.Create(resolved);

        return new SessionInvitationService(
            context, MultiplayerTest.Sessions(context, resolved), Policy(context), MultiplayerTest.Names(context, wrapped),
            Publisher(context, resolved), wrapped, NullLogger<SessionInvitationService>.Instance);
    }

    public static ChallengeService Challenges(ApplicationDbContext context, MultiplayerOptions? options = null)
    {
        var resolved = options ?? MultiplayerTest.Options();
        var wrapped = MSOptions.Create(resolved);

        return new ChallengeService(
            context, Policy(context), EngineTest.Unlocks(context), MultiplayerTest.Names(context, wrapped),
            Publisher(context, resolved), MultiplayerTest.Sessions(context, resolved), Invitations(context, resolved),
            NullLogger<ChallengeService>.Instance);
    }

    public static PartyService Parties(ApplicationDbContext context, MultiplayerOptions? options = null)
    {
        var resolved = options ?? MultiplayerTest.Options();
        var wrapped = MSOptions.Create(resolved);

        return new PartyService(
            context, Policy(context), Presence(context, resolved), MultiplayerTest.Names(context, wrapped),
            Publisher(context, resolved), MultiplayerTest.Sessions(context, resolved), wrapped,
            NullLogger<PartyService>.Instance);
    }

    public static PlayerEventFeed Feed(ApplicationDbContext context, PlayerEventSignal? signal = null, MultiplayerOptions? options = null)
    {
        var resolved = options ?? MultiplayerTest.Options();
        return new PlayerEventFeed(context, signal ?? new PlayerEventSignal(), Presence(context, resolved), MSOptions.Create(resolved));
    }

    /// <summary>A context with the feed's commit interceptors installed, as production has.</summary>
    public static ApplicationDbContext CreateSignallingContext(this SqlServerFixture fixture, PlayerEventSignal signal)
    {
        var tracker = new PlayerEventCommitTracker(signal);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(fixture.ConnectionString)
            .AddInterceptors(new PlayerEventSaveInterceptor(tracker), new PlayerEventTransactionInterceptor(tracker))
            .Options;

        return new ApplicationDbContext(options);
    }

    /// <summary>Everything currently on a player's feed.</summary>
    public static async Task<List<PlayerEventDto>> EventsAsync(ApplicationDbContext context, Guid userId, string? type = null)
    {
        var page = await Feed(context).ReadAsync(userId, after: 0, waitSeconds: 0);
        return page.Value!.Events.Where(e => type is null || e.Type == type).ToList();
    }

    public static string? Text(this PlayerEventDto e, string property) =>
        e.Payload.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
