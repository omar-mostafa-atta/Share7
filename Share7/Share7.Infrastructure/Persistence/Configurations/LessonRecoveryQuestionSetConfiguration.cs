using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Curriculum;

namespace Share7.Infrastructure.Persistence.Configurations;

public class LessonRecoveryQuestionSetConfiguration : IEntityTypeConfiguration<LessonRecoveryQuestionSet>
{
    public void Configure(EntityTypeBuilder<LessonRecoveryQuestionSet> builder)
    {
        builder.ToTable("LessonRecoveryQuestionSets");
        builder.HasKey(s => new { s.LessonId, s.LangId });
        builder.Property(s => s.LangId).HasColumnName("Lang_Id");
        builder.Property(s => s.Version).HasDefaultValue(0);

        builder.HasOne(s => s.Lesson)
            .WithMany(l => l.RecoveryQuestionSets)
            .HasForeignKey(s => s.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Language)
            .WithMany()
            .HasForeignKey(s => s.LangId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
