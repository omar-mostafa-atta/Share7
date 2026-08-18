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

        // Unique so the ladder can never be ambiguous — the unlock chain reads this order.
        builder.HasIndex(g => g.Order).IsUnique();

        // The Egyptian pre-university ladder: 2 kindergarten years, 6 primary, 3 preparatory,
        // 3 secondary. Secondary is deliberately not split into علمي / أدبي — the
        // specializations are modelled as subjects, which keeps this list linear.
        builder.HasData(
            new Grade { Id = GradeIds.Kg1, Order = 1 },
            new Grade { Id = GradeIds.Kg2, Order = 2 },
            new Grade { Id = GradeIds.PrimaryOne, Order = 3 },
            new Grade { Id = GradeIds.PrimaryTwo, Order = 4 },
            new Grade { Id = GradeIds.PrimaryThree, Order = 5 },
            new Grade { Id = GradeIds.PrimaryFour, Order = 6 },
            new Grade { Id = GradeIds.PrimaryFive, Order = 7 },
            new Grade { Id = GradeIds.PrimarySix, Order = 8 },
            new Grade { Id = GradeIds.PreparatoryOne, Order = 9 },
            new Grade { Id = GradeIds.PreparatoryTwo, Order = 10 },
            new Grade { Id = GradeIds.PreparatoryThree, Order = 11 },
            new Grade { Id = GradeIds.SecondaryOne, Order = 12 },
            new Grade { Id = GradeIds.SecondaryTwo, Order = 13 },
            new Grade { Id = GradeIds.SecondaryThree, Order = 14 }
        );
    }
}
