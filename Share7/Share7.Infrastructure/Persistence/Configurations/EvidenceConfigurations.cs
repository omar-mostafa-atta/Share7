using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Share7.Domain.Constants;
using Share7.Domain.Evidence;

namespace Share7.Infrastructure.Persistence.Configurations;

public class EvidenceContractConfiguration : IEntityTypeConfiguration<EvidenceContract>
{
    public void Configure(EntityTypeBuilder<EvidenceContract> builder)
    {
        builder.ToTable("EvidenceContracts");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.ContractKey).HasMaxLength(128).IsRequired();
        builder.Property(c => c.InteractionKind).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(512);

        builder.HasIndex(c => c.ContractKey).IsUnique();

        // Resolution is most-specific-wins over (GameId, ModeId, InteractionKind), so two contracts
        // at the same specificity would make the winner arbitrary. The index makes that an insert
        // failure rather than a silent coin toss.
        builder.HasIndex(c => new { c.GameId, c.ModeId, c.InteractionKind }).IsUnique();

        builder.HasOne(c => c.Game)
            .WithMany()
            .HasForeignKey(c => c.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction, not Cascade: SQL Server refuses two cascade paths reaching this table via Game.
        builder.HasOne(c => c.Mode)
            .WithMany()
            .HasForeignKey(c => c.ModeId)
            .OnDelete(DeleteBehavior.NoAction);

        // Seeded, not authored. LearnerResponse.EvidenceContractVersionId is non-nullable, so a
        // deployment without this row records no evidence at all — silently, and unrecoverably.
        // See EvidenceContractIds for why that makes it a schema dependency rather than config.
        builder.HasData(
            new EvidenceContract
            {
                Id = EvidenceContractIds.PlatformLessonAttempt,
                ContractKey = EvidenceContractKeys.PlatformLessonAttempt,
                GameId = null,
                ModeId = null,
                InteractionKind = InteractionKinds.ItemResponse,
                Description =
                    "A lesson attempt posted to POST /api/progress/attempts is an administration of "
                    + "assessment items. Applies to every game and mode unless a more specific "
                    + "contract exists.",
                CreatedAtUtc = SeededAtUtc
            },

            // The formal-sitting contract. A separate interaction kind rather than a second
            // contract on the same one, because the unique index above would otherwise make the
            // winner arbitrary — and because a sitting genuinely is a different interaction: its
            // conditions were fixed by the server before the paper was served, which is the basis
            // the stronger claim rests on.
            new EvidenceContract
            {
                Id = EvidenceContractIds.PlatformAssessmentAdministration,
                ContractKey = EvidenceContractKeys.PlatformAssessmentAdministration,
                GameId = null,
                ModeId = null,
                InteractionKind = InteractionKinds.AssessmentResponse,
                Description =
                    "An item served inside an AssessmentAdministration, under conditions the "
                    + "server fixed when the sitting was opened and enforced for its duration.",
                CreatedAtUtc = SeededAtUtc
            });
    }

    /// <summary>
    /// Fixed, so re-running the migration does not rewrite the row with a new timestamp and show a
    /// pending model change on every build. Same reasoning as the seeded currencies.
    /// </summary>
    internal static readonly DateTime SeededAtUtc = new(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
}

public class EvidenceContractVersionConfiguration : IEntityTypeConfiguration<EvidenceContractVersion>
{
    public void Configure(EntityTypeBuilder<EvidenceContractVersion> builder)
    {
        builder.ToTable("EvidenceContractVersions");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Justification).HasMaxLength(1024);
        builder.Property(v => v.Weight).HasPrecision(6, 4);

        builder.HasIndex(v => new { v.ContractId, v.VersionNumber }).IsUnique();

