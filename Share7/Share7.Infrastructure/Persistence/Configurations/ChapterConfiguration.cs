using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Curriculum;

namespace Share7.Infrastructure.Persistence.Configurations;

public class ChapterConfiguration : IEntityTypeConfiguration<Chapter>
{
    public void Configure(EntityTypeBuilder<Chapter> builder)
    {
        builder.ToTable("Chapters");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasColumnName("Chapter").IsRequired().HasMaxLength(200);
        builder.Property(c => c.LangId).HasColumnName("Lang_Id");

        builder.HasOne(c => c.Subject)
            .WithMany(s => s.Chapters)
            .HasForeignKey(c => c.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Language)
            .WithMany()
            .HasForeignKey(c => c.LangId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.SubjectId);
    }
}
