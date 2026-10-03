namespace Share7.Domain.BrainPass;

public enum BrainPassState { Draft = 0, Published = 1, Disabled = 2 }
public enum BrainPassTrack { Free = 0, Premium = 1 }

public class BrainPassSeason
{
    public Guid Id { get; set; }
    public string Key { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string NameAr { get; set; } = "";
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public DateTime ClaimUntilUtc { get; set; }
    public BrainPassState State { get; set; }
    public Guid? PremiumProductId { get; set; }
    public string? ObjectiveGroupKey { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int Version { get; set; }
}

public class BrainPassTier
{
    public Guid SeasonId { get; set; }
    public int Number { get; set; }
    public BrainPassTrack Track { get; set; }
    public long RequiredXp { get; set; }
    public Guid RewardRuleId { get; set; }
}

/// <summary>Only real result metrics are allowed. Caps are immutable after publishing.</summary>
public class BrainPassXpRule
{
    public Guid SeasonId { get; set; }
    public string Metric { get; set; } = "";
    public long MinimumValue { get; set; }
    public long UnitValue { get; set; } = 1;
    public int XpPerUnit { get; set; }
    public int MaxSourceXp { get; set; }
    public int DailyCap { get; set; }
}

public class BrainPassProgress
{
    public Guid SeasonId { get; set; }
    public Guid UserId { get; set; }
    public long Xp { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Result identities, not a watermark: a late lower-sequence commit must still count.</summary>
public class BrainPassCredit
{
    public Guid SeasonId { get; set; }
    public Guid ResultId { get; set; }
    public Guid UserId { get; set; }
    public int Xp { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class BrainPassSource
{
    public Guid SeasonId { get; set; }
    public Guid UserId { get; set; }
    public string Metric { get; set; } = "";
    public Guid SourceId { get; set; }
    public long MaximumValue { get; set; }
    public int CreditedXp { get; set; }
}

public class BrainPassDaily
{
    public Guid SeasonId { get; set; }
    public Guid UserId { get; set; }
    public string Metric { get; set; } = "";
    public DateTime DayUtc { get; set; }
    public int Xp { get; set; }
}

public class BrainPassClaim
{
    public Guid SeasonId { get; set; }
    public Guid UserId { get; set; }
    public int Tier { get; set; }
    public BrainPassTrack Track { get; set; }
    public string RewardsJson { get; set; } = "[]";
    public DateTime ClaimedAtUtc { get; set; }
}
