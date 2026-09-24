using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Curriculum;

namespace Share7.Infrastructure.Persistence.Configurations;

public class ChapterConfiguration : IEntityTypeConfiguration<Chapter>
{
    public void Configure(EntityTypeBuilder<Chapter> builder)
    {
        builder.ToTable("Chapters");
        builder.HasKey(c => c.Id);

        builder.HasOne(c => c.Subject)
            .WithMany(s => s.Chapters)
            .HasForeignKey(c => c.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.SubjectId, c.Order }).IsUnique().HasFilter("[RetiredAtUtc] IS NULL");

        // Retired rows are the compatibility copy of a retired node: kept (progress and evidence
        // name them), hidden from every reader still on the typed tables. Code that must see them
        // — the structure writer, the projector — says so with IgnoreQueryFilters().
        builder.HasQueryFilter(c => c.RetiredAtUtc == null);
    }
}
