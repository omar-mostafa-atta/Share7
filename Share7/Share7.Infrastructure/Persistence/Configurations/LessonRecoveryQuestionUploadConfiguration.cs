using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Curriculum;

namespace Share7.Infrastructure.Persistence.Configurations;

public class LessonRecoveryQuestionUploadConfiguration : IEntityTypeConfiguration<LessonRecoveryQuestionUpload>
{
    public void Configure(EntityTypeBuilder<LessonRecoveryQuestionUpload> builder)
    {
        builder.ToTable("LessonRecoveryQuestionUploads");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.FileName).IsRequired().HasMaxLength(260);
        builder.Property(u => u.LangId).HasColumnName("Lang_Id");

        builder.HasOne(u => u.Lesson)
            .WithMany()
            .HasForeignKey(u => u.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(u => u.Language)
            .WithMany()
            .HasForeignKey(u => u.LangId)
            .OnDelete(DeleteBehavior.Restrict);

        // Recovery versions are per language, so the uniqueness that stops two concurrent uploads
        // producing the same version number is scoped that way too.
        builder.HasIndex(u => new { u.LessonId, u.LangId, u.Version }).IsUnique();
    }
}
