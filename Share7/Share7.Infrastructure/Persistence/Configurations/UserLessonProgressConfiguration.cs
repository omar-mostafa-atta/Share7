using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Progress;

namespace Share7.Infrastructure.Persistence.Configurations;

public class UserLessonProgressConfiguration : IEntityTypeConfiguration<UserLessonProgress>
{
    public void Configure(EntityTypeBuilder<UserLessonProgress> builder)
    {
        builder.ToTable("UserLessonProgress");
        builder.HasKey(p => new { p.UserId, p.GameId, p.LessonId });

        builder.Property(p => p.CompletionState).HasConversion<int>();

        // Rollups walk a student's whole game at once (chapter/subject/term summaries).
        builder.HasIndex(p => new { p.UserId, p.GameId });

        builder.HasOne<Domain.Games.Game>()
            .WithMany()
            .HasForeignKey(p => p.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a lesson takes its progress with it; the lesson no longer exists to report on.
        // NoAction rather than Cascade: Questions -> Lessons already cascades, and SQL Server
        // rejects two cascade paths arriving at UserQuestionProgress/UserLessonProgress.
        builder.HasOne<Domain.Curriculum.Lesson>()
            .WithMany()
            .HasForeignKey(p => p.LessonId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
