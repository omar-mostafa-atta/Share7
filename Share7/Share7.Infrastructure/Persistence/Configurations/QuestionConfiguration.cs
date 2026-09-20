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

        // Restrict, not Cascade: the lesson already cascades into this table, and a second cascade
        // path into one table is the shape SQL Server refuses. It is also the wrong behaviour —
        // an item version outlives every rendering of it and must not be removable through one.
        builder.HasOne(q => q.ItemVersion)
            .WithMany(v => v.Localizations)
            .HasForeignKey(q => q.ItemVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(q => q.Language)
            .WithMany()
            .HasForeignKey(q => q.LangId)
            .OnDelete(DeleteBehavior.Restrict);

        // CorrectChoiceId is intentionally an unconstrained column — see Question.CorrectChoiceId.
        builder.Property(q => q.CorrectChoiceId).IsRequired();

        // The hot path: "give me the current question set for this lesson in this language".
        // Questions stay language-partitioned even though the tree above them no longer is.
        builder.HasIndex(q => new { q.LessonId, q.LangId, q.IsActive });

        // The evidence read: given an item version, which rendering did the learner see. Also the
        // join the importer uses to recognise an item it has published before.
        builder.HasIndex(q => new { q.ItemVersionId, q.LangId });
    }
}
