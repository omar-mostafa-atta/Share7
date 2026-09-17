using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Curriculum;

namespace Share7.Infrastructure.Persistence.Configurations;

public class ChapterTranslationConfiguration : IEntityTypeConfiguration<ChapterTranslation>
{
    public void Configure(EntityTypeBuilder<ChapterTranslation> builder)
    {
        builder.ToTable("ChapterTranslations");
        builder.HasKey(t => new { t.ChapterId, t.LangId });
        builder.Property(t => t.LangId).HasColumnName("Lang_Id");
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);

        builder.HasOne(t => t.Chapter)
            .WithMany(c => c.Translations)
            .HasForeignKey(t => t.ChapterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Language)
            .WithMany()
            .HasForeignKey(t => t.LangId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.LangId, t.Name });
    }
}
