using Microsoft.EntityFrameworkCore;
using Share7.Application.Engine.Interfaces;
using Share7.Application.Engine.Models;
using Share7.Domain.Content;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Engine;

/// <inheritdoc cref="ILessonContentReader"/>
public sealed class LessonContentReader : ILessonContentReader
{
    private readonly ApplicationDbContext _db;

    public LessonContentReader(ApplicationDbContext db) => _db = db;

    public async Task<LessonContentDto?> ReadAsync(Guid lessonId, CancellationToken cancellationToken = default)
    {
        var live = await _db.CurriculumNodes.AsNoTracking().AnyAsync(
            n => n.Id == lessonId && n.KindKey == NodeKinds.Lesson && n.RetiredAtUtc == null,
            cancellationToken);

        if (!live)
            return null;

        var sets = await _db.PublishedItemSets
            .AsNoTracking()
            .Where(s => s.NodeId == lessonId)
            .Select(s => new ContentSetDto(s.Role, s.LangId, s.Version, s.ItemCount, s.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        var rows = await LoadActiveRowsAsync(_db, lessonId, cancellationToken);

        return new LessonContentDto
        {
            LessonId = lessonId,
            Sets = sets.OrderBy(s => s.Role).ThenBy(s => s.LangId).ToList(),
            Items = Group(rows)
        };
    }

    /// <summary>One served rendering, with what identifies it and what the game receives.</summary>
    internal sealed record ActiveRow(
        Guid Id,
        NodeItemRole Role,
        Guid LangId,
        int RowNumber,
        int Version,
        string Text,
        Guid CorrectChoiceId,
        Guid ItemVersionId,
        Guid ItemId,
        IReadOnlyList<ContentChoiceDto> Choices)
    {
        public int CorrectIndex
        {
            get
            {
                for (var i = 0; i < Choices.Count; i++)
                    if (Choices[i].Id == CorrectChoiceId) return i;
                return -1;
            }
        }
    }

    /// <summary>Every served rendering of a lesson, every pool and language. Shared with the publisher.</summary>
    internal static async Task<List<ActiveRow>> LoadActiveRowsAsync(
        ApplicationDbContext db, Guid lessonId, CancellationToken cancellationToken)
    {
        var rows = await db.ItemLocalizations
            .AsNoTracking()
            .Where(q => q.LessonId == lessonId && q.IsActive)
            .Select(q => new
            {
                q.Id,
                q.Role,
                q.LangId,
                q.RowNumber,
                q.Version,
                q.Text,
                q.CorrectChoiceId,
                q.ItemVersionId,
                ItemId = q.ItemVersion!.ItemId,
                Choices = q.Choices
                    .OrderBy(c => c.OrderIndex)
                    .Select(c => new ContentChoiceDto(c.Id, c.Text))
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new ActiveRow(
                r.Id, r.Role, r.LangId, r.RowNumber, r.Version, r.Text, r.CorrectChoiceId,
                r.ItemVersionId, r.ItemId, r.Choices))
            .ToList();
    }

    /// <summary>
    /// Renderings grouped into items. An item's position is its lowest row number across languages
    /// — they agree for everything the importers ever wrote, and this is only a tie-break for
    /// anything that does not.
    /// </summary>
    internal static List<ContentItemDto> Group(IEnumerable<ActiveRow> rows) =>
        rows
            .GroupBy(r => (r.Role, r.ItemId))
            .Select(g => new ContentItemDto
            {
                ItemId = g.Key.ItemId,
                Role = g.Key.Role,
                Order = g.Min(r => r.RowNumber),
                Renderings = g
                    .GroupBy(r => r.LangId)
                    .Select(byLang => byLang.OrderBy(r => r.RowNumber).First())
                    .Select(r => new ContentRenderingDto(
                        r.LangId, r.Id, r.ItemVersionId, r.Text, r.Choices, r.CorrectIndex))
                    .ToList()
            })
            .OrderBy(i => i.Role)
            .ThenBy(i => i.Order)
            .ThenBy(i => i.ItemId)
            .ToList();
}
