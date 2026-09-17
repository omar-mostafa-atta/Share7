using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Curriculum;

namespace Share7.Infrastructure.Persistence.Configurations;

public class LessonTranslationConfiguration : IEntityTypeConfiguration<LessonTranslation>
{
    public void Configure(EntityTypeBuilder<LessonTranslation> builder)
    {
        builder.ToTable("LessonTranslations");
        builder.HasKey(t => new { t.LessonId, t.LangId });
        builder.Property(t => t.LangId).HasColumnName("Lang_Id");
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);

        builder.HasOne(t => t.Lesson)
            .WithMany(l => l.Translations)
            .HasForeignKey(t => t.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Language)
            .WithMany()
            .HasForeignKey(t => t.LangId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.LangId, t.Name });
    }
}
