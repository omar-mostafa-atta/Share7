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

        builder.HasOne(s => s.Term)
            .WithMany(t => t.Subjects)
            .HasForeignKey(s => s.TermId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.TermId, s.Order }).IsUnique();
    }
}
