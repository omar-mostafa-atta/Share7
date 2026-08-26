using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Admin.Interfaces;
using Share7.Application.Admin.Models;
using Share7.Domain.Constants;
using Share7.Domain.Economy;
using Share7.Domain.Entities;
using Share7.Domain.Leaderboards;
using Share7.Domain.Objectives;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Seeding;

/// <summary>
/// Creates demo students so the social surfaces have something in them: balances, display names,
/// streaks and ranked leaderboard entries.
/// <para>
/// <b>Why fake players at all.</b> A leaderboard with one account on it does not exercise ranking,
/// paging, cohort filtering or the "you are 14th" band, and neither does a wallet with no peers. The
/// screens are only testable against a populated board.
/// </para>
/// <para>
/// <b>They are listed under generated handles carrying nothing personal</b> — <c>Runner-04</c> — for
/// the same reason real children are: a leaderboard is a public surface on a product for minors, and
/// no name, email, age or grade belongs on it. The <c>StudentProfile</c> rows exist so the grade
/// cohort resolves, and they never reach the wire.
/// </para>
/// <para>
/// <b>Off unless an environment asks for it.</b> These are real Identity accounts sharing one known
/// password; that is fine on a laptop and is a liability anywhere else.
/// </para>
/// </summary>
internal sealed class DemoPlayerSeeder
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ContentSeedOptions _options;

    public DemoPlayerSeeder(
        ApplicationDbContext db, UserManager<ApplicationUser> users, ContentSeedOptions options)
    {
        _db = db;
        _users = users;
        _options = options;
    }

    public async Task SeedAsync(ContentSeedReport report, CancellationToken ct)
    {
        var grades = await _db.Grades.OrderBy(g => g.Order).Select(g => g.Id).ToListAsync(ct);
        if (grades.Count == 0) return;

        var currencies = await _db.Currencies.ToDictionaryAsync(c => c.Key, c => c.Id, StringComparer.Ordinal, ct);
        var now = DateTime.UtcNow;
        var players = new List<(Guid UserId, Guid GradeId, string Handle, int Index)>();

        for (var i = 1; i <= _options.DemoPlayerCount; i++)
        {
            var userName = $"demo.student{i:D2}";
            var handle = $"Runner-{i:D2}";
            var gradeId = grades[(i - 1) % grades.Count];

            var user = await _users.FindByNameAsync(userName);

            if (user is null)
            {
                user = new ApplicationUser
                {
                    Id = SeedId.For("demo-user", userName),
                    UserName = userName,
                    Email = $"{userName}@example.invalid",
                    EmailConfirmed = true,
                    PreferredLanguageId = i % 2 == 0 ? LanguageIds.Arabic : LanguageIds.English,
                    CreatedAt = now
                };

                var created = await _users.CreateAsync(user, _options.DemoPlayerPassword);
                if (!created.Succeeded) continue;

                await _users.AddToRoleAsync(user, Roles.Student);
                report.DemoPlayers++;
            }

            players.Add((user.Id, gradeId, handle, i));
        }

        if (players.Count == 0) return;

        await ProfilesAsync(players, now, ct);
        await WalletsAsync(players, currencies, now, ct);
        await HandlesAsync(players, now, ct);
        await StreaksAsync(players, now, ct);
        await EntriesAsync(players, report, now, ct);

        await _db.SaveChangesAsync(ct);
    }

    private async Task ProfilesAsync(
        List<(Guid UserId, Guid GradeId, string Handle, int Index)> players, DateTime now, CancellationToken ct)
    {
        var have = await _db.StudentProfiles.Select(p => p.UserId).ToListAsync(ct);

        foreach (var player in players)
        {
            if (have.Contains(player.UserId)) continue;

            _db.StudentProfiles.Add(new StudentProfile
            {
                Id = SeedId.For("demo-profile", player.UserId.ToString()),
                UserId = player.UserId,
                FullName = $"Demo Student {player.Index:D2}",
                Age = 6 + player.Index % 12,
                PhoneNumber = string.Empty,
                Email = null,
                GradeId = player.GradeId,
                CreatedAt = now
            });
        }
    }

    private async Task WalletsAsync(
        List<(Guid UserId, Guid GradeId, string Handle, int Index)> players,
        Dictionary<string, Guid> currencies, DateTime now, CancellationToken ct)
    {
        var have = (await _db.UserCurrencyBalances.Select(b => new { b.UserId, b.CurrencyId }).ToListAsync(ct))
            .Select(b => (b.UserId, b.CurrencyId)).ToHashSet();

        foreach (var player in players)
        {
            // A spread rather than a constant, so the level badge and the wallet both have something
            // to show and the ordering on a board is not an artefact of insertion order.
            Add(player.UserId, CurrencyKeys.Xp, 120 + player.Index * 95);
            Add(player.UserId, "coins", 300 + player.Index * 140);
            Add(player.UserId, "gems", player.Index % 5 * 10);
        }

        void Add(Guid userId, string currencyKey, long amount)
        {
            if (!currencies.TryGetValue(currencyKey, out var currencyId)) return;
            if (have.Contains((userId, currencyId))) return;

            _db.UserCurrencyBalances.Add(new UserCurrencyBalance
            {
                Id = SeedId.For("demo-balance", userId.ToString(), currencyKey),
                UserId = userId,
                CurrencyId = currencyId,
                Amount = amount,
                UpdatedAtUtc = now
            });
        }
    }

    private async Task HandlesAsync(
        List<(Guid UserId, Guid GradeId, string Handle, int Index)> players, DateTime now, CancellationToken ct)
    {
        var have = await _db.PlayerDisplayNames.Select(n => n.UserId).ToListAsync(ct);

        foreach (var player in players)
        {
            if (have.Contains(player.UserId)) continue;

            _db.PlayerDisplayNames.Add(new PlayerDisplayName
            {
                UserId = player.UserId,
                Handle = player.Handle,
                Source = DisplayNameSource.Generated,
                IsHidden = false,
                IsHiddenByGuardian = false,
                CreatedAtUtc = now
            });
        }
    }

    private async Task StreaksAsync(
        List<(Guid UserId, Guid GradeId, string Handle, int Index)> players, DateTime now, CancellationToken ct)
    {
        var have = (await _db.UserStreaks.Select(s => new { s.UserId, s.StreakKey }).ToListAsync(ct))
            .Select(s => (s.UserId, s.StreakKey)).ToHashSet();

        var cycle = ObjectiveCycle.KeyFor(ObjectiveKind.Daily, now);

        foreach (var player in players)
        {
            if (have.Contains((player.UserId, StreakKeys.Daily))) continue;

            var current = player.Index % 9;

            _db.UserStreaks.Add(new UserStreak
            {
                UserId = player.UserId,
                StreakKey = StreakKeys.Daily,
                Current = current,
                Best = current + player.Index % 5,
                LastCycleKey = cycle,
                FreezesRemaining = 1,
                UpdatedAtUtc = now
            });
        }
    }

    /// <summary>
    /// Ranked entries on every open cycle.
    /// <para>
    /// Written straight to the entry table rather than pushed through <c>GameResults</c> and the
    /// projector. The projector is the path a real result takes and it is not this seeder's to
    /// simulate — what the screens need is a populated, correctly ranked board, and inventing a
    /// thousand plausible game results to get one would be a larger fiction than the entries are.
    /// </para>
    /// </summary>
    private async Task EntriesAsync(
        List<(Guid UserId, Guid GradeId, string Handle, int Index)> players,
        ContentSeedReport report, DateTime now, CancellationToken ct)
    {
        var cycles = await _db.LeaderboardCycles
            .Where(c => c.State == LeaderboardCycleState.Open)
            .Select(c => new { c.Id, c.BoardId })
            .ToListAsync(ct);

        if (cycles.Count == 0) return;

        var have = (await _db.LeaderboardEntries
                .Select(e => new { e.CycleId, e.Cohort, e.CohortKey, e.UserId })
                .ToListAsync(ct))
            .Select(e => (e.CycleId, e.Cohort, e.CohortKey, e.UserId)).ToHashSet();

        foreach (var cycle in cycles)
        {
            // Value descends with the player index so the ranks below are the real ordering rather
            // than a number that disagrees with the column it sits next to.
            var ranked = players
                .Select(p => (Player: p, Value: (long)(players.Count - p.Index + 1) * 25 + p.Index % 7))
                .OrderByDescending(x => x.Value)
                .ToList();

            for (var i = 0; i < ranked.Count; i++)
            {
                var (player, value) = ranked[i];
                AddEntry(cycle.Id, LeaderboardCohort.All, Guid.Empty, player, value, i + 1);
            }

            // Rank is per cohort, not global: being fourth overall and first in your grade are both
            // true, and a grade row carrying the overall rank would contradict its own ordering.
            foreach (var grade in ranked.GroupBy(x => x.Player.GradeId))
            {
                var rank = 1;
                foreach (var (player, value) in grade)
                    AddEntry(cycle.Id, LeaderboardCohort.Grade, grade.Key, player, value, rank++);
            }
        }

        void AddEntry(
            Guid cycleId, LeaderboardCohort cohort, Guid cohortKey,
            (Guid UserId, Guid GradeId, string Handle, int Index) player, long value, int rank)
        {
            if (have.Contains((cycleId, cohort, cohortKey, player.UserId))) return;

            _db.LeaderboardEntries.Add(new LeaderboardEntry
            {
                Id = SeedId.For("demo-entry", cycleId.ToString(), cohort.ToString(),
                    cohortKey.ToString(), player.UserId.ToString()),
                CycleId = cycleId,
                Cohort = cohort,
                CohortKey = cohortKey,
                UserId = player.UserId,
                Value = value,
                AchievedAtUtc = now,
                Rank = rank,
                DisplayName = player.Handle,
                AvatarKey = null,
                IsHidden = false,
                IsFlagged = false,
                LastResultId = null,
                UpdatedAtUtc = now
            });

            report.LeaderboardEntries++;
        }
    }
}
