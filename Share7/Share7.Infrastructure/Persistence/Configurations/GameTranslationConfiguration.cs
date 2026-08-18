using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Games;

namespace Share7.Infrastructure.Persistence.Configurations;

public class GameTranslationConfiguration : IEntityTypeConfiguration<GameTranslation>
{
    public void Configure(EntityTypeBuilder<GameTranslation> builder)
    {
        builder.ToTable("GameTranslations");
        builder.HasKey(t => new { t.GameId, t.LangId });
        builder.Property(t => t.LangId).HasColumnName("Lang_Id");
        builder.Property(t => t.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).IsRequired().HasMaxLength(1000);

        builder.HasOne(t => t.Game)
            .WithMany(g => g.Translations)
            .HasForeignKey(t => t.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Language)
            .WithMany()
            .HasForeignKey(t => t.LangId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
