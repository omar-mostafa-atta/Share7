using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Curriculum;

namespace Share7.Infrastructure.Persistence.Configurations;

public class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> builder)
    {
        builder.ToTable("Questions");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.Text).HasColumnName("Question").IsRequired().HasMaxLength(1000);
        builder.Property(q => q.LangId).HasColumnName("Lang_Id");
        builder.Property(q => q.IsActive).HasDefaultValue(true);

        builder.HasOne(q => q.Lesson)
            .WithMany(l => l.Questions)
            .HasForeignKey(q => q.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(q => q.Language)
            .WithMany()
            .HasForeignKey(q => q.LangId)
            .OnDelete(DeleteBehavior.Restrict);

        // CorrectChoiceId is intentionally an unconstrained column — see Question.CorrectChoiceId.
        builder.Property(q => q.CorrectChoiceId).IsRequired();

        // The hot path: "give me the current question set for this lesson in this language".
        // Questions stay language-partitioned even though the tree above them no longer is.
        builder.HasIndex(q => new { q.LessonId, q.LangId, q.IsActive });
    }
}
