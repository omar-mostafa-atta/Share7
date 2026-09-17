using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Curriculum;

namespace Share7.Infrastructure.Persistence.Configurations;

public class QuestionChoiceConfiguration : IEntityTypeConfiguration<QuestionChoice>
{
    public void Configure(EntityTypeBuilder<QuestionChoice> builder)
    {
        builder.ToTable("QuestionChoices");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Text).HasColumnName("Choice").IsRequired().HasMaxLength(500);

        builder.HasOne(c => c.Question)
            .WithMany(q => q.Choices)
            .HasForeignKey(c => c.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.QuestionId);
    }
}
