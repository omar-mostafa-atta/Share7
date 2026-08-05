using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Constants;
using Share7.Domain.LookUps;

namespace Share7.Infrastructure.Persistence.Configurations;

public class GradeConfiguration : IEntityTypeConfiguration<Grade>
{
    public void Configure(EntityTypeBuilder<Grade> builder)
    {
        builder.ToTable("Grades");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).HasColumnName("Grade").IsRequired().HasMaxLength(100);
        builder.Property(g => g.LangId).HasColumnName("Lang_Id");

        builder.HasOne(g => g.Language)
            .WithMany()
            .HasForeignKey(g => g.LangId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(g => g.LangId);

        // The 12 English ids are the ones seeded by the original SeedGradeLookupData migration —
        // reused deliberately so existing StudentProfile.GradeId values keep resolving.
        builder.HasData(
            new Grade { Id = Guid.Parse("ee0bc8b5-de4a-4a25-afaa-cfdec9e9e27a"), Name = "Grade 1", LangId = LanguageIds.English },
            new Grade { Id = Guid.Parse("cc203a51-cc8c-4814-ae5e-587c9903e17c"), Name = "Grade 2", LangId = LanguageIds.English },
            new Grade { Id = Guid.Parse("52c97299-9356-46d5-87e9-fc1ac17d5c14"), Name = "Grade 3", LangId = LanguageIds.English },
            new Grade { Id = Guid.Parse("2b004d42-ca9c-4823-ac5b-1cb59cba0467"), Name = "Grade 4", LangId = LanguageIds.English },
            new Grade { Id = Guid.Parse("b14d62e5-56ee-42a4-916a-5554d811f72f"), Name = "Grade 5", LangId = LanguageIds.English },
            new Grade { Id = Guid.Parse("76e10c16-da74-4731-8818-448262946b70"), Name = "Grade 6", LangId = LanguageIds.English },
            new Grade { Id = Guid.Parse("c3f4e414-cde4-4929-a235-d4f09b5f748d"), Name = "Grade 7", LangId = LanguageIds.English },
            new Grade { Id = Guid.Parse("de6e690a-9bbf-4e32-afea-7e36942f7957"), Name = "Grade 8", LangId = LanguageIds.English },
            new Grade { Id = Guid.Parse("895f300d-21b4-4167-afc7-6c06677abf0e"), Name = "Grade 9", LangId = LanguageIds.English },
            new Grade { Id = Guid.Parse("f4313c32-260c-4e55-8877-c68b9d5ae33d"), Name = "Grade 10", LangId = LanguageIds.English },
            new Grade { Id = Guid.Parse("c2287d7b-e111-45ef-ac10-a3669c8a5eeb"), Name = "Grade 11", LangId = LanguageIds.English },
            new Grade { Id = Guid.Parse("38111b66-de42-4adc-b20c-464b1dd2a9d1"), Name = "Grade 12", LangId = LanguageIds.English },

            new Grade { Id = Guid.Parse("a1e6c390-7d24-4f81-9b05-3c8e2a6f4d17"), Name = "الصف الأول", LangId = LanguageIds.Arabic },
            new Grade { Id = Guid.Parse("b2f7d4a1-8e35-4092-ac16-4d9f3b7e5a28"), Name = "الصف الثاني", LangId = LanguageIds.Arabic },
            new Grade { Id = Guid.Parse("c3a8e5b2-9f46-41a3-bd27-5e0a4c8f6b39"), Name = "الصف الثالث", LangId = LanguageIds.Arabic },
            new Grade { Id = Guid.Parse("d4b9f6c3-a057-42b4-ce38-6f1b5d907c4a"), Name = "الصف الرابع", LangId = LanguageIds.Arabic },
            new Grade { Id = Guid.Parse("e5c0a7d4-b168-43c5-df49-701c6e018d5b"), Name = "الصف الخامس", LangId = LanguageIds.Arabic },
            new Grade { Id = Guid.Parse("f6d1b8e5-c279-44d6-e05a-812d7f129e6c"), Name = "الصف السادس", LangId = LanguageIds.Arabic },
            new Grade { Id = Guid.Parse("07e2c9f6-d38a-45e7-f16b-923e80230f7d"), Name = "الصف السابع", LangId = LanguageIds.Arabic },
            new Grade { Id = Guid.Parse("18f3d007-e49b-46f8-021c-a34f9134108e"), Name = "الصف الثامن", LangId = LanguageIds.Arabic },
            new Grade { Id = Guid.Parse("2904e118-f5ac-4709-132d-b450a245219f"), Name = "الصف التاسع", LangId = LanguageIds.Arabic },
            new Grade { Id = Guid.Parse("3a15f229-06bd-481a-243e-c561b35632a0"), Name = "الصف العاشر", LangId = LanguageIds.Arabic },
            new Grade { Id = Guid.Parse("4b260330-17ce-492b-354f-d672c46743b1"), Name = "الصف الحادي عشر", LangId = LanguageIds.Arabic },
            new Grade { Id = Guid.Parse("5c371441-28df-4a3c-4650-e783d57854c2"), Name = "الصف الثاني عشر", LangId = LanguageIds.Arabic }
        );
    }
}
