using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Curriculum;

namespace Share7.Infrastructure.Persistence.Configurations;

public class LessonConfiguration : IEntityTypeConfiguration<Lesson>
{
    public void Configure(EntityTypeBuilder<Lesson> builder)
    {
        builder.ToTable("Lessons");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Name).HasColumnName("Lesson").IsRequired().HasMaxLength(200);
        builder.Property(l => l.LangId).HasColumnName("Lang_Id");
        builder.Property(l => l.QuestionsVersion).HasDefaultValue(0);

        builder.HasOne(l => l.Chapter)
            .WithMany(c => c.Lessons)
            .HasForeignKey(l => l.ChapterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Language)
            .WithMany()
            .HasForeignKey(l => l.LangId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.ChapterId);
    }
}
