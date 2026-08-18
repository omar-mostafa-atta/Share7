using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Curriculum;

namespace Share7.Infrastructure.Persistence.Configurations;

public class RecoveryQuestionChoiceConfiguration : IEntityTypeConfiguration<RecoveryQuestionChoice>
{
    public void Configure(EntityTypeBuilder<RecoveryQuestionChoice> builder)
    {
        builder.ToTable("RecoveryQuestionChoices");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Text).HasColumnName("Choice").IsRequired().HasMaxLength(500);

        builder.HasOne(c => c.RecoveryQuestion)
            .WithMany(q => q.Choices)
            .HasForeignKey(c => c.RecoveryQuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.RecoveryQuestionId);
    }
}
