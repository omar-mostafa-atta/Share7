using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Constants;
using Share7.Domain.LookUps;

namespace Share7.Infrastructure.Persistence.Configurations;

public class LanguageConfiguration : IEntityTypeConfiguration<Language>
{
    public void Configure(EntityTypeBuilder<Language> builder)
    {
        builder.ToTable("Languages");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Name).IsRequired().HasMaxLength(50);
        builder.Property(l => l.Code).IsRequired().HasMaxLength(5);
        builder.Property(l => l.IsContentLanguage).HasDefaultValue(true);
        builder.Property(l => l.RequiredToPublish).HasDefaultValue(true);
        builder.Property(l => l.Direction).IsRequired().HasMaxLength(3).HasDefaultValue(LanguageDirections.LeftToRight);
        builder.HasIndex(l => l.Code).IsUnique();

        builder.HasData(
            new Language
            {
                Id = LanguageIds.English, Name = "English", Code = "en",
                IsContentLanguage = true, RequiredToPublish = true,
                Direction = LanguageDirections.LeftToRight, SortOrder = 1
            },
            new Language
            {
                Id = LanguageIds.Arabic, Name = "العربية", Code = "ar",
                IsContentLanguage = true, RequiredToPublish = true,
                Direction = LanguageDirections.RightToLeft, SortOrder = 2
            }
        );
    }
}
