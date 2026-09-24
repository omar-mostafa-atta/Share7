using Share7.Domain.Guidance;

namespace Share7.Application.Guidance.Models;

public sealed class GuidanceStateResponseDto
{
    public int Generation { get; set; } = 1;
    public GuidanceStateSnapshot Snapshot { get; set; } = new();
}

public sealed class PushGuidanceStateRequest
{
    public GuidanceStateSnapshot Snapshot { get; set; } = new();
}
