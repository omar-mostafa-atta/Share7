using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Curriculum;

namespace Share7.Infrastructure.Persistence.Configurations;

public class LessonConfiguration : IEntityTypeConfiguration<Lesson>
{
    public void Configure(EntityTypeBuilder<Lesson> builder)
    {
        builder.ToTable("Lessons");
        builder.HasKey(l => l.Id);

        builder.HasOne(l => l.Chapter)
            .WithMany(c => c.Lessons)
            .HasForeignKey(l => l.ChapterId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique because this is the order the unlock chain steps through: lesson N+1 opens
        // once lesson N is completed, so two lessons sharing a position is not resolvable.
        builder.HasIndex(l => new { l.ChapterId, l.Order }).IsUnique();
    }
}
