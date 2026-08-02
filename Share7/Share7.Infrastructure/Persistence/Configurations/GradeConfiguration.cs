using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.LookUps;

namespace Share7.Infrastructure.Persistence.Configurations;

public class GradeConfiguration : IEntityTypeConfiguration<Grade>
{
    public void Configure(EntityTypeBuilder<Grade> builder)
    {
        builder.ToTable("Grades");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.NameEn).IsRequired().HasMaxLength(100);
        builder.Property(g => g.NameAr).IsRequired().HasMaxLength(100);

        builder.HasData(
            new Grade { Id = Guid.Parse("ee0bc8b5-de4a-4a25-afaa-cfdec9e9e27a"), NameEn = "Grade 1", NameAr = "الصف الأول", Order = 1 },
            new Grade { Id = Guid.Parse("cc203a51-cc8c-4814-ae5e-587c9903e17c"), NameEn = "Grade 2", NameAr = "الصف الثاني", Order = 2 },
            new Grade { Id = Guid.Parse("52c97299-9356-46d5-87e9-fc1ac17d5c14"), NameEn = "Grade 3", NameAr = "الصف الثالث", Order = 3 },
            new Grade { Id = Guid.Parse("2b004d42-ca9c-4823-ac5b-1cb59cba0467"), NameEn = "Grade 4", NameAr = "الصف الرابع", Order = 4 },
            new Grade { Id = Guid.Parse("b14d62e5-56ee-42a4-916a-5554d811f72f"), NameEn = "Grade 5", NameAr = "الصف الخامس", Order = 5 },
            new Grade { Id = Guid.Parse("76e10c16-da74-4731-8818-448262946b70"), NameEn = "Grade 6", NameAr = "الصف السادس", Order = 6 },
            new Grade { Id = Guid.Parse("c3f4e414-cde4-4929-a235-d4f09b5f748d"), NameEn = "Grade 7", NameAr = "الصف السابع", Order = 7 },
            new Grade { Id = Guid.Parse("de6e690a-9bbf-4e32-afea-7e36942f7957"), NameEn = "Grade 8", NameAr = "الصف الثامن", Order = 8 },
            new Grade { Id = Guid.Parse("895f300d-21b4-4167-afc7-6c06677abf0e"), NameEn = "Grade 9", NameAr = "الصف التاسع", Order = 9 },
            new Grade { Id = Guid.Parse("f4313c32-260c-4e55-8877-c68b9d5ae33d"), NameEn = "Grade 10", NameAr = "الصف العاشر", Order = 10 },
            new Grade { Id = Guid.Parse("c2287d7b-e111-45ef-ac10-a3669c8a5eeb"), NameEn = "Grade 11", NameAr = "الصف الحادي عشر", Order = 11 },
            new Grade { Id = Guid.Parse("38111b66-de42-4adc-b20c-464b1dd2a9d1"), NameEn = "Grade 12", NameAr = "الصف الثاني عشر", Order = 12 }
        );
    }
}
