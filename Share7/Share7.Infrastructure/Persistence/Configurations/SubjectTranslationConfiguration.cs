using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Curriculum;

namespace Share7.Infrastructure.Persistence.Configurations;

public class SubjectTranslationConfiguration : IEntityTypeConfiguration<SubjectTranslation>
{
    public void Configure(EntityTypeBuilder<SubjectTranslation> builder)
    {
        builder.ToTable("SubjectTranslations");
        builder.HasKey(t => new { t.SubjectId, t.LangId });
        builder.Property(t => t.LangId).HasColumnName("Lang_Id");
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);

        builder.HasOne(t => t.Subject)
            .WithMany(s => s.Translations)
            .HasForeignKey(t => t.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Language)
            .WithMany()
            .HasForeignKey(t => t.LangId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.LangId, t.Name });
    }
}
