using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Curriculum;

namespace Share7.Infrastructure.Persistence.Configurations;

public class SubjectConfiguration : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> builder)
    {
        builder.ToTable("Subjects");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasColumnName("Subject").IsRequired().HasMaxLength(200);
        builder.Property(s => s.LangId).HasColumnName("Lang_Id");

        builder.HasOne(s => s.Term)
            .WithMany(t => t.Subjects)
            .HasForeignKey(s => s.TermId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Language)
            .WithMany()
            .HasForeignKey(s => s.LangId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => s.TermId);
    }
}
