using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Organizations;

namespace Share7.Infrastructure.Persistence.Configurations;

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.OrgKey).HasMaxLength(64).IsRequired();
        builder.HasIndex(o => o.OrgKey).IsUnique();

        builder.Property(o => o.Name).HasMaxLength(256).IsRequired();
        builder.Property(o => o.CountryCode).HasMaxLength(2);

        builder.HasIndex(o => o.ParentOrgId);

        // Restrict, not cascade. Deleting a district must not silently take its schools — and with
        // them every cohort, every enrolment and the context every observation was collected in.
        builder.HasOne(o => o.ParentOrg)
            .WithMany(o => o.Children)
            .HasForeignKey(o => o.ParentOrgId)
            .OnDelete(DeleteBehavior.Restrict);

        // No FK to ItemBanks: the bank is created in the same transaction and owned by the org, but
        // constraining it would make the two rows order-dependent on each other for no gain.
    }
}

public class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("Memberships");
        builder.HasKey(m => m.Id);

        // One row per (user, org, role). A second grant of the same role revives this one rather
        // than stacking, so "who could see this child's data, and when" stays readable.
        builder.HasIndex(m => new { m.OrgId, m.UserId, m.Role }).IsUnique();

        builder.HasIndex(m => m.UserId);

        builder.HasOne(m => m.Org)
            .WithMany(o => o.Memberships)
            .HasForeignKey(m => m.OrgId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(m => m.IsActive);

        // No FK to AspNetUsers, matching every other user-keyed table here.
    }
}

public class CohortConfiguration : IEntityTypeConfiguration<Cohort>
{
    public void Configure(EntityTypeBuilder<Cohort> builder)
    {
        builder.ToTable("Cohorts");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(256).IsRequired();
        builder.Property(c => c.AcademicPeriod).HasMaxLength(64);

        builder.HasIndex(c => new { c.OrgId, c.Status });

        builder.HasOne(c => c.Org)
            .WithMany(o => o.Cohorts)
            .HasForeignKey(c => c.OrgId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.CurriculumVersion)
            .WithMany()
            .HasForeignKey(c => c.CurriculumVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        // No FK on PlacementNodeId, for the reason EnrollmentConfiguration gives: the node table is
        // a rebuilt projection, and constraining against it would couple creating a class to the
        // projector having run.
    }
}

public class CohortMembershipConfiguration : IEntityTypeConfiguration<CohortMembership>
{
    public void Configure(EntityTypeBuilder<CohortMembership> builder)
    {
        builder.ToTable("CohortMemberships");
        builder.HasKey(cm => cm.Id);

        // One current membership per person per cohort; a filtered index rather than a plain unique
        // one, because somebody who left in November and rejoined in February is two real rows.
        builder.HasIndex(cm => new { cm.CohortId, cm.UserId }).IsUnique()
            .HasFilter("[LeftAtUtc] IS NULL")
            .HasDatabaseName("IX_CohortMemberships_Cohort_User_Active");

        builder.HasIndex(cm => cm.UserId);

        builder.HasOne(cm => cm.Cohort)
            .WithMany(c => c.Memberships)
            .HasForeignKey(cm => cm.CohortId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(cm => cm.IsActive);
    }
}

public class GuardianLinkConfiguration : IEntityTypeConfiguration<GuardianLink>
{
    public void Configure(EntityTypeBuilder<GuardianLink> builder)
    {
        builder.ToTable("GuardianLinks");
        builder.HasKey(g => g.Id);

        builder.HasIndex(g => new { g.GuardianUserId, g.LearnerUserId }).IsUnique()
            .HasFilter("[RevokedAtUtc] IS NULL")
            .HasDatabaseName("IX_GuardianLinks_Pair_Active");

        builder.HasIndex(g => g.LearnerUserId);

        builder.Ignore(g => g.IsEffective);
    }
}

public class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> builder)
    {
        builder.ToTable("Assignments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Title).HasMaxLength(256).IsRequired();

        builder.HasIndex(a => new { a.CohortId, a.DueAtUtc });

        builder.HasOne(a => a.Cohort)
            .WithMany()
            .HasForeignKey(a => a.CohortId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(a => a.IsOpen);

        // No FK on NodeId (rebuilt projection) and none on AssessmentFormId either: a form is
        // sealed and immutable, and an assignment that outlives one should fail to resolve rather
        // than block the form's own lifecycle.
    }
}

public class CurriculumOverlayConfiguration : IEntityTypeConfiguration<CurriculumOverlay>
{
    public void Configure(EntityTypeBuilder<CurriculumOverlay> builder)
    {
        builder.ToTable("CurriculumOverlays");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.OverlayKey).HasMaxLength(128).IsRequired();
        builder.HasIndex(o => o.OverlayKey).IsUnique();

        builder.Property(o => o.Name).HasMaxLength(256).IsRequired();
        builder.Property(o => o.SourceNote).HasMaxLength(1024);

        builder.HasIndex(o => new { o.OrgId, o.CurriculumVersionId });

        builder.HasOne(o => o.Org)
            .WithMany()
            .HasForeignKey(o => o.OrgId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.CurriculumVersion)
            .WithMany()
            .HasForeignKey(o => o.CurriculumVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class CurriculumOverlayEditConfiguration : IEntityTypeConfiguration<CurriculumOverlayEdit>
{
    public void Configure(EntityTypeBuilder<CurriculumOverlayEdit> builder)
    {
        builder.ToTable("CurriculumOverlayEdits");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Note).HasMaxLength(512);

        builder.HasIndex(e => new { e.OverlayId, e.ApplyOrder });

        builder.HasOne(e => e.Overlay)
            .WithMany(o => o.Edits)
            .HasForeignKey(e => e.OverlayId)
            .OnDelete(DeleteBehavior.Cascade);

        // No FKs to CurriculumNodes: an overlay names nodes in a projection that is rebuilt, and a
        // constraint here would make publishing a school's timetable order-dependent on a rebuild
        // that has nothing to do with it.
    }
}
