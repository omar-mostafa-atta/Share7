using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Constants;
using Share7.Domain.Structure;

// Aliased because Share7.Infrastructure.Curriculum is a namespace in scope here, and an unqualified
// `Curriculum` would bind to it rather than to the entity.
using CurriculumEntity = Share7.Domain.Structure.Curriculum;

namespace Share7.Infrastructure.Persistence.Configurations;

public class CurriculumAuthorityConfiguration : IEntityTypeConfiguration<CurriculumAuthority>
{
    public void Configure(EntityTypeBuilder<CurriculumAuthority> builder)
    {
        builder.ToTable("CurriculumAuthorities");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.AuthorityKey).HasMaxLength(128).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(256).IsRequired();
        builder.Property(a => a.CountryCode).HasMaxLength(2);
        builder.HasIndex(a => a.AuthorityKey).IsUnique();

        builder.HasData(
            new CurriculumAuthority
            {
                Id = EducationIds.EgyptianAuthority,
                AuthorityKey = "eg.moe",
                Name = "Egyptian Ministry of Education",
                CountryCode = "EG",
                TrustTier = 2,
                CreatedAtUtc = EvidenceContractConfiguration.SeededAtUtc
            },

            // Share7 as a publisher of its own assessment material, so that an authored exam
            // specification has an honest owner from the first deployment. **Trust tier 0** — a
            // private author. The platform writing a benchmark is not a ministry setting a paper,
            // and hanging one off the ministry's authority would put a ministry's name in front of
            // every parent reading a number the ministry never published.
            new CurriculumAuthority
            {
                Id = AssessmentIds.Share7Authority,
                AuthorityKey = "share7",
                Name = "Share7",
                TrustTier = 0,
                CreatedAtUtc = EvidenceContractConfiguration.SeededAtUtc
            });
    }
}

public class CurriculumConfiguration : IEntityTypeConfiguration<CurriculumEntity>
{
    public void Configure(EntityTypeBuilder<CurriculumEntity> builder)
    {
        builder.ToTable("Curricula");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.CurriculumKey).HasMaxLength(128).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(256).IsRequired();
        builder.HasIndex(c => c.CurriculumKey).IsUnique();

        builder.HasOne(c => c.Authority)
            .WithMany()
            .HasForeignKey(c => c.AuthorityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(new CurriculumEntity
        {
            Id = EducationIds.EgyptianNationalCurriculum,
            CurriculumKey = "eg.national",
            Name = "Egyptian National Curriculum",
            AuthorityId = EducationIds.EgyptianAuthority,
            CreatedAtUtc = EvidenceContractConfiguration.SeededAtUtc
        });
    }
}

public class CurriculumVersionConfiguration : IEntityTypeConfiguration<CurriculumVersion>
{
    public void Configure(EntityTypeBuilder<CurriculumVersion> builder)
    {
        builder.ToTable("CurriculumVersions");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.VersionLabel).HasMaxLength(64).IsRequired();
        builder.HasIndex(v => new { v.CurriculumId, v.VersionLabel }).IsUnique();

        builder.HasOne(v => v.Curriculum)
            .WithMany(c => c.Versions)
            .HasForeignKey(v => v.CurriculumId)
            .OnDelete(DeleteBehavior.Restrict);

        // IsAuthoritative is false: while the legacy typed tables are still the source of truth,
        // this version's nodes are a projection of them. The flag flips when the last reader moves.
        builder.HasData(new CurriculumVersion
        {
            Id = EducationIds.EgyptianNationalAsMigrated,
            CurriculumId = EducationIds.EgyptianNationalCurriculum,
            VersionLabel = "as-migrated",
            PublishedAtUtc = EvidenceContractConfiguration.SeededAtUtc,
            IsAuthoritative = false,
            CreatedAtUtc = EvidenceContractConfiguration.SeededAtUtc
        });
    }
}

