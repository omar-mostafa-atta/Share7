using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Auth.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Economy.Interfaces;
using Share7.Application.Objectives.Interfaces;
using Share7.Application.Progression.Interfaces;
using Share7.Application.Progression.Models;
using Share7.Application.Users.Models;
using Share7.Domain.Constants;
using Share7.Domain.Progress;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Users;

namespace Share7.Infrastructure.Identity;

public class UserAdminService : IUserAdminService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _dbContext;

    // Composed, not reimplemented. Each of these already resolves state for an
    // arbitrary userId; only the controllers were ever /me-scoped.
    private readonly IWalletService _wallet;
    private readonly ILevelService _levelService;
    private readonly IObjectiveService _objectiveService;

    public UserAdminService(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext dbContext,
        IWalletService wallet,
        ILevelService levelService,
        IObjectiveService objectiveService)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _wallet = wallet;
        _levelService = levelService;
        _objectiveService = objectiveService;
    }

    public async Task<AdminUserPageDto> ListAsync(
        AdminUserQuery query,
        CancellationToken cancellationToken = default)
    {
        // Clamp rather than reject. A console sending page 0 or pageSize 5000 is a bug in
        // the console, not a reason to show the operator an error — and an unbounded page
        // size is a denial-of-service handed to anyone holding an admin token.
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        // Left-joined by hand rather than through a navigation property: ApplicationUser has
        // no StudentProfile navigation, and most accounts have no profile row at all until
        // the child completes onboarding. An inner join here would silently hide every
        // account that has not finished signing up — which is the exact population an admin
        // most often goes looking for.
        var rows =
            from user in _dbContext.Users
            join profile in _dbContext.StudentProfiles
                on user.Id equals profile.UserId into profiles
            from profile in profiles.DefaultIfEmpty()
            select new { user, profile };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // EF translates this to LIKE '%term%'. Not sargable, so it scans — acceptable
            // for an admin-only lookup, and the alternative is full-text indexing that this
            // schema does not have.
            var term = query.Search.Trim();

            rows = rows.Where(r =>
                (r.user.UserName != null && EF.Functions.Like(r.user.UserName, $"%{term}%"))
                || (r.user.Email != null && EF.Functions.Like(r.user.Email, $"%{term}%"))
                || (r.profile != null && EF.Functions.Like(r.profile.FullName, $"%{term}%")));
        }

        if (query.GradeId is { } gradeId)
            rows = rows.Where(r => r.profile != null && r.profile.GradeId == gradeId);

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var role = query.Role.Trim();

            rows = rows.Where(r =>
                _dbContext.UserRoles.Any(ur =>
                    ur.UserId == r.user.Id
                    && _dbContext.Roles.Any(x => x.Id == ur.RoleId && x.Name == role)));
        }

        // Counted before paging, and it is the count of the *filter*, not the page — the
        // console needs it to know how many pages exist.
        var total = await rows.CountAsync(cancellationToken);

        // Newest first: an admin opening this screen is far more often looking for someone
        // who just signed up than for the oldest account in the system. Id is the tiebreaker
        // so the order is total — without it, accounts created in the same tick can swap
        // places between pages and one of them is never shown.
        var pageRows = await rows
            .OrderByDescending(r => r.user.CreatedAt)
            .ThenBy(r => r.user.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                r.user.Id,
                r.user.UserName,
                r.user.Email,
                r.user.CreatedAt,
                FullName = r.profile != null ? r.profile.FullName : null,
                Age = r.profile != null ? (int?)r.profile.Age : null,
                GradeId = r.profile != null ? (Guid?)r.profile.GradeId : null,
                HasProfile = r.profile != null
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var ids = pageRows.Select(r => r.Id).ToList();

        // Roles and last-seen are fetched for the page's ids in one query each, rather than
        // per row. Calling UserManager.GetRolesAsync in the projection loop would be 50
        // round-trips for a 50-row page.
        var rolesByUser = await (
                from userRole in _dbContext.UserRoles
                join role in _dbContext.Roles on userRole.RoleId equals role.Id
                where ids.Contains(userRole.UserId)
                select new { userRole.UserId, role.Name })
            .ToListAsync(cancellationToken);

        var roleLookup = rolesByUser
            .GroupBy(r => r.UserId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(x => x.Name ?? string.Empty)
                    .Where(n => n.Length > 0)
                    .ToList());

        // The schema has no login audit, so the latest run start is the best available
        // proxy for "last seen". Named LastSeenAtUtc rather than LastRunAtUtc because that
        // is the question it answers, and documented as a proxy on the DTO.
        var lastSeen = await _dbContext.Runs
            .Where(r => ids.Contains(r.UserId))
            .GroupBy(r => r.UserId)
            .Select(g => new { UserId = g.Key, LastSeen = g.Max(r => r.StartedAtUtc) })
            .ToDictionaryAsync(x => x.UserId, x => x.LastSeen, cancellationToken);

        var users = pageRows
            .Select(r => new AdminUserListItemDto
            {
                UserId = r.Id,
                UserName = r.UserName ?? string.Empty,
                FullName = r.FullName,
                Email = r.Email,
                Age = r.Age,
                GradeId = r.GradeId,
                Roles = roleLookup.TryGetValue(r.Id, out var roles) ? roles : [],
                IsProfileComplete = r.HasProfile,
                CreatedAtUtc = r.CreatedAt,
                LastSeenAtUtc = lastSeen.TryGetValue(r.Id, out var seen) ? seen : null
            })
            .ToList();

        return new AdminUserPageDto
        {
            Users = users,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    // -----------------------------------------------------------------------
    // Detail reads
    //
    // Admin-scoped views of state a player can already see about themselves. Each
    // one composes services that ALREADY take a userId — IWalletService,
    // ILevelService, IObjectiveService, IEntitlementService — so none of this
    // recomputes anything. The /me restriction lived in the controllers, not in
    // the domain, which is why these are projections rather than new logic.
    // -----------------------------------------------------------------------

    /// <summary>Whether the account exists, resolved once so each read can 404 honestly.</summary>
    private Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken)
        => _dbContext.Users.AnyAsync(u => u.Id == userId, cancellationToken);

    public async Task<ServiceResult<AdminUserDetailDto>> GetDetailAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
            return ServiceResult<AdminUserDetailDto>.NotFound("User not found.");

        var profile = await _dbContext.StudentProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        var roles = await (
                from userRole in _dbContext.UserRoles
                join role in _dbContext.Roles on userRole.RoleId equals role.Id
                where userRole.UserId == userId
                select role.Name)
            .ToListAsync(cancellationToken);

        // The grade's name is language-dependent, so it is resolved against the
        // user's own preferred language rather than the admin's — this is a fact
        // about their account, not about the console's current locale. Falls back
        // to any translation so an unset preference still shows something.
        string? gradeName = null;
        if (profile is not null)
        {
            gradeName = await _dbContext.GradeTranslations
                .AsNoTracking()
                .Where(t => t.GradeId == profile.GradeId)
                .OrderByDescending(t => t.LangId == user.PreferredLanguageId)
                .Select(t => t.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var languageCode = user.PreferredLanguageId is { } langId
            ? await _dbContext.Languages
                .AsNoTracking()
                .Where(l => l.Id == langId)
                .Select(l => l.Code)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var runCount = await _dbContext.Runs.CountAsync(r => r.UserId == userId, cancellationToken);

        var flaggedRunCount = await _dbContext.Runs
            .CountAsync(r => r.UserId == userId && r.IsFlagged, cancellationToken);

        var lastSeen = await _dbContext.Runs
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.StartedAtUtc)
            .Select(r => (DateTime?)r.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var entitlementCount = await _dbContext.Entitlements
            .CountAsync(e => e.UserId == userId, cancellationToken);

        var purchaseCount = await _dbContext.PurchaseTransactions
            .CountAsync(p => p.UserId == userId, cancellationToken);

        // Anything at or above the pass mark counts as completed, which is the same
        // rule the unlock logic uses — Uncompleted is the only state that is not.
        var lessonsCompleted = await _dbContext.UserLessonProgress
            .CountAsync(
                p => p.UserId == userId && p.CompletionState != CompletionState.Uncompleted,
                cancellationToken);

        return ServiceResult<AdminUserDetailDto>.Success(new AdminUserDetailDto
        {
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            FullName = profile?.FullName,
            Email = profile?.Email ?? user.Email,
            PhoneNumber = profile?.PhoneNumber,
            Age = profile?.Age,
            GradeId = profile?.GradeId,
            GradeName = gradeName,
            PreferredLanguageId = user.PreferredLanguageId,
            PreferredLanguageCode = languageCode,
            Roles = roles.Where(r => !string.IsNullOrEmpty(r)).Select(r => r!).ToList(),
            IsProfileComplete = profile is not null,
            CreatedAtUtc = user.CreatedAt,
            UpdatedAtUtc = profile?.UpdatedAt,
            LastSeenAtUtc = lastSeen,
            RunCount = runCount,
            FlaggedRunCount = flaggedRunCount,
            EntitlementCount = entitlementCount,
            PurchaseCount = purchaseCount,
            LessonsCompleted = lessonsCompleted
        });
    }

    public async Task<ServiceResult<AdminUserWalletDto>> GetWalletAsync(
        Guid userId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(userId, cancellationToken))
            return ServiceResult<AdminUserWalletDto>.NotFound("User not found.");

        take = Math.Clamp(take, 1, 500);

        var balances = await _wallet.GetBalancesAsync(userId, cancellationToken);

        var ledgerCount = await _dbContext.CurrencyLedgerEntries
            .CountAsync(e => e.UserId == userId, cancellationToken);

        var recent = await _dbContext.CurrencyLedgerEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            // Id descending rather than CreatedAtUtc: the column is an identity, so it
            // orders writes exactly, and two entries in the same millisecond would
            // otherwise come back in an arbitrary order — which for a running-balance
            // column reads as the balance going backwards.
            .OrderByDescending(e => e.Id)
            .Take(take)
            .Select(e => new AdminLedgerEntryDto
            {
                Id = e.Id,
                CurrencyId = e.CurrencyId,
                Currency = e.Currency != null ? e.Currency.Key : string.Empty,
                Amount = e.Amount,
                BalanceAfter = e.BalanceAfter,
                TransactionType = e.TransactionType.ToString(),
                SourceType = e.SourceType.ToString(),
                SourceId = e.SourceId,
                CreatedAtUtc = e.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return ServiceResult<AdminUserWalletDto>.Success(new AdminUserWalletDto
        {
            Balances = balances,
            Recent = recent,
            LedgerCount = ledgerCount
        });
    }

    public async Task<ServiceResult<AdminUserProgressionDto>> GetProgressionAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(userId, cancellationToken))
            return ServiceResult<AdminUserProgressionDto>.NotFound("User not found.");

        var level = await _levelService.GetForUserAsync(userId, cancellationToken);
        var objectives = await _objectiveService.GetForUserAsync(userId, cancellationToken);
        var (current, best, freezes) = await _objectiveService.GetStreakAsync(userId, cancellationToken);

        return ServiceResult<AdminUserProgressionDto>.Success(new AdminUserProgressionDto
        {
            Level = level,
            Objectives = objectives,
            Streak = new StreakDto { Current = current, Best = best, FreezesRemaining = freezes }
        });
    }

    public async Task<ServiceResult<IReadOnlyList<AdminUserEntitlementDto>>> GetEntitlementsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(userId, cancellationToken))
            return ServiceResult<IReadOnlyList<AdminUserEntitlementDto>>.NotFound("User not found.");

        // Queried directly rather than through IEntitlementService: that returns
        // EntitlementDto, which carries only a productId. An operator reading this
        // needs the product's key and kind, and resolving those per row afterwards
        // would be one query per owned item.
        var rows = await _dbContext.Entitlements
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.GrantedAtUtc)
            .Select(e => new AdminUserEntitlementDto
            {
                EntitlementId = e.Id,
                ProductId = e.ProductId,
                ProductKey = e.Product != null ? e.Product.Key : string.Empty,
                KindName = e.Product != null && e.Product.Kind != null ? e.Product.Kind.Name : string.Empty,
                ProductActive = e.Product != null && e.Product.Active,
                GrantedAtUtc = e.GrantedAtUtc,
                Source = e.Source.ToString(),
                SourceId = e.SourceId
            })
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<AdminUserEntitlementDto>>.Success(rows);
    }

    public async Task<ServiceResult<IReadOnlyList<AdminUserRunDto>>> GetRunsAsync(
        Guid userId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (!await ExistsAsync(userId, cancellationToken))
            return ServiceResult<IReadOnlyList<AdminUserRunDto>>.NotFound("User not found.");

        take = Math.Clamp(take, 1, 200);

        var rows = await _dbContext.Runs
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.StartedAtUtc)
            .Take(take)
            .Select(r => new AdminUserRunDto
            {
                RunId = r.Id,
                GameId = r.GameId,
                State = r.State.ToString(),
                Outcome = r.Outcome.ToString(),
                StartedAtUtc = r.StartedAtUtc,
                EndedAtUtc = r.EndedAtUtc,
                DurationMs = r.DurationMs,
                IsFlagged = r.IsFlagged,
                FlagReason = r.FlagReason,
                Reviewed = r.ReviewedAtUtc != null,

                // Summed in SQL rather than by loading the payout rows. A history of
                // 200 runs with a dozen payout lines each is 2,400 rows to transfer
                // for 200 totals.
                NetPaid = r.Payouts.Sum(p => (long?)p.NetAmount) ?? 0
            })
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<AdminUserRunDto>>.Success(rows);
    }

    public async Task<ServiceResult> DeleteUserAsync(
        Guid userId,
        Guid? actingUserId,
        bool actorIsSuperAdmin,
        CancellationToken cancellationToken = default)
    {
        // Deleting the account you are authenticated as leaves the caller holding tokens for
        // a user that no longer exists — almost always a mistake, never what was intended.
        if (actingUserId is not null && actingUserId == userId)
            return ServiceResult.Forbidden("You cannot delete your own account.");

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return ServiceResult.NotFound("User not found.");

        var roles = await _userManager.GetRolesAsync(user);
        var targetIsPrivileged = roles.Contains(Roles.Admin) || roles.Contains(Roles.SuperAdmin);

        if (targetIsPrivileged && !actorIsSuperAdmin)
            return ServiceResult.Forbidden("Only a Super Admin can delete an Admin or Super Admin account.");

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Same sweep the user's own delete runs. Kept in UserOwnedData rather than written out
        // here so the admin path and the self-service path cannot drift — one of them gaining a
        // table the other forgets is precisely the bug this guards against.
        await UserOwnedData.PurgeAsync(_dbContext, userId, cancellationToken);

        var deleteResult = await _userManager.DeleteAsync(user);
        if (!deleteResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ServiceResult.Invalid(deleteResult.Errors.Select(e => e.Description).ToArray());
        }

        await transaction.CommitAsync(cancellationToken);
        return ServiceResult.Success();
    }
}
