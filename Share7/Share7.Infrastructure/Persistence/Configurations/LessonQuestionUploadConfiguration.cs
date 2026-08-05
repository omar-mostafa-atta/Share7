using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Curriculum;

namespace Share7.Infrastructure.Persistence.Configurations;

public class LessonQuestionUploadConfiguration : IEntityTypeConfiguration<LessonQuestionUpload>
{
    public void Configure(EntityTypeBuilder<LessonQuestionUpload> builder)
    {
        builder.ToTable("LessonQuestionUploads");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.FileName).IsRequired().HasMaxLength(260);

        builder.HasOne(u => u.Lesson)
            .WithMany()
            .HasForeignKey(u => u.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(u => new { u.LessonId, u.Version }).IsUnique();
    }
}
