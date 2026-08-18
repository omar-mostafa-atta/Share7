using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Application.Common.Models;
using Share7.Application.Equipment.Interfaces;
using Share7.Application.Equipment.Models;
using Share7.Domain.Equipment;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Equipment;

/// <summary>
/// Reads and replaces one player's saved outfit.
/// <para>
/// Storage is one row per equipped item; the response splits those rows into an <c>equipped</c>
/// list and a <c>colors</c> list. Both directions of that translation live here — nowhere else
/// needs to know the two shapes differ.
/// </para>
/// </summary>
public class EquipmentService : IEquipmentService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly EquipmentOptions _options;

    public EquipmentService(ApplicationDbContext dbContext, IOptions<EquipmentOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task<EquipmentDto> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.Equipments
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .ToListAsync(cancellationToken);

        return ToDto(rows);
    }

    public async Task<ServiceResult<EquipmentDto>> ReplaceAsync(
        Guid userId,
        UpdateEquipmentRequest request,
        CancellationToken cancellationToken = default)
    {
        // A null list means "wearing nothing", not "leave it alone" — a save always replaces the
        // whole outfit, so there is no partial-update reading of a missing field.
        var requested = request.Equipped ?? [];

        if (!TryParseBodyType(request.BodyType, out var bodyType))
        {
            return Reject(
                ApiErrors.EquipmentInvalid,
                $"bodyType '{request.BodyType}' is not a known body type.",
                ("field", "bodyType"), ("value", request.BodyType));
        }

        if (requested.Count > EquipmentLimits.MaxEquipped)
        {
            return Reject(
                ApiErrors.EquipmentInvalid,
                $"equipped has {requested.Count} entries, above the limit of {EquipmentLimits.MaxEquipped}.",
                ("field", "equipped"), ("count", requested.Count), ("limit", EquipmentLimits.MaxEquipped));
        }

        // Slots are compared case-insensitively. "Head" and "head" arriving together is a client
        // bug every time — treating them as two slots would store a contradiction the avatar
        // cannot render, and would need two rows where the unique index allows one.
        var seenSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in requested)
        {
            if (!EquipmentLimits.IsValidKey(item.SlotKey))
                return RejectKey("equipped[].slotKey", item.SlotKey);

            if (!EquipmentLimits.IsValidKey(item.CosmeticKey))
                return RejectKey("equipped[].cosmeticKey", item.CosmeticKey);

            // Optional: a cosmetic may be worn with no colour picked. Only validated if supplied.
            if (item.ColorKey is not null && !EquipmentLimits.IsValidKey(item.ColorKey))
                return RejectKey("equipped[].colorKey", item.ColorKey);

            if (!seenSlots.Add(item.SlotKey))
            {
                return Reject(
                    ApiErrors.EquipmentInvalid,
                    $"slotKey '{item.SlotKey}' appears more than once in equipped.",
                    ("field", "equipped[].slotKey"), ("value", item.SlotKey));
            }
        }

        if (_options.EnforceOwnership && requested.Count > 0)
        {
            var unowned = await FindUnownedAsync(userId, requested, cancellationToken);
            if (unowned.Count > 0)
            {
                return Reject(
                    ApiErrors.EquipmentNotOwned,
                    $"the account does not own: {string.Join(", ", unowned)}.",
                    ("field", "equipped[].cosmeticKey"), ("cosmeticKeys", unowned));
            }
        }

        var now = DateTime.UtcNow;

        var existing = await _dbContext.Equipments
            .Where(e => e.UserId == userId)
            .ToListAsync(cancellationToken);

        // Match by slot so an unchanged slot keeps its row — and its id. "Re-equipping the head
        // updates row 1" rather than deleting it and inserting a replacement, which is what the
        // one-row-per-(user, slot) rule is for.
        var bySlot = existing
            .Where(e => e.SlotKey is not null)
            .ToDictionary(e => e.SlotKey!, StringComparer.OrdinalIgnoreCase);

        // Tracked explicitly rather than read back off the change tracker: rows staged for deletion
        // are still Local, and stamping those would make the intent of the loop below unclear.
        var survivors = new List<UserEquipment>();

        foreach (var item in requested)
        {
            if (bySlot.Remove(item.SlotKey, out var row))
            {
                row.CosmeticKey = item.CosmeticKey;
                row.ColorKey = item.ColorKey;
            }
            else
            {
                row = new UserEquipment
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SlotKey = item.SlotKey,
                    CosmeticKey = item.CosmeticKey,
                    ColorKey = item.ColorKey
                };

                _dbContext.Equipments.Add(row);
            }

            survivors.Add(row);
        }

        // Whatever is left in the lookup is a slot the player is no longer wearing.
        _dbContext.Equipments.RemoveRange(bySlot.Values);

        var noItemsRow = existing.FirstOrDefault(e => e.SlotKey is null);

        if (requested.Count == 0)
        {
            // Nothing equipped, so the marker row is the only thing recording that this player has
            // saved at all. Without it the next read cannot distinguish an intentionally empty
            // outfit from a player who has never dressed.
            if (noItemsRow is null)
            {
                noItemsRow = new UserEquipment { Id = Guid.NewGuid(), UserId = userId };
                _dbContext.Equipments.Add(noItemsRow);
            }

            survivors.Add(noItemsRow);
        }
        else if (noItemsRow is not null)
        {
            // Real items are back, so the marker has nothing left to record.
            _dbContext.Equipments.Remove(noItemsRow);
        }

        // Body type and timestamp are per player, so every surviving row carries the same values.
        foreach (var row in survivors)
        {
            row.BodyType = bodyType;
            row.UpdatedAtUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<EquipmentDto>.Success(ToDto(survivors));
    }

    /// <summary>
    /// Turns the stored rows into the two-list response shape.
    /// <para>
    /// No rows at all is the "never dressed" case and is the only thing that yields a null
    /// <c>updatedAtUtc</c>. A lone no-items row means the player deliberately wears nothing: the
    /// lists come back empty but the timestamp is set, which is the whole distinction.
    /// </para>
    /// </summary>
    private static EquipmentDto ToDto(List<UserEquipment> rows)
    {
        if (rows.Count == 0)
            return new EquipmentDto { BodyType = BodyType.Male, Equipped = [], Colors = [], UpdatedAtUtc = null };

        var items = rows
            .Where(r => r.SlotKey is not null && r.CosmeticKey is not null)
            .OrderBy(r => r.SlotKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new EquipmentDto
        {
            // Per-player values are written identically to every row, so any row answers for them.
            // Read off the newest to be safe if a partial write ever left them disagreeing.
            BodyType = rows.OrderByDescending(r => r.UpdatedAtUtc).First().BodyType,

            Equipped = items
                .Select(r => new EquippedItemDto { SlotKey = r.SlotKey!, CosmeticKey = r.CosmeticKey! })
                .ToList(),

            // Items worn without a colour chosen simply do not appear in the colours list.
            Colors = items
                .Where(r => r.ColorKey is not null)
                .Select(r => new CosmeticColorDto { CosmeticKey = r.CosmeticKey!, ColorKey = r.ColorKey! })
                .ToList(),

            // Re-stamped as UTC because SQL Server hands datetime2 back as Unspecified, which the
            // serialiser writes without a trailing Z — a client doing a naive parse would then
            // shift it by its own offset. Everything stored here was written as UTC.
            UpdatedAtUtc = DateTime.SpecifyKind(rows.Max(r => r.UpdatedAtUtc), DateTimeKind.Utc)
        };
    }

    /// <summary>
    /// Which of the requested cosmetics the account has no entitlement for.
    /// <para>
    /// Ownership resolves the same chain a purchase writes: the account's entitlements, through
    /// their products, to each product's grant references — which are the client-side cosmetic
    /// ids. One query, then a set comparison in memory, because the request is capped at 32 items.
    /// </para>
    /// </summary>
    private async Task<List<string>> FindUnownedAsync(
        Guid userId,
        List<EquipmentSlotInput> requested,
        CancellationToken cancellationToken)
    {
        var wanted = requested
            .Select(i => i.CosmeticKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var owned = await _dbContext.Entitlements
            .Where(e => e.UserId == userId)
            .SelectMany(e => e.Product!.Grants.Select(g => g.Reference))
            .Distinct()
            .ToListAsync(cancellationToken);

        var ownedSet = new HashSet<string>(owned, StringComparer.OrdinalIgnoreCase);

        return wanted.Where(key => !ownedSet.Contains(key)).ToList();
    }

    private static bool TryParseBodyType(string? text, out BodyType bodyType)
    {
        // Absent means the documented default rather than a rejection — a client that has never
        // shown a body-type picker should still be able to save an outfit.
        if (string.IsNullOrWhiteSpace(text))
        {
            bodyType = BodyType.Male;
            return true;
        }

        return WireEnum.TryFromWire(text, out bodyType);
    }

    private static ServiceResult<EquipmentDto> RejectKey(string field, string? value)
    {
        var reason = string.IsNullOrEmpty(value)
            ? "is empty"
            : value.Length > EquipmentLimits.MaxKeyLength
                ? $"is {value.Length} characters, above the limit of {EquipmentLimits.MaxKeyLength}"
                : "contains characters outside A-Z a-z 0-9 . _ -";

        return Reject(
            ApiErrors.EquipmentInvalid,
            $"{field} {reason}.",
            ("field", field), ("value", value));
    }

    private static ServiceResult<EquipmentDto> Reject(
        ApiErrorCode code,
        string message,
        params (string Key, object? Value)[] details) =>
        ServiceResult<EquipmentDto>.Unprocessable(
            code,
            message,
            details.ToDictionary(d => d.Key, d => d.Value));
}
