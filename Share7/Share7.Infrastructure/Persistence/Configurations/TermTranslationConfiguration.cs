using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Curriculum;

namespace Share7.Infrastructure.Persistence.Configurations;

public class TermTranslationConfiguration : IEntityTypeConfiguration<TermTranslation>
{
    public void Configure(EntityTypeBuilder<TermTranslation> builder)
    {
        builder.ToTable("TermTranslations");
        builder.HasKey(t => new { t.TermId, t.LangId });
        builder.Property(t => t.LangId).HasColumnName("Lang_Id");
        builder.Property(t => t.Name).IsRequired().HasMaxLength(100);

        builder.HasOne(t => t.Term)
            .WithMany(t => t.Translations)
            .HasForeignKey(t => t.TermId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Language)
            .WithMany()
            .HasForeignKey(t => t.LangId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.LangId, t.Name });
    }
}
