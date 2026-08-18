using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Constants;
using Share7.Domain.LookUps;

namespace Share7.Infrastructure.Persistence.Configurations;

public class GradeTranslationConfiguration : IEntityTypeConfiguration<GradeTranslation>
{
    /// <summary>
    /// The Egyptian pre-university ladder, in grade order. Both names are seeded for every
    /// grade — a half-translated root would leave one language with unnamed grades.
    /// </summary>
    private static readonly (Guid Id, string English, string Arabic)[] Grades =
    [
        (GradeIds.Kg1, "KG1", "الروضة الأولى"),
        (GradeIds.Kg2, "KG2", "الروضة الثانية"),
        (GradeIds.PrimaryOne, "Primary One", "الصف الأول الابتدائي"),
        (GradeIds.PrimaryTwo, "Primary Two", "الصف الثاني الابتدائي"),
        (GradeIds.PrimaryThree, "Primary Three", "الصف الثالث الابتدائي"),
        (GradeIds.PrimaryFour, "Primary Four", "الصف الرابع الابتدائي"),
        (GradeIds.PrimaryFive, "Primary Five", "الصف الخامس الابتدائي"),
        (GradeIds.PrimarySix, "Primary Six", "الصف السادس الابتدائي"),
        (GradeIds.PreparatoryOne, "Preparatory One", "الصف الأول الإعدادي"),
        (GradeIds.PreparatoryTwo, "Preparatory Two", "الصف الثاني الإعدادي"),
        (GradeIds.PreparatoryThree, "Preparatory Three", "الصف الثالث الإعدادي"),
        (GradeIds.SecondaryOne, "Secondary One", "الصف الأول الثانوي"),
        (GradeIds.SecondaryTwo, "Secondary Two", "الصف الثاني الثانوي"),
        (GradeIds.SecondaryThree, "Secondary Three", "الصف الثالث الثانوي")
    ];

    public void Configure(EntityTypeBuilder<GradeTranslation> builder)
    {
        builder.ToTable("GradeTranslations");
        builder.HasKey(t => new { t.GradeId, t.LangId });
        builder.Property(t => t.LangId).HasColumnName("Lang_Id");
        builder.Property(t => t.Name).IsRequired().HasMaxLength(100);

        builder.HasOne(t => t.Grade)
            .WithMany(g => g.Translations)
            .HasForeignKey(t => t.GradeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Language)
            .WithMany()
            .HasForeignKey(t => t.LangId)
            .OnDelete(DeleteBehavior.Restrict);

        // Supports the per-language duplicate-name check on create.
        builder.HasIndex(t => new { t.LangId, t.Name });

        builder.HasData(Grades.SelectMany(g => new[]
        {
            new GradeTranslation { GradeId = g.Id, LangId = LanguageIds.English, Name = g.English },
            new GradeTranslation { GradeId = g.Id, LangId = LanguageIds.Arabic, Name = g.Arabic }
        }));
    }
}