public class CurriculumNodeKindConfiguration : IEntityTypeConfiguration<CurriculumNodeKind>
{
    public void Configure(EntityTypeBuilder<CurriculumNodeKind> builder)
    {
        builder.ToTable("CurriculumNodeKinds");
        builder.HasKey(k => k.Id);

        builder.Property(k => k.KindKey).HasMaxLength(64).IsRequired();
        builder.Property(k => k.DisplayName).HasMaxLength(128).IsRequired();
        builder.Property(k => k.ParentKindKey).HasMaxLength(64);

        builder.HasIndex(k => new { k.CurriculumVersionId, k.KindKey }).IsUnique();

        builder.HasOne(k => k.CurriculumVersion)
            .WithMany(v => v.NodeKinds)
            .HasForeignKey(k => k.CurriculumVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        // The five kinds the existing tree has, declared as data. A different curriculum declares
        // a different set and needs no code — that is the whole point of this table.
        builder.HasData(
            Kind("13d0c0b1-0000-4000-8000-000000000001", "grade", "Grade", 0, null, false, 1),
            Kind("13d0c0b1-0000-4000-8000-000000000002", "term", "Term", 1, "grade", false, 2),
            Kind("13d0c0b1-0000-4000-8000-000000000003", "subject", "Subject", 2, "term", false, 3),
            Kind("13d0c0b1-0000-4000-8000-000000000004", "chapter", "Chapter", 3, "subject", false, 4),
            Kind("13d0c0b1-0000-4000-8000-000000000005", "lesson", "Lesson", 4, "chapter", true, 5));
    }

    private static CurriculumNodeKind Kind(
        string id, string key, string name, int depth, string? parent, bool playable, int order) =>
        new()
        {
            Id = Guid.Parse(id),
            CurriculumVersionId = EducationIds.EgyptianNationalAsMigrated,
            KindKey = key,
            DisplayName = name,
            Depth = depth,
            ParentKindKey = parent,
            IsPlayable = playable,
            Order = order
        };

    /// <summary>The seeded kind ids, so the projector does not have to look them up by key.</summary>
    public static readonly IReadOnlyDictionary<string, Guid> SeededKindIds =
        new Dictionary<string, Guid>
        {
            ["grade"] = Guid.Parse("13d0c0b1-0000-4000-8000-000000000001"),
            ["term"] = Guid.Parse("13d0c0b1-0000-4000-8000-000000000002"),
            ["subject"] = Guid.Parse("13d0c0b1-0000-4000-8000-000000000003"),
            ["chapter"] = Guid.Parse("13d0c0b1-0000-4000-8000-000000000004"),
            ["lesson"] = Guid.Parse("13d0c0b1-0000-4000-8000-000000000005")
        };
}

public class CurriculumNodeConfiguration : IEntityTypeConfiguration<CurriculumNode>
{
    public void Configure(EntityTypeBuilder<CurriculumNode> builder)
    {
        builder.ToTable("CurriculumNodes");
        builder.HasKey(n => n.Id);

        // Not generated: the id is the legacy row's id, preserved so every stored reference,
        // every client cache and every UserLessonProgress.LessonId keeps resolving.
        builder.Property(n => n.Id).ValueGeneratedNever();

        builder.Property(n => n.KindKey).HasMaxLength(64).IsRequired();
        builder.Property(n => n.Path).HasMaxLength(512).IsRequired();
        builder.Property(n => n.LegacySource).HasMaxLength(32);
        builder.Property(n => n.Revision).HasDefaultValue(1);

        builder.HasIndex(n => new { n.CurriculumVersionId, n.ParentNodeId, n.Order });
        builder.HasIndex(n => new { n.CurriculumVersionId, n.KindKey });

        // "Everything under this subject" as one prefix scan, which is what every report wants.
        builder.HasIndex(n => n.Path);

        builder.HasOne(n => n.CurriculumVersion)
            .WithMany()
            .HasForeignKey(n => n.CurriculumVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        // NoAction: a self-referencing cascade is a cycle SQL Server refuses. The projector
        // deletes children before parents, which is the only place rows are removed.
        builder.HasOne(n => n.Parent)
            .WithMany()
            .HasForeignKey(n => n.ParentNodeId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(n => n.NodeKind)
            .WithMany()
            .HasForeignKey(n => n.NodeKindId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CurriculumNodeTranslationConfiguration : IEntityTypeConfiguration<CurriculumNodeTranslation>
{
    public void Configure(EntityTypeBuilder<CurriculumNodeTranslation> builder)
    {
        builder.ToTable("CurriculumNodeTranslations");
        builder.HasKey(t => new { t.NodeId, t.LangId });

        builder.Property(t => t.Title).HasMaxLength(500).IsRequired();

        builder.HasOne(t => t.Node)
            .WithMany(n => n.Translations)
            .HasForeignKey(t => t.NodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Language)
            .WithMany()
            .HasForeignKey(t => t.LangId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class NodeItemMappingConfiguration : IEntityTypeConfiguration<NodeItemMapping>
{
    public void Configure(EntityTypeBuilder<NodeItemMapping> builder)
    {
        builder.ToTable("NodeItemMappings");
        builder.HasKey(m => m.Id);

        builder.HasIndex(m => new { m.CurriculumVersionId, m.NodeId, m.ItemId, m.Role }).IsUnique();
        builder.HasIndex(m => m.ItemId);

        builder.HasOne(m => m.Item)
            .WithMany()
            .HasForeignKey(m => m.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // **No foreign key on NodeId**, and the same is true of NodeTargetMapping,
        // Enrollment.PlacementNodeId and LearnerResponse.NodeId. While CurriculumNodes is a derived
        // projection of the legacy typed tree, a constraint here would make every write
        // order-dependent on a rebuild that has nothing to do with it — publishing questions for a
        // lesson created a moment ago would fail because the projector had not caught up. The node
        // id *is* the lesson id, so the reference is exact regardless; the orphan check lives in
        // the projector, which is the only writer of node rows. The constraint arrives with
        // CurriculumVersion.IsAuthoritative.
        builder.Ignore(m => m.Node);
    }
}

public class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> builder)
    {
        builder.ToTable("Enrollments");
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.LearnerId, e.CurriculumVersionId, e.StartedAtUtc });

        // Exactly one active primary enrollment per learner. A filtered unique index rather than a
        // rule in a service, because two primaries make "what is this learner studying" ambiguous
        // and every report would have to pick arbitrarily.
        builder.HasIndex(e => e.LearnerId).IsUnique()
            .HasFilter("[IsPrimary] = 1 AND [EndedAtUtc] IS NULL")
            .HasDatabaseName("IX_Enrollments_LearnerId_ActivePrimary");

        builder.HasOne(e => e.CurriculumVersion)
            .WithMany()
            .HasForeignKey(e => e.CurriculumVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        // No FK, for the reason spelled out in NodeItemMappingConfiguration: the placement node id
        // is the grade id, and constraining it to a projection that is rebuilt would couple
        // enrolling a learner to the projector having run.
        builder.Ignore(e => e.PlacementNode);

        // No FK to AspNetUsers, matching every other user-keyed table here. Deletion is explicit;
        // UserOwnedData.ManuallyPurged carries this type.
    }
}

public class CurriculumNodeKindTranslationConfiguration : IEntityTypeConfiguration<CurriculumNodeKindTranslation>
{
    public void Configure(EntityTypeBuilder<CurriculumNodeKindTranslation> builder)
    {
        builder.ToTable("CurriculumNodeKindTranslations");
        builder.HasKey(t => new { t.NodeKindId, t.LangId });

        builder.Property(t => t.Name).HasMaxLength(64).IsRequired();

        builder.HasOne(t => t.NodeKind)
            .WithMany(k => k.Translations)
            .HasForeignKey(t => t.NodeKindId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Language)
            .WithMany()
            .HasForeignKey(t => t.LangId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
