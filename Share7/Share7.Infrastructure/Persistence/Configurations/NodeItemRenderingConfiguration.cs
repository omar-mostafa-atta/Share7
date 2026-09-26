using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Content;

namespace Share7.Infrastructure.Persistence.Configurations;

/// <summary>
/// The renderings of a curriculum the game does not serve yet. Sized exactly as <c>Questions</c> and
/// <c>QuestionChoices</c> are, so a question written under one curriculum fits under the other.
/// </summary>
public class NodeItemRenderingConfiguration : IEntityTypeConfiguration<NodeItemRendering>
{
    public void Configure(EntityTypeBuilder<NodeItemRendering> builder)
    {
        builder.ToTable("NodeItemRenderings");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Text).IsRequired().HasMaxLength(1000);
        builder.Property(r => r.IsActive).HasDefaultValue(true);
        builder.Property(r => r.Role).HasDefaultValue(NodeItemRole.Core);
        builder.Property(r => r.CorrectChoiceId).IsRequired();

        // NoAction: nodes are never deleted, and a node is not the owner of the item it renders.
        builder.HasOne(r => r.Node)
            .WithMany()
            .HasForeignKey(r => r.NodeId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(r => r.ItemVersion)
            .WithMany()
            .HasForeignKey(r => r.ItemVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Language)
            .WithMany()
            .HasForeignKey(r => r.LangId)
            .OnDelete(DeleteBehavior.Restrict);

        // The same two reads Questions is indexed for: a node's set, and a version's renderings.
        builder.HasIndex(r => new { r.NodeId, r.Role, r.LangId, r.IsActive });
        builder.HasIndex(r => new { r.ItemVersionId, r.LangId });
    }
}

public class NodeItemRenderingChoiceConfiguration : IEntityTypeConfiguration<NodeItemRenderingChoice>
{
    public void Configure(EntityTypeBuilder<NodeItemRenderingChoice> builder)
    {
        builder.ToTable("NodeItemRenderingChoices");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Text).IsRequired().HasMaxLength(500);

        builder.HasOne(c => c.Rendering)
            .WithMany(r => r.Choices)
            .HasForeignKey(c => c.RenderingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.RenderingId);
    }
}
