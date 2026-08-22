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

        // Text rather than an integer, like every other stored enum here: this table is read to
        // explain a change, and MANUAL_ENTRY says what 1 does not.
        builder.Property(u => u.Source)
            .HasConversion(EnumWire.Converter<QuestionSetSource>())
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(u => u.LangId).HasColumnName("Lang_Id");

        builder.HasOne(u => u.Lesson)
            .WithMany()
            .HasForeignKey(u => u.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(u => u.Language)
            .WithMany()
            .HasForeignKey(u => u.LangId)
            .OnDelete(DeleteBehavior.Restrict);

        // Versions are per language now, so the uniqueness that stops two concurrent uploads
        // producing the same version number has to be scoped that way too.
        builder.HasIndex(u => new { u.LessonId, u.LangId, u.Version }).IsUnique();
    }
}
