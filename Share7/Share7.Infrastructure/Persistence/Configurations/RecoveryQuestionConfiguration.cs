using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Curriculum;

namespace Share7.Infrastructure.Persistence.Configurations;

public class RecoveryQuestionConfiguration : IEntityTypeConfiguration<RecoveryQuestion>
{
    public void Configure(EntityTypeBuilder<RecoveryQuestion> builder)
    {
        builder.ToTable("RecoveryQuestions");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.Text).HasColumnName("Question").IsRequired().HasMaxLength(1000);
        builder.Property(q => q.LangId).HasColumnName("Lang_Id");
        builder.Property(q => q.IsActive).HasDefaultValue(true);

        builder.HasOne(q => q.Lesson)
            .WithMany(l => l.RecoveryQuestions)
            .HasForeignKey(q => q.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(q => q.Language)
            .WithMany()
            .HasForeignKey(q => q.LangId)
            .OnDelete(DeleteBehavior.Restrict);

        // CorrectChoiceId is intentionally an unconstrained column — see RecoveryQuestion.CorrectChoiceId.
        builder.Property(q => q.CorrectChoiceId).IsRequired();

        // The hot path: "give me the current recovery set for this lesson in this language".
        builder.HasIndex(q => new { q.LessonId, q.LangId, q.IsActive });
    }
}
