namespace Share7.Domain.Organizations;

/// <summary>
/// A district, a school, a department, a tutoring centre or a publisher.
///
/// <para><b>Recursive, and deliberately one table.</b> A district contains schools; a school
/// contains departments. Modelling each level separately would make "everything under this
/// organization" a different query at every depth, and the depth is exactly the thing that varies
/// between customers (<c>Docs/EducationalArchitecture.md</c> §9.1).</para>
///
/// <para><b>This is not a tenant.</b> Tenancy here is row-level scoping through
/// <see cref="Structure.Enrollment.OwnerOrgId"/>, on one database. Database-per-tenant costs every
/// feature a multiplier and buys nothing until a customer's procurement demands it; the
/// irreversible part — having the scoping column and the enrollment indirection — is what is being
/// built here, and physical isolation stays available later for the few who contract for it
/// (§9.4).</para>
/// </summary>
public class Organization
{
    public Guid Id { get; set; }

    /// <summary>
    /// Stable, human-readable, unique. What an integration quotes and what a support conversation
    /// names, so neither has to pass a GUID around.
    /// </summary>
    public string OrgKey { get; set; } = string.Empty;

    /// <summary>The organization above this one. Null for a root — a standalone school or district.</summary>
    public Guid? ParentOrgId { get; set; }
    public Organization? ParentOrg { get; set; }

    public OrganizationKind Kind { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// ISO 3166-1 alpha-2. Not decoration: retention and consent rules are national, and a report
    /// crossing a border is a different legal object from one that does not.
    /// </summary>
    public string CountryCode { get; set; } = string.Empty;

    public OrganizationStatus Status { get; set; }

    /// <summary>
    /// The organization's own item bank, provisioned on creation. Its trust ceiling is what stops a
    /// school's unreviewed material from moving a national examination projection (§10.2).
    /// </summary>
    public Guid? ItemBankId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<Organization> Children { get; set; } = new List<Organization>();
    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
    public ICollection<Cohort> Cohorts { get; set; } = new List<Cohort>();
}

/// <summary>
/// A person's standing inside one organization.
///
/// <para><b>This is the row the authorization rule joins through.</b> §9.3 is a single sentence — a
/// viewer may see a learner's educational data only through an explicit stored relationship, and
/// the relationship determines the scope — and this is one of the three tables that sentence names.
/// There is no policy engine, no ReBAC and no DSL: there is this join and a scope filter, enforced
/// in one place.</para>
///
/// <para><b>Revoked, never deleted.</b> A report about last term has to be able to say who taught
/// it, and an audit of who saw a child's data has to resolve against the membership that permitted
/// it at the time.</para>
/// </summary>
public class Membership
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid OrgId { get; set; }
    public Organization? Org { get; set; }

    public OrgRole Role { get; set; }

    public MembershipStatus Status { get; set; }

    /// <summary>Who granted it. Null only for memberships the platform itself created.</summary>
    public Guid? GrantedByUserId { get; set; }

    public DateTime GrantedAtUtc { get; set; }

    /// <summary>
    /// When it stopped being in force. Set rather than deleting the row, so the history of who
    /// could see what stays reconstructible.
    /// </summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>In force right now. The condition every access check applies.</summary>
    public bool IsActive => Status == MembershipStatus.Active && RevokedAtUtc is null;
}

/// <summary>
/// A guardian's verified relationship to a learner, and what it permits.
///
/// <para><b>Consent is a row with a scope and a revocation date, not a boolean set at signup</b>
/// (§9.5, §18.3). A signup boolean cannot answer "did they agree to <i>this</i>", and on a
/// child-audience platform that question is asked by every new feature.</para>
///
/// <para><b>Revocation is a timestamp, not a deletion.</b> The relationship's history has to stay
/// auditable — including the period during which data was legitimately shared.</para>
/// </summary>
public class GuardianLink
{
    public Guid Id { get; set; }

    public Guid GuardianUserId { get; set; }

    public Guid LearnerUserId { get; set; }

    public GuardianRelationship Relationship { get; set; }

    /// <summary>
    /// When the relationship was actually established rather than merely claimed. <b>Null means
    /// unverified, and an unverified link grants nothing</b> — anyone can type a child's email
    /// address.
    /// </summary>
    public DateTime? VerifiedAtUtc { get; set; }

    /// <summary>Who verified it: an org admin, a platform admin, or the learner themselves.</summary>
    public Guid? VerifiedByUserId { get; set; }

    public GuardianConsentScope ConsentScope { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Verified, unrevoked, and therefore usable. Checked before every scope test.</summary>
    public bool IsEffective => VerifiedAtUtc is not null && RevokedAtUtc is null;

    /// <summary>Whether this link carries a particular permission right now.</summary>
    public bool Permits(GuardianConsentScope scope) =>
        IsEffective && (ConsentScope & scope) == scope;
}