        builder.HasOne(v => v.Contract)
            .WithMany(c => c.Versions)
            .HasForeignKey(v => v.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasData(
            new EvidenceContractVersion
            {
                Id = EvidenceContractIds.PlatformLessonAttemptV1,
                ContractId = EvidenceContractIds.PlatformLessonAttempt,
                VersionNumber = 1,

                // A first, unhinted, no-retry administration in a curriculum run is the strongest
                // thing this platform currently collects. Everything else is practice: a replay, a
                // hinted answer and a retryable one all inflate performance in ways that do not
                // generalise to an examination.
                StrengthWhenControlled = EvidenceStrength.Assessment,
                StrengthOtherwise = EvidenceStrength.Practice,
                Weight = 1.0m,

                RequiresFirstEncounter = true,
                RequiresUnhinted = true,
                RequiresNoRetry = true,

                // Every context, deliberately. Practice and free play produce weaker evidence, not
                // no evidence — a child grinding the questions they got wrong is the richest
                // diagnostic signal the product has, and it was previously discarded entirely.
                AdmittedContexts = AdmittedContextFlags.All,
                IsIndividuallyAttributable = true,

                Justification =
                    "The attempt endpoint accepts nothing but item responses, and grades them "
                    + "server-side against the stored answer key. Gameplay signals (distance, coins, "
                    + "combo, survival) arrive on the Run and telemetry paths, which have no contract "
                    + "and no route into this schema.",

                PublishedAtUtc = EvidenceContractConfiguration.SeededAtUtc
            },

            new EvidenceContractVersion
            {
                Id = EvidenceContractIds.PlatformAssessmentAdministrationV1,
                ContractId = EvidenceContractIds.PlatformAssessmentAdministration,
                VersionNumber = 1,

                // The same three conditions, and they are not decoration here: a sitting declares
                // them when it opens, the server enforces them for its whole duration, and no
                // caller can change them afterwards. That is what makes an exam-grade claim from a
                // sitting defensible where the same claim from a lesson would not be.
                StrengthWhenControlled = EvidenceStrength.Assessment,
                StrengthOtherwise = EvidenceStrength.Practice,
                Weight = 1.0m,

                RequiresFirstEncounter = true,
                RequiresUnhinted = true,
                RequiresNoRetry = true,

                AdmittedContexts = AdmittedContextFlags.All,
                IsIndividuallyAttributable = true,

                Justification =
                    "An AssessmentAdministration fixes its conditions — retries, aiding, delivery "
                    + "mode, time limit — before the first item is served, server-side, and refuses "
                    + "answers after its deadline. The responses it produces are therefore "
                    + "interpretable as an administration under stated conditions rather than as "
                    + "whatever a game happened to allow.",

                PublishedAtUtc = EvidenceContractConfiguration.SeededAtUtc
            });
    }
}

public class LearnerResponseConfiguration : IEntityTypeConfiguration<LearnerResponse>
{
    public void Configure(EntityTypeBuilder<LearnerResponse> builder)
    {
        builder.ToTable("LearnerResponses");
        builder.HasKey(r => r.Id);

        // Monotonic, database-assigned. Projections track a watermark over it rather than a
        // timestamp, because two rows can share a millisecond and a watermark must not skip one.
        builder.Property(r => r.Sequence).ValueGeneratedOnAdd().UseIdentityColumn();
        builder.HasIndex(r => r.Sequence).IsUnique();

        builder.Property(r => r.IdempotencyKey).HasMaxLength(128);

        // The measurement read: one learner's history on one item, in order. Also what derives
        // AttemptOrdinal on the way in — keyed by **item**, not by version or rendering, so a
        // child who answers in Arabic in September and English in March has one history.
        builder.HasIndex(r => new { r.LearnerId, r.ItemId, r.OccurredAtUtc });

        // The reporting read: one learner's evidence under one node.
        builder.HasIndex(r => new { r.LearnerId, r.NodeId });

        // Item statistics and the observation projection: every response against one item version.
        builder.HasIndex(r => r.ItemVersionId);
        builder.HasIndex(r => r.ItemId);

        // Restrict on all three: a response outlives the content it was given against, and the
        // whole point of this table is that history is not deletable through the curriculum.
        // Retirement is a timestamp; removal is not a path that exists.
        builder.HasOne(r => r.Item)
            .WithMany()
            .HasForeignKey(r => r.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ItemVersion)
            .WithMany()
            .HasForeignKey(r => r.ItemVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        // SetNull, and this one is worth spelling out. Deleting a lesson cascades into Questions,
        // and before item identity existed that cascade continued straight into this table —
        // **a content edit silently destroyed children's answers.** Now the meaning of a response
        // lives on Item and ItemVersion, which a lesson delete cannot reach, and only the pointer
        // to the rendering they saw is cleared. The evidence survives its own curriculum.
        builder.HasOne(r => r.ItemLocalization)
            .WithMany()
            .HasForeignKey(r => r.ItemLocalizationId)
            .OnDelete(DeleteBehavior.SetNull);

        // **The enforcement of the contract rule.** Required, so there is no code path that writes
        // evidence without naming a published contract. NoAction because a contract that has
        // admitted evidence must not be deletable — retire it instead.
        builder.HasOne(r => r.EvidenceContractVersion)
            .WithMany()
            .HasForeignKey(r => r.EvidenceContractVersionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.NoAction);

        // No FK to AspNetUsers, matching every other user-keyed table here; Identity's delete path
        // is explicit rather than cascading (see UserAdminService.DeleteUserAsync).
    }
}
