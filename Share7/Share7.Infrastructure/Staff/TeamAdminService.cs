using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Staff.Interfaces;
using Share7.Application.Staff.Models;
using Share7.Domain.Audit;
using Share7.Domain.Constants;
using Share7.Domain.Staff;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Staff;

/// <inheritdoc cref="ITeamAdminService"/>
/// <remarks>
/// Messages here are English sentences for the Admin Console, like every other /api/admin
/// endpoint. They never name the member — the console already shows who is being acted on — and
/// the audit rows they write hold ids only.
/// </remarks>
public partial class TeamAdminService : ITeamAdminService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly ApplicationDbContext _db;
    private readonly StudioSessions _sessions;
    private readonly StaffScopeReader _scopes;
    private readonly PeopleDirectory _people;
    private readonly IAuditLog _audit;
    private readonly IAuditActor _actor;
    private readonly StudioOptions _studio;

    public TeamAdminService(
        UserManager<ApplicationUser> users,
        ApplicationDbContext db,
        StudioSessions sessions,
        StaffScopeReader scopes,
        PeopleDirectory people,
        IAuditLog audit,
        IAuditActor actor,
        IOptions<StudioOptions> studio)
    {
        _users = users;
        _db = db;
        _sessions = sessions;
        _scopes = scopes;
        _people = people;
        _audit = audit;
        _actor = actor;
        _studio = studio.Value;
    }

    // =========================================================================== reads

    public async Task<TeamOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var rows = await (
                from profile in _db.StaffProfiles.AsNoTracking()
                join user in _db.Users.AsNoTracking() on profile.UserId equals user.Id
                select new
                {
                    profile.UserId,
                    user.UserName,
                    profile.FullName,
                    profile.JobTitle,
                    profile.StudioRole,
                    profile.Status,
                    user.TwoFactorEnabled,
                    profile.LastActiveAtUtc,
                    profile.CreatedAtUtc,
                    profile.ActivatedAtUtc
                })
            .ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.UserId).ToList();
        var scopes = await _scopes.ReadAsync(ids, cancellationToken);

        var pendingLinks = await _db.StaffSetupTokens.AsNoTracking()
            .Where(t => ids.Contains(t.UserId) && t.UsedAtUtc == null && t.RevokedAtUtc == null && t.ExpiresAtUtc > now)
            .GroupBy(t => t.UserId)
            .Select(g => new { UserId = g.Key, ExpiresAtUtc = g.Max(t => t.ExpiresAtUtc) })
            .ToDictionaryAsync(x => x.UserId, x => x.ExpiresAtUtc, cancellationToken);

        var members = rows
            .OrderBy(r => StatusOrder(r.Status))
            .ThenBy(r => r.FullName, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => new TeamMemberListItemDto(
                r.UserId,
                r.UserName!,
                r.FullName,
                r.JobTitle,
                r.StudioRole,
                scopes[r.UserId],
                r.Status,
                r.TwoFactorEnabled,
                r.LastActiveAtUtc,
                r.CreatedAtUtc,
                r.ActivatedAtUtc,
                pendingLinks.TryGetValue(r.UserId, out var expires) ? expires : null))
            .ToList();

        var settings = await _sessions.SettingsAsync(cancellationToken);

        return new TeamOverviewDto(
            members,
            await LegacyAccountsAsync(cancellationToken),
            new TeamCountsDto(
                rows.Count(r => r.Status == StaffStatus.Active),
                rows.Count(r => r.Status == StaffStatus.Invited),
                rows.Count(r => r.Status == StaffStatus.Suspended),
                rows.Count(r => r.Status == StaffStatus.Deactivated)),
            settings.RequireTwoStep,
            StudioAddressConfigured: !string.IsNullOrWhiteSpace(_studio.PublicUrl),
            StudioAddress: string.IsNullOrWhiteSpace(_studio.PublicUrl) ? null : _studio.PublicUrl.Trim().TrimEnd('/'));
    }

    public async Task<TeamScopeOptionsDto> GetScopeOptionsAsync(CancellationToken cancellationToken = default) =>
        new(
            await _scopes.TreeAsync(cancellationToken),
            await _scopes.LanguagesAsync(cancellationToken),
            StaffPasswordPolicy.Rules((await _sessions.SettingsAsync(cancellationToken)).MinimumPasswordLength),
            string.IsNullOrWhiteSpace(_studio.PublicUrl) ? null : _studio.PublicUrl.Trim().TrimEnd('/'));

    public async Task<ServiceResult<TeamMemberDetailDto>> GetMemberAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await DetailAsync(userId, cancellationToken) is { } detail
            ? ServiceResult<TeamMemberDetailDto>.Success(detail)
            : ServiceResult<TeamMemberDetailDto>.NotFound("There is no content-team member with that id.");

    // =========================================================================== create

    public async Task<ServiceResult<CreatedTeamMemberDto>> CreateMemberAsync(
        CreateTeamMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        var errors = ValidateProfile(request.FullName, request.JobTitle, request.WorkEmail, request.InterfaceLanguage ?? StaffInterfaceLanguages.English);

        if (!UsernamePattern().IsMatch(username))
            errors.Add("Choose a username of 3 to 50 characters that starts with a letter and uses only letters, digits, dots, dashes and underscores.");
        else if (await _users.FindByNameAsync(username) is not null)
            errors.Add("That username is already taken. Usernames are fixed once created, so pick another.");

        errors.AddRange(await ValidateScopeAsync(request.AllNodes, request.NodeIds, request.AllLanguages, request.LanguageIds, cancellationToken));

        var settings = await _sessions.SettingsAsync(cancellationToken);
        var withPassword = request.Password is not null;
        if (withPassword)
            errors.AddRange(PasswordErrors(request.Password!, username, settings.MinimumPasswordLength));

        if (errors.Count > 0)
            return ServiceResult<CreatedTeamMemberDto>.Invalid([.. errors]);

        var now = DateTime.UtcNow;
        var user = new ApplicationUser { UserName = username, CreatedAt = now, LockoutEnabled = true };
        SetupLinkDto? link = null;

        await using (var transaction = await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            // With a password the admin chose, the member signs in at the Studio straight away
            // (decided 2026-09-26). Without one they choose it through a setup link, and until then
            // nothing — not the Studio, not the old sign-in — can be signed in to with this account.
            var created = withPassword ? await _users.CreateAsync(user, request.Password!) : await _users.CreateAsync(user);
            if (!created.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ServiceResult<CreatedTeamMemberDto>.Invalid([.. created.Errors.Select(e => e.Description)]);
            }

            await _users.AddToRoleAsync(user, Roles.ContentTeam);

            var profile = new StaffProfile
            {
                UserId = user.Id,
                FullName = request.FullName.Trim(),
                JobTitle = Blank(request.JobTitle),
                WorkEmail = Blank(request.WorkEmail),
                StudioRole = request.StudioRole,
                InterfaceLanguage = request.InterfaceLanguage ?? StaffInterfaceLanguages.English,
                Status = withPassword ? StaffStatus.Active : StaffStatus.Invited,
                ActivatedAtUtc = withPassword ? now : null,
                CreatedAtUtc = now,
                CreatedByUserId = _actor.UserId,
                UpdatedAtUtc = now
            };

            _db.StaffProfiles.Add(profile);
            ApplyScope(profile, request.AllNodes, request.NodeIds, request.AllLanguages, request.LanguageIds);

            if (!withPassword)
                link = IssueLink(user.Id, StaffSetupPurpose.Activation, settings, now);

            _audit.Record(new AuditEntry(
                AuditActions.TeamMemberCreated, AuditAreas.Team,
                withPassword
                    ? $"Created a content-team member as {profile.StudioRole}, with a password set by the admin."
                    : $"Created a content-team member as {profile.StudioRole} and issued their setup link.",
                "staff", user.Id.ToString(),
                new
                {
                    studioRole = profile.StudioRole.ToString(),
                    allNodes = profile.AllNodes,
                    nodeIds = request.AllNodes ? null : request.NodeIds,
                    allLanguages = profile.AllLanguages,
                    languageIds = request.AllLanguages ? null : request.LanguageIds,
                    passwordSetByAdmin = withPassword,
                    setupLinkExpiresAtUtc = link?.ExpiresAtUtc
                }));

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return ServiceResult<CreatedTeamMemberDto>.Success(
            new CreatedTeamMemberDto((await DetailAsync(user.Id, cancellationToken))!, link));
    }

    public async Task<ServiceResult<TeamMemberDetailDto>> AdoptLegacyAccountAsync(
        Guid userId,
        AdoptLegacyAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
            return ServiceResult<TeamMemberDetailDto>.NotFound("That account does not exist.");

        if (await _db.StaffProfiles.AnyAsync(p => p.UserId == userId, cancellationToken))
            return ServiceResult<TeamMemberDetailDto>.Conflict("This account already has a Studio profile.");

        var roles = await _users.GetRolesAsync(user);
        if (!roles.Contains(Roles.ContentTeam))
            return ServiceResult<TeamMemberDetailDto>.Invalid("Only content-team accounts can be set up in the Studio.");

        // Staff accounts are staff-only: an administrator's account is never also a Studio account.
        if (roles.Contains(Roles.Admin) || roles.Contains(Roles.SuperAdmin))
            return ServiceResult<TeamMemberDetailDto>.Invalid(
                "This account is also an administrator. Studio accounts are staff-only — create a separate content-team member instead.");

        var errors = ValidateProfile(request.FullName, request.JobTitle, request.WorkEmail, request.InterfaceLanguage ?? StaffInterfaceLanguages.English);
        errors.AddRange(await ValidateScopeAsync(request.AllNodes, request.NodeIds, request.AllLanguages, request.LanguageIds, cancellationToken));
        if (errors.Count > 0)
            return ServiceResult<TeamMemberDetailDto>.Invalid([.. errors]);

        var now = DateTime.UtcNow;
        var hasPassword = await _users.HasPasswordAsync(user);

        var profile = new StaffProfile
        {
            UserId = userId,
            FullName = request.FullName.Trim(),
            JobTitle = Blank(request.JobTitle),
            WorkEmail = Blank(request.WorkEmail),
            StudioRole = request.StudioRole,
            InterfaceLanguage = request.InterfaceLanguage ?? StaffInterfaceLanguages.English,

            // They already sign in with a password; setting them up changes nothing about that.
            Status = hasPassword ? StaffStatus.Active : StaffStatus.Invited,
            ActivatedAtUtc = hasPassword ? now : null,
            CreatedAtUtc = now,
            CreatedByUserId = _actor.UserId,
            UpdatedAtUtc = now
        };

        _db.StaffProfiles.Add(profile);
        ApplyScope(profile, request.AllNodes, request.NodeIds, request.AllLanguages, request.LanguageIds);

        _audit.Record(new AuditEntry(
            AuditActions.TeamMemberAdopted, AuditAreas.Team,
            $"Set up an existing content-team account in the Studio as {profile.StudioRole}.",
            "staff", userId.ToString(),
            new
            {
                studioRole = profile.StudioRole.ToString(),
                allNodes = profile.AllNodes,
                nodeIds = request.AllNodes ? null : request.NodeIds,
                allLanguages = profile.AllLanguages,
                languageIds = request.AllLanguages ? null : request.LanguageIds,
                status = profile.Status.ToString()
            }));

        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<TeamMemberDetailDto>.Success((await DetailAsync(userId, cancellationToken))!);
    }

    // =========================================================================== edit

    public async Task<ServiceResult<TeamMemberDetailDto>> UpdateProfileAsync(
        Guid userId,
        UpdateTeamMemberProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var (profile, refusal) = await LoadEditableAsync(userId, request.RowVersion, cancellationToken);
        if (refusal is not null)
            return refusal;

        var errors = ValidateProfile(request.FullName, request.JobTitle, request.WorkEmail, request.InterfaceLanguage);
        if (errors.Count > 0)
            return ServiceResult<TeamMemberDetailDto>.Invalid([.. errors]);

        var changed = new List<string>();
        if (profile!.FullName != request.FullName.Trim()) changed.Add("fullName");
        if (profile.JobTitle != Blank(request.JobTitle)) changed.Add("jobTitle");
        if (profile.WorkEmail != Blank(request.WorkEmail)) changed.Add("workEmail");
        if (profile.InterfaceLanguage != request.InterfaceLanguage) changed.Add("interfaceLanguage");

        if (changed.Count == 0)
            return ServiceResult<TeamMemberDetailDto>.Success((await DetailAsync(userId, cancellationToken))!);

        profile.FullName = request.FullName.Trim();
        profile.JobTitle = Blank(request.JobTitle);
        profile.WorkEmail = Blank(request.WorkEmail);
        profile.InterfaceLanguage = request.InterfaceLanguage;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        _audit.Record(new AuditEntry(
            AuditActions.TeamMemberProfileUpdated, AuditAreas.Team,
            "Edited a content-team member's profile.",
            "staff", userId.ToString(),
            new { changed }));

        return await SaveAndDescribeAsync(userId, cancellationToken);
    }

    public async Task<ServiceResult<TeamMemberDetailDto>> UpdateAccessAsync(
        Guid userId,
        UpdateTeamMemberAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        var (profile, refusal) = await LoadEditableAsync(userId, request.RowVersion, cancellationToken, withScope: true);
        if (refusal is not null)
            return refusal;

        var errors = await ValidateScopeAsync(request.AllNodes, request.NodeIds, request.AllLanguages, request.LanguageIds, cancellationToken);
        if (errors.Count > 0)
            return ServiceResult<TeamMemberDetailDto>.Invalid([.. errors]);

        var before = new
        {
            studioRole = profile!.StudioRole.ToString(),
            allNodes = profile.AllNodes,
            nodeIds = profile.ScopeNodes.Select(n => n.NodeId).Order().ToList(),
            allLanguages = profile.AllLanguages,
            languageIds = profile.ScopeLanguages.Select(l => l.LanguageId).Order().ToList()
        };

        profile.StudioRole = request.StudioRole;
        ApplyScope(profile, request.AllNodes, request.NodeIds, request.AllLanguages, request.LanguageIds);
        profile.UpdatedAtUtc = DateTime.UtcNow;

        var after = new
        {
            studioRole = profile.StudioRole.ToString(),
            allNodes = profile.AllNodes,
            nodeIds = profile.ScopeNodes.Select(n => n.NodeId).Order().ToList(),
            allLanguages = profile.AllLanguages,
            languageIds = profile.ScopeLanguages.Select(l => l.LanguageId).Order().ToList()
        };

        _audit.Record(new AuditEntry(
            AuditActions.TeamMemberAccessChanged, AuditAreas.Team,
            before.studioRole == after.studioRole
                ? $"Changed a content-team member's scope ({after.studioRole})."
                : $"Changed a content-team member's Studio role from {before.studioRole} to {after.studioRole}.",
            "staff", userId.ToString(),
            new { before, after }));

        var result = await SaveAndDescribeAsync(userId, cancellationToken);

        // Role and scope are read per request; drop the cached answer so it applies now, not in a minute.
        _sessions.ForgetCached(userId);
        return result;
    }

    public async Task<ServiceResult<TeamMemberDetailDto>> UpdateNotesAsync(
        Guid userId,
        UpdateTeamMemberNotesRequest request,
        CancellationToken cancellationToken = default)
    {
        var profile = await _db.StaffProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
            return ServiceResult<TeamMemberDetailDto>.NotFound("There is no content-team member with that id.");

        var notes = Blank(request.Notes);
        if (notes?.Length > 4000)
            return ServiceResult<TeamMemberDetailDto>.Invalid("Notes can be at most 4,000 characters.");

        profile.Notes = notes;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        // The notes are private to SuperAdmins; the trail records that they changed, not what they say.
        _audit.Record(new AuditEntry(
            AuditActions.TeamMemberNotesUpdated, AuditAreas.Team,
            "Edited the private notes on a content-team member.",
            "staff", userId.ToString(),
            new { length = notes?.Length ?? 0 }));

        return await SaveAndDescribeAsync(userId, cancellationToken);
    }

    // =========================================================================== lifecycle

    public async Task<ServiceResult<TeamMemberDetailDto>> SuspendAsync(
        Guid userId,
        SuspendTeamMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        var (user, profile) = await LoadAsync(userId, cancellationToken);
        if (user is null || profile is null)
            return ServiceResult<TeamMemberDetailDto>.NotFound("There is no content-team member with that id.");

        if (profile.Status is not (StaffStatus.Active or StaffStatus.Invited))
            return ServiceResult<TeamMemberDetailDto>.Conflict(profile.Status == StaffStatus.Suspended
                ? "This member is already suspended."
                : "A deactivated member cannot be suspended.");

        var reason = Blank(request.Reason);
        if (reason?.Length > 500)
            return ServiceResult<TeamMemberDetailDto>.Invalid("Keep the reason under 500 characters.");

        var ended = await ShutOutAsync(user, profile, StaffStatus.Suspended, reason, StaffSessionEndReasons.Suspended, cancellationToken,
            new AuditEntry(
                AuditActions.TeamMemberSuspended, AuditAreas.Team,
                "Suspended a content-team member and signed them out everywhere.",
                "staff", userId.ToString()));

        return ended;
    }

    public async Task<ServiceResult<TeamMemberDetailDto>> ReactivateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var (user, profile) = await LoadAsync(userId, cancellationToken);
        if (user is null || profile is null)
            return ServiceResult<TeamMemberDetailDto>.NotFound("There is no content-team member with that id.");

        if (profile.Status != StaffStatus.Suspended)
            return ServiceResult<TeamMemberDetailDto>.Conflict(profile.Status == StaffStatus.Deactivated
                ? "A deactivated member can never be reactivated. Create a new member if they are coming back."
                : "Only a suspended member can be reactivated.");

        var now = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Someone suspended before they ever used their setup link goes back to waiting for one.
        profile.Status = profile.ActivatedAtUtc is null ? StaffStatus.Invited : StaffStatus.Active;
        profile.StatusReason = null;
        profile.StatusChangedAtUtc = now;
        profile.StatusChangedByUserId = _actor.UserId;
        profile.UpdatedAtUtc = now;

        await _users.SetLockoutEndDateAsync(user, null);
        await _users.ResetAccessFailedCountAsync(user);

        _audit.Record(new AuditEntry(
            AuditActions.TeamMemberReactivated, AuditAreas.Team,
            profile.Status == StaffStatus.Active
                ? "Reactivated a suspended content-team member."
                : "Lifted a suspension on a content-team member who has not activated their account yet.",
            "staff", userId.ToString(),
            new { status = profile.Status.ToString() }));

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _sessions.ForgetCached(userId);
        return ServiceResult<TeamMemberDetailDto>.Success((await DetailAsync(userId, cancellationToken))!);
    }

    public async Task<ServiceResult<TeamMemberDetailDto>> DeactivateAsync(
        Guid userId,
        DeactivateTeamMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        var (user, profile) = await LoadAsync(userId, cancellationToken);
        if (user is null || profile is null)
            return ServiceResult<TeamMemberDetailDto>.NotFound("There is no content-team member with that id.");

        if (profile.Status == StaffStatus.Deactivated)
            return ServiceResult<TeamMemberDetailDto>.Conflict("This member is already deactivated.");

        if (!string.Equals(request.ConfirmUsername?.Trim(), user.UserName, StringComparison.OrdinalIgnoreCase))
            return ServiceResult<TeamMemberDetailDto>.Invalid("Type the member's username exactly to confirm. Deactivation cannot be undone.");

        var reason = Blank(request.Reason);
        if (reason is null)
            return ServiceResult<TeamMemberDetailDto>.Invalid("Say why the account is being closed. It is kept on the record.");
        if (reason.Length > 500)
            return ServiceResult<TeamMemberDetailDto>.Invalid("Keep the reason under 500 characters.");

        return await ShutOutAsync(user, profile, StaffStatus.Deactivated, reason, StaffSessionEndReasons.Deactivated, cancellationToken,
            new AuditEntry(
                AuditActions.TeamMemberDeactivated, AuditAreas.Team,
                "Deactivated a content-team member. The account can never sign in again; their work keeps their name.",
                "staff", userId.ToString()));
    }

    /// <summary>
    /// Suspension and deactivation share everything but finality: the status, an Identity lockout,
    /// a new security stamp, every Studio session ended, the old sign-in's refresh tokens revoked,
    /// and any pending setup link withdrawn — in one transaction with the audit row.
    /// <para>
    /// The lockout predates cutover, which now refuses a content-team account the old sign-in
    /// outright. It is kept because it is checked <i>before</i> the password and before that
    /// refusal, so it holds whatever else changes — and because it is what makes a suspension
    /// bite on a token minted while the old door was still open.
    /// </para>
    /// </summary>
    private async Task<ServiceResult<TeamMemberDetailDto>> ShutOutAsync(
        ApplicationUser user,
        StaffProfile profile,
        StaffStatus status,
        string? reason,
        string sessionEndReason,
        CancellationToken cancellationToken,
        AuditEntry entry)
    {
        var now = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var previous = profile.Status;
        profile.Status = status;
        profile.StatusReason = reason;
        profile.StatusChangedAtUtc = now;
        profile.StatusChangedByUserId = _actor.UserId;
        profile.UpdatedAtUtc = now;

        await _users.SetLockoutEnabledAsync(user, true);
        await _users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

        if (status == StaffStatus.Deactivated && await _users.HasPasswordAsync(user))
            await _users.RemovePasswordAsync(user);

        await _users.UpdateSecurityStampAsync(user);

        var sessions = await _sessions.EndAllAsync(user.Id, sessionEndReason, _actor.UserId, cancellationToken);
        var legacy = await _sessions.EndLegacySignInsAsync(user.Id, status == StaffStatus.Suspended ? "Suspended in Team & Access" : "Deactivated in Team & Access", cancellationToken);
        var links = await RevokeLinksAsync(user.Id, now, cancellationToken);

        _audit.Record(entry with
        {
            Data = new
            {
                previousStatus = previous.ToString(),
                reason,
                studioSessionsEnded = sessions,
                oldSignInsRevoked = legacy,
                setupLinksWithdrawn = links
            }
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _sessions.ForgetCached(user.Id);
        return ServiceResult<TeamMemberDetailDto>.Success((await DetailAsync(user.Id, cancellationToken))!);
    }

    public async Task<ServiceResult<TeamMemberDetailDto>> SetPasswordAsync(
        Guid userId,
        SetTeamMemberPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var (user, profile) = await LoadAsync(userId, cancellationToken);
        if (user is null || profile is null)
            return ServiceResult<TeamMemberDetailDto>.NotFound("There is no content-team member with that id.");

        if (profile.Status == StaffStatus.Deactivated)
            return ServiceResult<TeamMemberDetailDto>.Conflict("A closed account cannot be given a new password.");

        var settings = await _sessions.SettingsAsync(cancellationToken);
        var errors = PasswordErrors(request.Password ?? string.Empty, user.UserName!, settings.MinimumPasswordLength);
        if (errors.Count > 0)
            return ServiceResult<TeamMemberDetailDto>.Invalid([.. errors]);

        var now = DateTime.UtcNow;
        var activated = profile.Status == StaffStatus.Invited;

        await using (var transaction = await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            // Any link still waiting is withdrawn: the password is now the way in.
            var withdrawn = await RevokeLinksAsync(userId, now, cancellationToken);

            if (await _users.HasPasswordAsync(user))
                await _users.RemovePasswordAsync(user);

            var added = await _users.AddPasswordAsync(user, request.Password!);
            if (!added.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ServiceResult<TeamMemberDetailDto>.Invalid([.. added.Errors.Select(e => e.Description)]);
            }

            if (request.ClearTwoStep && user.TwoFactorEnabled)
            {
                await _users.SetTwoFactorEnabledAsync(user, false);
                await _users.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey");
                await _users.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "RecoveryCodes");
            }

            await _users.SetLockoutEnabledAsync(user, true);
            await _users.SetLockoutEndDateAsync(user, null);
            await _users.ResetAccessFailedCountAsync(user);
            await _users.UpdateSecurityStampAsync(user);

            // Whoever held the old password is out, on every device.
            var sessions = await _sessions.EndAllAsync(userId, StaffSessionEndReasons.PasswordChanged, _actor.UserId, cancellationToken);
            var legacy = await _sessions.EndLegacySignInsAsync(userId, "Password set in Team & Access", cancellationToken);

            if (activated)
            {
                profile.Status = StaffStatus.Active;
                profile.ActivatedAtUtc = now;
                profile.StatusChangedAtUtc = now;
                profile.StatusChangedByUserId = _actor.UserId;
            }

            profile.UpdatedAtUtc = now;

            _audit.Record(new AuditEntry(
                AuditActions.TeamMemberPasswordSet, AuditAreas.Team,
                request.ClearTwoStep
                    ? "Set a member's password, turned off their 2-step sign-in and signed them out everywhere."
                    : "Set a member's password and signed them out everywhere.",
                "staff", userId.ToString(),
                new
                {
                    clearTwoStep = request.ClearTwoStep,
                    activated,
                    studioSessionsEnded = sessions,
                    oldSignInsRevoked = legacy,
                    setupLinksWithdrawn = withdrawn
                }));

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        _sessions.ForgetCached(userId);
        return ServiceResult<TeamMemberDetailDto>.Success((await DetailAsync(userId, cancellationToken))!);
    }

    /// <summary>The staff password rules a password breaks, as sentences for the Admin Console.</summary>
    private static List<string> PasswordErrors(string password, string username, int minimumLength) =>
        StaffPasswordPolicy.Problems(password, username, minimumLength).Select(code => code switch
        {
            "tooShort" => $"The password needs at least {minimumLength} characters.",
            "needsUppercase" => "The password needs an upper-case letter.",
            "needsLowercase" => "The password needs a lower-case letter.",
            "needsDigit" => "The password needs a digit.",
            "containsUsername" => "The password must not contain the username.",
            "common" => "That password is too easy to guess. Use Generate for a strong one.",
            _ => "The password does not meet the rules."
        }).ToList();

    public async Task<ServiceResult<SetupLinkDto>> ResetAccessAsync(
        Guid userId,
        ResetTeamMemberAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        var (user, profile) = await LoadAsync(userId, cancellationToken);
        if (user is null || profile is null)
            return ServiceResult<SetupLinkDto>.NotFound("There is no content-team member with that id.");

        if (profile.Status == StaffStatus.Suspended)
            return ServiceResult<SetupLinkDto>.Conflict("Reactivate the member before resetting their access.");
        if (profile.Status == StaffStatus.Deactivated)
            return ServiceResult<SetupLinkDto>.Conflict("A deactivated member's access cannot be reset.");

        var now = DateTime.UtcNow;
        var settings = await _sessions.SettingsAsync(cancellationToken);
        SetupLinkDto link;

        await using (var transaction = await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            var withdrawn = await RevokeLinksAsync(userId, now, cancellationToken);

            if (profile.Status == StaffStatus.Invited)
            {
                // Never activated: nothing to clear, just a fresh link in place of the old one.
                link = IssueLink(userId, StaffSetupPurpose.Activation, settings, now);

                _audit.Record(new AuditEntry(
                    AuditActions.TeamSetupLinkIssued, AuditAreas.Team,
                    "Issued a new setup link to a member who has not activated their account.",
                    "staff", userId.ToString(),
                    new { expiresAtUtc = link.ExpiresAtUtc, setupLinksWithdrawn = withdrawn }));
            }
            else
            {
                if (await _users.HasPasswordAsync(user))
                    await _users.RemovePasswordAsync(user);

                if (request.ClearTwoStep && user.TwoFactorEnabled)
                {
                    await _users.SetTwoFactorEnabledAsync(user, false);
                    await _users.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey");
                    await _users.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "RecoveryCodes");
                }

                await _users.SetLockoutEndDateAsync(user, null);
                await _users.ResetAccessFailedCountAsync(user);
                await _users.UpdateSecurityStampAsync(user);

                var sessions = await _sessions.EndAllAsync(userId, StaffSessionEndReasons.AccessReset, _actor.UserId, cancellationToken);
                var legacy = await _sessions.EndLegacySignInsAsync(userId, "Access reset in Team & Access", cancellationToken);

                link = IssueLink(userId, StaffSetupPurpose.Reset, settings, now);

                _audit.Record(new AuditEntry(
                    AuditActions.TeamMemberAccessReset, AuditAreas.Team,
                    request.ClearTwoStep
                        ? "Reset a member's access: cleared their password and 2-step sign-in, signed them out and issued a setup link."
                        : "Reset a member's access: cleared their password, signed them out and issued a setup link.",
                    "staff", userId.ToString(),
                    new
                    {
                        clearTwoStep = request.ClearTwoStep,
                        studioSessionsEnded = sessions,
                        oldSignInsRevoked = legacy,
                        setupLinksWithdrawn = withdrawn,
                        expiresAtUtc = link.ExpiresAtUtc
                    }));
            }

            profile.UpdatedAtUtc = now;
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        _sessions.ForgetCached(userId);
        return ServiceResult<SetupLinkDto>.Success(link);
    }

    public async Task<ServiceResult> RevokeSetupLinkAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await _db.StaffProfiles.AnyAsync(p => p.UserId == userId, cancellationToken))
            return ServiceResult.NotFound("There is no content-team member with that id.");

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var withdrawn = await RevokeLinksAsync(userId, DateTime.UtcNow, cancellationToken);
        if (withdrawn == 0)
            return ServiceResult.NotFound("This member has no setup link waiting to be used.");

        _audit.Record(new AuditEntry(
            AuditActions.TeamSetupLinkRevoked, AuditAreas.Team,
            "Withdrew a member's unused setup link.",
            "staff", userId.ToString()));

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<TeamMemberDetailDto>> SignOutEverywhereAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var (user, profile) = await LoadAsync(userId, cancellationToken);
        if (user is null || profile is null)
            return ServiceResult<TeamMemberDetailDto>.NotFound("There is no content-team member with that id.");

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        await _users.UpdateSecurityStampAsync(user);
        var sessions = await _sessions.EndAllAsync(userId, StaffSessionEndReasons.SignedOutEverywhere, _actor.UserId, cancellationToken);
        var legacy = await _sessions.EndLegacySignInsAsync(userId, "Signed out everywhere in Team & Access", cancellationToken);

        _audit.Record(new AuditEntry(
            AuditActions.TeamMemberSignedOutEverywhere, AuditAreas.Team,
            "Signed a content-team member out on every device.",
            "staff", userId.ToString(),
            new { studioSessionsEnded = sessions, oldSignInsRevoked = legacy }));

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _sessions.ForgetCached(userId);
        return ServiceResult<TeamMemberDetailDto>.Success((await DetailAsync(userId, cancellationToken))!);
    }

    public async Task<ServiceResult> RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        if (!await _sessions.EndOneAsync(userId, sessionId, StaffSessionEndReasons.RevokedByAdmin, _actor.UserId, cancellationToken))
            return ServiceResult.NotFound("That device is not signed in.");

        _audit.Record(new AuditEntry(
            AuditActions.TeamSessionRevoked, AuditAreas.Team,
            "Signed one of a content-team member's devices out of the Studio.",
            "staff-session", sessionId.ToString(),
            new { userId }));

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ServiceResult.Success();
    }

    // =========================================================================== security settings

    public async Task<StaffSecuritySettingsDto> GetSecurityAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _sessions.SettingsAsync(cancellationToken);
        var names = await _people.NamesAsync([settings.UpdatedByUserId], cancellationToken);

        var withoutTwoStep = await (
                from profile in _db.StaffProfiles.AsNoTracking()
                join user in _db.Users.AsNoTracking() on profile.UserId equals user.Id
                where profile.Status == StaffStatus.Active && !user.TwoFactorEnabled
                select profile.UserId)
            .CountAsync(cancellationToken);

        return new StaffSecuritySettingsDto(
            settings.RequireTwoStep,
            settings.SessionLifetimeHours,
            settings.IdleTimeoutHours,
            settings.MinimumPasswordLength,
            settings.SetupLinkLifetimeHours,
            settings.UpdatedAtUtc,
            PeopleDirectory.Ref(names, settings.UpdatedByUserId),
            withoutTwoStep);
    }

    public async Task<ServiceResult<StaffSecuritySettingsDto>> UpdateSecurityAsync(
        UpdateStaffSecuritySettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        if (request.SessionLifetimeHours is < 1 or > 24 * 30)
            errors.Add("A sign-in can last from 1 hour to 30 days.");
        if (request.IdleTimeoutHours < 1 || request.IdleTimeoutHours > request.SessionLifetimeHours)
            errors.Add("The inactivity limit must be at least 1 hour and no longer than the sign-in itself.");
        if (request.MinimumPasswordLength < StaffSecuritySettings.MinimumPasswordLengthFloor || request.MinimumPasswordLength > 64)
            errors.Add($"Passwords must be at least {StaffSecuritySettings.MinimumPasswordLengthFloor} characters; the longest minimum you can set is 64.");
        if (request.SetupLinkLifetimeHours is < 1 or > 24 * 14)
            errors.Add("A setup link can stay valid from 1 hour to 14 days.");

        if (errors.Count > 0)
            return ServiceResult<StaffSecuritySettingsDto>.Invalid([.. errors]);

        var settings = await _db.StaffSecuritySettings.FirstAsync(cancellationToken);

        var before = new
        {
            settings.RequireTwoStep,
            settings.SessionLifetimeHours,
            settings.IdleTimeoutHours,
            settings.MinimumPasswordLength,
            settings.SetupLinkLifetimeHours
        };

        settings.RequireTwoStep = request.RequireTwoStep;
        settings.SessionLifetimeHours = request.SessionLifetimeHours;
        settings.IdleTimeoutHours = request.IdleTimeoutHours;
        settings.MinimumPasswordLength = request.MinimumPasswordLength;
        settings.SetupLinkLifetimeHours = request.SetupLinkLifetimeHours;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        settings.UpdatedByUserId = _actor.UserId;

        _audit.Record(new AuditEntry(
            AuditActions.TeamSecurityUpdated, AuditAreas.Team,
            before.RequireTwoStep != request.RequireTwoStep
                ? request.RequireTwoStep
                    ? "Made 2-step sign-in required for every content-team member."
                    : "Made 2-step sign-in optional for content-team members."
                : "Changed the staff sign-in settings.",
            "staff-security", StaffSecuritySettings.SingletonId.ToString(),
            new { before, after = request }));

        await _db.SaveChangesAsync(cancellationToken);

        return ServiceResult<StaffSecuritySettingsDto>.Success(await GetSecurityAsync(cancellationToken));
    }

    // =========================================================================== helpers

    private async Task<TeamMemberDetailDto?> DetailAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await _db.StaffProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        var user = profile is null ? null : await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (profile is null || user is null)
            return null;

        var now = DateTime.UtcNow;

        var pending = await _db.StaffSetupTokens.AsNoTracking()
            .Where(t => t.UserId == userId && t.UsedAtUtc == null && t.RevokedAtUtc == null && t.ExpiresAtUtc > now)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new PendingSetupLinkDto(t.Purpose, t.CreatedAtUtc, t.ExpiresAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        var sessions = await _db.StaffSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null && s.ExpiresAtUtc > now)
            .OrderByDescending(s => s.LastSeenAtUtc)
            .ToListAsync(cancellationToken);

        var signIns = await _db.StaffSignInEvents.AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        var names = await _people.NamesAsync([profile.CreatedByUserId, profile.StatusChangedByUserId], cancellationToken);
        var recoveryLeft = user.TwoFactorEnabled ? await _users.CountRecoveryCodesAsync(user) : 0;

        return new TeamMemberDetailDto(
            user.Id,
            user.UserName!,
            profile.FullName,
            profile.JobTitle,
            profile.WorkEmail,
            profile.InterfaceLanguage,
            profile.StudioRole,
            await _scopes.ReadAsync(userId, cancellationToken),
            profile.Status,
            profile.StatusReason,
            profile.StatusChangedAtUtc,
            PeopleDirectory.Ref(names, profile.StatusChangedByUserId),
            profile.CreatedAtUtc,
            PeopleDirectory.Ref(names, profile.CreatedByUserId),
            profile.ActivatedAtUtc,
            profile.LastActiveAtUtc,
            profile.Notes,
            new TeamMemberSecurityDto(
                user.TwoFactorEnabled,
                recoveryLeft,
                !string.IsNullOrEmpty(user.PasswordHash),

                // A suspension's lockout runs to the end of time; only a failed-attempts lockout is
                // worth showing as a lockout.
                user.LockoutEnd is { } until && until > DateTimeOffset.UtcNow && until < DateTimeOffset.MaxValue.AddYears(-1)
                    ? until.UtcDateTime
                    : null,
                user.AccessFailedCount),
            pending,
            sessions.Select(s => new StaffSessionDto(
                s.Id, s.CreatedAtUtc, s.LastSeenAtUtc, s.ExpiresAtUtc, s.IpAddress, DeviceNames.Describe(s.UserAgent), s.TwoStepVerified)).ToList(),
            signIns.Select(e => new StaffSignInDto(e.OccurredAtUtc, e.Outcome, e.IpAddress, DeviceNames.Describe(e.UserAgent))).ToList(),
            Convert.ToBase64String(profile.RowVersion));
    }

    private async Task<IReadOnlyList<LegacyContentAccountDto>> LegacyAccountsAsync(CancellationToken cancellationToken) =>
        await (
                from user in _db.Users.AsNoTracking()
                join userRole in _db.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
                join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                where role.Name == Roles.ContentTeam && !_db.StaffProfiles.Any(p => p.UserId == user.Id)
                orderby user.UserName
                select new LegacyContentAccountDto(user.Id, user.UserName!, user.CreatedAt))
            .ToListAsync(cancellationToken);

    private async Task<(ApplicationUser? User, StaffProfile? Profile)> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await _db.StaffProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        var user = profile is null ? null : await _users.FindByIdAsync(userId.ToString());
        return (user, profile);
    }

    /// <summary>
    /// The profile for an edit, or the refusal: missing, deactivated (the record is closed), or
    /// changed by somebody else since this SuperAdmin loaded it.
    /// </summary>
    private async Task<(StaffProfile? Profile, ServiceResult<TeamMemberDetailDto>? Refusal)> LoadEditableAsync(
        Guid userId,
        string? rowVersion,
        CancellationToken cancellationToken,
        bool withScope = false)
    {
        var query = _db.StaffProfiles.AsQueryable();
        if (withScope)
            query = query.Include(p => p.ScopeNodes).Include(p => p.ScopeLanguages);

        var profile = await query.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (profile is null)
            return (null, ServiceResult<TeamMemberDetailDto>.NotFound("There is no content-team member with that id."));

        if (profile.Status == StaffStatus.Deactivated)
            return (null, ServiceResult<TeamMemberDetailDto>.Conflict("A deactivated member's record is closed and cannot be changed."));

        if (string.IsNullOrEmpty(rowVersion) || rowVersion != Convert.ToBase64String(profile.RowVersion))
            return (null, ServiceResult<TeamMemberDetailDto>.Conflict(
                "Somebody changed this member while you were editing. Reload to see their changes, then make yours again."));

        return (profile, null);
    }

    private async Task<ServiceResult<TeamMemberDetailDto>> SaveAndDescribeAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<TeamMemberDetailDto>.Conflict(
                "Somebody changed this member while you were editing. Reload to see their changes, then make yours again.");
        }

        return ServiceResult<TeamMemberDetailDto>.Success((await DetailAsync(userId, cancellationToken))!);
    }

    private static List<string> ValidateProfile(string? fullName, string? jobTitle, string? workEmail, string interfaceLanguage)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length < 2)
            errors.Add("Enter the member's full name. It appears on everything they author, review and publish.");
        else if (fullName.Trim().Length > 200)
            errors.Add("Keep the full name under 200 characters.");

        if (jobTitle?.Trim().Length > 100)
            errors.Add("Keep the job title under 100 characters.");

        if (Blank(workEmail) is { } email && (email.Length > 256 || !MailAddress.TryCreate(email, out _)))
            errors.Add("The work email doesn't look like an email address. Leave it empty if there isn't one.");

        if (!StaffInterfaceLanguages.All.Contains(interfaceLanguage))
            errors.Add("The Studio's interface language must be English or Arabic.");

        return errors;
    }

    private async Task<List<string>> ValidateScopeAsync(
        bool allNodes,
        IReadOnlyList<Guid>? nodeIds,
        bool allLanguages,
        IReadOnlyList<Guid>? languageIds,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        if (!allNodes)
        {
            var ids = nodeIds?.Distinct().ToList() ?? [];

            if (ids.Count == 0)
                errors.Add("Choose at least one part of the curriculum, or give access to all of it.");
            else if (ids.Count > 200)
                errors.Add("A scope can name at most 200 parts of the curriculum. Choose a higher level instead.");
            else if ((await _scopes.ValidScopeNodeIdsAsync(ids, cancellationToken)).Count != ids.Count)
                errors.Add("Some of the chosen curriculum parts no longer exist. Reload and choose again.");
        }

        if (!allLanguages)
        {
            var ids = languageIds?.Distinct().ToList() ?? [];

            if (ids.Count == 0)
                errors.Add("Choose at least one language, or give access to all of them.");
            else if (await _db.Languages.CountAsync(l => ids.Contains(l.Id), cancellationToken) != ids.Count)
                errors.Add("Some of the chosen languages don't exist.");
        }

        return errors;
    }

    /// <summary>
    /// Brings the profile's scope rows to exactly the requested set — removing what went, adding
    /// what is new, leaving the rest untouched, so an unchanged row is never deleted and re-added
    /// under the same key.
    /// </summary>
    private static void ApplyScope(StaffProfile profile, bool allNodes, IReadOnlyList<Guid>? nodeIds, bool allLanguages, IReadOnlyList<Guid>? languageIds)
    {
        profile.AllNodes = allNodes;
        profile.AllLanguages = allLanguages;

        var wantedNodes = allNodes ? new HashSet<Guid>() : nodeIds!.ToHashSet();
        foreach (var gone in profile.ScopeNodes.Where(n => !wantedNodes.Contains(n.NodeId)).ToList())
            profile.ScopeNodes.Remove(gone);
        foreach (var nodeId in wantedNodes.Where(id => profile.ScopeNodes.All(n => n.NodeId != id)))
            profile.ScopeNodes.Add(new StaffScopeNode { UserId = profile.UserId, NodeId = nodeId });

        var wantedLanguages = allLanguages ? new HashSet<Guid>() : languageIds!.ToHashSet();
        foreach (var gone in profile.ScopeLanguages.Where(l => !wantedLanguages.Contains(l.LanguageId)).ToList())
            profile.ScopeLanguages.Remove(gone);
        foreach (var languageId in wantedLanguages.Where(id => profile.ScopeLanguages.All(l => l.LanguageId != id)))
            profile.ScopeLanguages.Add(new StaffScopeLanguage { UserId = profile.UserId, LanguageId = languageId });
    }

    /// <summary>Stages a new setup link. The secret leaves this method in the returned URL and is never stored.</summary>
    private SetupLinkDto IssueLink(Guid userId, StaffSetupPurpose purpose, StaffSecuritySettings settings, DateTime now)
    {
        var secret = StaffSecrets.NewToken();
        var expires = now.AddHours(settings.SetupLinkLifetimeHours);

        _db.StaffSetupTokens.Add(new StaffSetupToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = StaffSecrets.Hash(secret),
            Purpose = purpose,
            CreatedAtUtc = now,
            CreatedByUserId = _actor.UserId,
            ExpiresAtUtc = expires
        });

        // In the fragment, not the query: a fragment is never sent to a server, so the secret stays
        // out of every access log and proxy between the member and the Studio.
        var path = $"/activate#{secret}";
        var baseUrl = _studio.PublicUrl?.Trim().TrimEnd('/');

        return string.IsNullOrEmpty(baseUrl)
            // The Studio is at /studio on the API's own site when no address is configured (StudioHosting).
            ? new SetupLinkDto("/studio" + path, IsAbsolute: false, expires, purpose)
            : new SetupLinkDto(baseUrl + path, IsAbsolute: true, expires, purpose);
    }

    private Task<int> RevokeLinksAsync(Guid userId, DateTime now, CancellationToken cancellationToken) =>
        _db.StaffSetupTokens
            .Where(t => t.UserId == userId && t.UsedAtUtc == null && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAtUtc, now), cancellationToken);

    private static int StatusOrder(StaffStatus status) => status switch
    {
        StaffStatus.Active => 0,
        StaffStatus.Invited => 1,
        StaffStatus.Suspended => 2,
        _ => 3
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9._-]{2,49}$")]
    private static partial Regex UsernamePattern();
}
