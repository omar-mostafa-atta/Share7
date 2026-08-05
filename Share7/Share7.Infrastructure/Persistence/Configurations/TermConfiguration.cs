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
        builder.Property(t => t.Name).HasColumnName("Term").IsRequired().HasMaxLength(100);
        builder.Property(t => t.LangId).HasColumnName("Lang_Id");

        builder.HasOne(t => t.Grade)
            .WithMany(g => g.Terms)
            .HasForeignKey(t => t.GradeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Language)
            .WithMany()
            .HasForeignKey(t => t.LangId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.GradeId);
    }
}
