using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Curriculum;

namespace Share7.Infrastructure.Persistence.Configurations;

public class TermConfiguration : IEntityTypeConfiguration<Term>
{
    public void Configure(EntityTypeBuilder<Term> builder)
    {
        builder.ToTable("Terms");
        builder.HasKey(t => t.Id);

        builder.HasOne(t => t.Grade)
            .WithMany(g => g.Terms)
            .HasForeignKey(t => t.GradeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique so sibling order is never ambiguous — the unlock chain walks it.
        builder.HasIndex(t => new { t.GradeId, t.Order }).IsUnique();
    }
}
