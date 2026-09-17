using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Progress;

namespace Share7.Infrastructure.Persistence.Configurations;

public class UserQuestionProgressConfiguration : IEntityTypeConfiguration<UserQuestionProgress>
{
    public void Configure(EntityTypeBuilder<UserQuestionProgress> builder)
    {
        builder.ToTable("UserQuestionProgress");
        builder.HasKey(p => new { p.UserId, p.GameId, p.QuestionId });

        // Every read is "this student's rows for this lesson" — the wrong-question report and
        // the rollups both hit it.
        builder.HasIndex(p => new { p.UserId, p.GameId, p.LessonId });

        // No FK to AspNetUsers: nothing else in this schema has one either, and Identity's
        // delete path is explicit rather than cascading (see UserAdminService.DeleteUserAsync).
        builder.HasOne<Domain.Games.Game>()
            .WithMany()
            .HasForeignKey(p => p.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        // Retired questions keep their progress rows — a re-upload soft-deletes questions
        // rather than removing them, so this never dangles.
        builder.HasOne<Domain.Curriculum.Question>()
            .WithMany()
            .HasForeignKey(p => p.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
