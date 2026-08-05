using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Entities;

namespace Share7.Infrastructure.Persistence.Configurations;

public class StudentProfileConfiguration : IEntityTypeConfiguration<StudentProfile>
{
    public void Configure(EntityTypeBuilder<StudentProfile> builder)
    {
        builder.ToTable("StudentProfiles");
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => p.UserId).IsUnique();
        builder.Property(p => p.FullName).IsRequired().HasMaxLength(200);
        builder.Property(p => p.PhoneNumber).IsRequired().HasMaxLength(30);
        builder.Property(p => p.Email).HasMaxLength(256);

        builder.HasOne(p => p.Grade)
            .WithMany()
            .HasForeignKey(p => p.GradeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
