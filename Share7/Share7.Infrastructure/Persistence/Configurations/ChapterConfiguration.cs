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

        builder.HasIndex(c => new { c.SubjectId, c.Order }).IsUnique();
    }
}
