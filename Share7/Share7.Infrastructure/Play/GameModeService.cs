using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Play.Interfaces;
using Share7.Application.Play.Models;
using Share7.Domain.Play;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Play;

/// <summary>
/// Serves one game's modes to the client.
/// <para>
/// <b>Withdrawn and unopened modes are omitted, never flagged.</b> A client that has to decide
/// whether to draw a row is a client that will eventually decide wrongly — and the decision is the
/// server's anyway, because only it knows the time. The one exception is an explicit
/// <c>includeScheduled</c> read, where a picker is deliberately advertising what opens later.
/// </para>
/// </summary>
public class GameModeService : IGameModeService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILanguageService _languageService;

    public GameModeService(ApplicationDbContext dbContext, ILanguageService languageService)
    {
        _dbContext = dbContext;
        _languageService = languageService;
    }

    public async Task<GameModesResponse> GetForGameAsync(
        string gameKey, bool includeScheduled = false, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var key = (gameKey ?? string.Empty).Trim();

        if (key.Length == 0)
            return new GameModesResponse { GameKey = key, ServerTimeUtc = now, Modes = [] };

        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);
        var defaultProfileKey = await DefaultProfileKeyAsync(_dbContext, cancellationToken);

        var query = _dbContext.GameModes
            .AsNoTracking()
            .Where(m => m.Game!.GameKey == key && m.IsActive);

        // The window is compared against the server clock here rather than on the device, which is
        // the point of the whole read.
        query = includeScheduled
            // Even a "show me what is coming" read hides what has already finished: a mode whose
            // window closed is not upcoming, it is over.
            ? query.Where(m => m.AvailableToUtc == null || now < m.AvailableToUtc)
            : query.Where(m =>
                (m.AvailableFromUtc == null || m.AvailableFromUtc <= now)
                && (m.AvailableToUtc == null || now < m.AvailableToUtc));

        var rows = await query
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.ModeKey)
            .Select(Projection(langId, defaultProfileKey))
            .ToListAsync(cancellationToken);

        return new GameModesResponse
        {
            GameKey = key,
            ServerTimeUtc = now,
            Modes = rows.Select(row => row.ToDto(now)).ToList()
        };
    }

    /// <summary>
    /// The key every mode that names no profile settles under. Read once per request rather than
    /// joined per row, and falling back to the seeded key's name when no default row exists — a
    /// platform whose profiles were never seeded still has to be able to answer this read.
    /// </summary>
    internal static async Task<string> DefaultProfileKeyAsync(
        ApplicationDbContext dbContext, CancellationToken cancellationToken) =>
        await dbContext.EconomyProfiles
            .AsNoTracking()
            .Where(p => p.IsDefault)
            .Select(p => p.ProfileKey)
            .FirstOrDefaultAsync(cancellationToken) ?? EconomyProfileKeys.Default;

    /// <summary>
    /// The row shape the database can actually produce.
    /// <para>
    /// Topologies stay an <c>int</c> here and become tokens in <see cref="ToDto"/>, because the
    /// conversion is a method call SQL Server cannot run — projecting it directly compiles, then
    /// throws at the first request with "could not be translated".
    /// </para>
    /// </summary>
    internal sealed record ModeRow
    {
        public required Guid ModeId { get; init; }
        public required string ModeKey { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required PlayTopologies Topologies { get; init; }
        public required int MinPlayers { get; init; }
        public required int MaxPlayers { get; init; }
        public required DateTime? AvailableFromUtc { get; init; }
        public required DateTime? AvailableToUtc { get; init; }
        public required bool RequiresEntitlement { get; init; }
        public required string? EntitlementSku { get; init; }
        public required int MinGradeOrder { get; init; }
        public required bool CountsTowardMastery { get; init; }
        public required bool SettlesEconomy { get; init; }
        public required bool CountsTowardRanking { get; init; }
        public required string EconomyProfileKey { get; init; }
        public required int SortOrder { get; init; }
        public required bool IsDefault { get; init; }

        public GameModeDto ToDto(DateTime now) => new()
        {
            ModeId = ModeId,
            ModeKey = ModeKey,
            Name = Name,
            Description = Description,
            Topologies = PlayTopologyTokens.ToTokens(Topologies),
            MinPlayers = MinPlayers,
            MaxPlayers = MaxPlayers,
            IsOffered = (AvailableFromUtc is not { } from || from <= now)
                        && (AvailableToUtc is not { } to || now < to),
            AvailableFromUtc = AvailableFromUtc,
            AvailableToUtc = AvailableToUtc,
            RequiresEntitlement = RequiresEntitlement,
            EntitlementSku = EntitlementSku,
            MinGradeOrder = MinGradeOrder,
            CountsTowardMastery = CountsTowardMastery,
            SettlesEconomy = SettlesEconomy,
            CountsTowardRanking = CountsTowardRanking,
            EconomyProfileKey = EconomyProfileKey,
            SortOrder = SortOrder,
            IsDefault = IsDefault
        };
    }

    /// <summary>
    /// An <see cref="Expression"/> rather than a method for the reason <c>GameService.Projection</c>
    /// documents: a method call inside <c>Select</c> is not translatable, so EF would materialize
    /// every row and evaluate it client-side — where the translations were never included and every
    /// name silently comes back blank.
    /// </summary>
    private static Expression<Func<GameMode, ModeRow>> Projection(Guid langId, string defaultProfileKey) =>
        m => new ModeRow
        {
            ModeId = m.Id,
            ModeKey = m.ModeKey,
            Name = m.Translations.Where(t => t.LangId == langId).Select(t => t.Name).FirstOrDefault() ?? string.Empty,
            Description = m.Translations.Where(t => t.LangId == langId).Select(t => t.Description).FirstOrDefault() ?? string.Empty,
            Topologies = m.Topologies,
            MinPlayers = m.MinPlayers,
            MaxPlayers = m.MaxPlayers,
            AvailableFromUtc = m.AvailableFromUtc,
            AvailableToUtc = m.AvailableToUtc,
            RequiresEntitlement = m.RequiresEntitlement,
            EntitlementSku = m.EntitlementProduct != null ? m.EntitlementProduct.Key : null,
            MinGradeOrder = m.MinGradeOrder,
            CountsTowardMastery = m.CountsTowardMastery,
            SettlesEconomy = m.SettlesEconomy,
            CountsTowardRanking = m.CountsTowardRanking,
            EconomyProfileKey = m.EconomyProfile != null ? m.EconomyProfile.ProfileKey : defaultProfileKey,
            SortOrder = m.SortOrder,
            IsDefault = m.IsDefault
        };
}
