using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Feed;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Social;
using Share7.Domain.Social;
using Share7.Infrastructure.Persistence;
namespace Share7.Infrastructure.Social;
public sealed class PlayerTeamService(ApplicationDbContext db, ISocialPolicy policy, IDisplayNameService names,
    IBlockList blocks, IPlayerEventPublisher events) : IPlayerTeamService
{
    public const int Capacity = 8;
    public const int MaxTeams = 5;
    public async Task<IReadOnlyList<PlayerTeamDto>> ListAsync(Guid user, CancellationToken token = default)
    {
        var teams = await db.PlayerTeams.AsNoTracking().Where(t => db.PlayerTeamMembers.Any(m => m.TeamId == t.Id && m.UserId == user))
            .OrderByDescending(t => t.CreatedAtUtc).Take(MaxTeams).ToListAsync(token);
        return await MapAsync(user, teams, token);
    }
    public async Task<ServiceResult<PlayerTeamDto>> CreateAsync(Guid user, string requestId, CancellationToken token = default)
    {
        if (!SocialProfileAdminService.Key(requestId, 64)) return ServiceResult<PlayerTeamDto>.Invalid("Provide a request key.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await GamingProfileService.LockUserAsync(db, user, token);
        var old = await db.PlayerTeams.AsNoTracking().FirstOrDefaultAsync(t => t.OwnerUserId == user && t.RequestId == requestId, token);
        if (old is not null) return ServiceResult<PlayerTeamDto>.Success((await MapAsync(user, [old], token))[0]);
        if (await Restricted(user, token) || await db.PlayerTeamMembers.CountAsync(m => m.UserId == user, token) >= MaxTeams)
            return ServiceResult<PlayerTeamDto>.Forbidden("Team unavailable.");
        var team = new PlayerTeam { Id = Guid.NewGuid(), OwnerUserId = user, RequestId = requestId, CreatedAtUtc = DateTime.UtcNow };
        db.PlayerTeams.Add(team); db.PlayerTeamMembers.Add(new() { TeamId = team.Id, UserId = user, Accepted = true,
            InvitedAtUtc = DateTime.UtcNow, AcceptedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return ServiceResult<PlayerTeamDto>.Success((await MapAsync(user, [team], token))[0]);
    }
    public async Task<ServiceResult> InviteAsync(Guid user, Guid team, Guid target, CancellationToken token = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        // Team -> account, consistently across invitation and acceptance.
        await LockTeam(team, token); await GamingProfileService.LockUserAsync(db, target, token);
        if (!await db.PlayerTeams.AnyAsync(t => t.Id == team && t.OwnerUserId == user, token)
            || !(await policy.CanInteractAsync(user, target, SocialAction.Invite, token)).Allowed) return ServiceResult.NotFound("Team unavailable.");
        if (await db.PlayerTeamMembers.AnyAsync(m => m.TeamId == team && m.UserId == target, token)) return ServiceResult.Success();
        if (await db.PlayerTeamMembers.CountAsync(m => m.TeamId == team, token) >= Capacity
            || await db.PlayerTeamMembers.CountAsync(m => m.UserId == target, token) >= MaxTeams) return ServiceResult.Conflict("Team capacity reached.");
        db.PlayerTeamMembers.Add(new() { TeamId = team, UserId = target, InvitedAtUtc = DateTime.UtcNow });
        events.Stage(target, "social.team.invited", new { teamId = team, fromUserId = user });
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return ServiceResult.Success();
    }
    public async Task<ServiceResult> AcceptAsync(Guid user, Guid team, CancellationToken token = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await LockTeam(team, token); await GamingProfileService.LockUserAsync(db, user, token);
        var owner = await db.PlayerTeams.Where(t => t.Id == team).Select(t => (Guid?)t.OwnerUserId).FirstOrDefaultAsync(token);
        var member = await db.PlayerTeamMembers.FirstOrDefaultAsync(m => m.TeamId == team && m.UserId == user, token);
        if (owner is null || member is null) return ServiceResult.NotFound("Team unavailable.");
        if (member.Accepted) return ServiceResult.Success();
        if (!(await policy.CanInteractAsync(user, owner.Value, SocialAction.Invite, token)).Allowed) return ServiceResult.NotFound("Team unavailable.");
        member.Accepted = true; member.AcceptedAtUtc = DateTime.UtcNow;
        events.Stage(owner.Value, "social.team.accepted", new { teamId = team, fromUserId = user });
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return ServiceResult.Success();
    }
    public async Task<ServiceResult> RemoveAsync(Guid user, Guid team, Guid target, CancellationToken token = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token); await LockTeam(team, token);
        var owner = await db.PlayerTeams.Where(t => t.Id == team).Select(t => (Guid?)t.OwnerUserId).FirstOrDefaultAsync(token);
        if (owner is null || (user != owner && user != target) || target == owner
            || !await db.PlayerTeamMembers.AnyAsync(m => m.TeamId == team && m.UserId == user, token)) return ServiceResult.NotFound("Team unavailable.");
        await db.PlayerTeamMembers.Where(m => m.TeamId == team && m.UserId == target).ExecuteDeleteAsync(token);
        await transaction.CommitAsync(token); return ServiceResult.Success();
    }
    public async Task<ServiceResult> DisbandAsync(Guid user, Guid team, CancellationToken token = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token); await LockTeam(team, token);
        var count = await db.PlayerTeams.Where(t => t.Id == team && t.OwnerUserId == user).ExecuteDeleteAsync(token);
        await transaction.CommitAsync(token); return count > 0 ? ServiceResult.Success() : ServiceResult.NotFound("Team unavailable.");
    }
    private Task<bool> Restricted(Guid user, CancellationToken token) => db.SocialRestrictions.AnyAsync(r => r.UserId == user
        && r.RevokedAtUtc == null && r.StartsAtUtc <= DateTime.UtcNow && (r.ExpiresAtUtc == null || r.ExpiresAtUtc > DateTime.UtcNow), token);
    private async Task LockTeam(Guid team, CancellationToken token) => await db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT [Id] FROM [PlayerTeams] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id] = {team}", token);
    private async Task<IReadOnlyList<PlayerTeamDto>> MapAsync(Guid user, IReadOnlyList<PlayerTeam> teams, CancellationToken token)
    {
        var ids = teams.Select(t => t.Id).ToArray(); var blocked = await blocks.BlockedEitherWayAsync(user, token);
        var members = await db.PlayerTeamMembers.AsNoTracking().Where(m => ids.Contains(m.TeamId)).ToListAsync(token);
        var handles = await names.EnsureHandlesAsync(members.Where(m => !blocked.Contains(m.UserId)).Select(m => m.UserId).Distinct().ToArray(), token);
        return teams.Select(t => new PlayerTeamDto(t.Id, t.OwnerUserId, members.Where(m => m.TeamId == t.Id)
            .Select(m => new TeamMemberDto(m.UserId, blocked.Contains(m.UserId) ? null : handles.GetValueOrDefault(m.UserId), m.Accepted, m.UserId == t.OwnerUserId)).ToArray(),
            DateTime.SpecifyKind(t.CreatedAtUtc, DateTimeKind.Utc), user == t.OwnerUserId)).ToArray();
    }
}
