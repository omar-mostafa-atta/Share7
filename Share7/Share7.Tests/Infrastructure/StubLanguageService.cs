using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;

namespace Share7.Tests.Infrastructure;

/// <summary>
/// Answers "what language is this caller reading in" without an HTTP context.
/// <para>
/// The real implementation reads a JWT claim, which a service-level test has no way to supply —
/// and standing up the whole auth stack to assert that an Arabic caller gets the Arabic name would
/// test the wrong thing.
/// </para>
/// </summary>
public class StubLanguageService : ILanguageService
{
    private readonly Guid _langId;

    public StubLanguageService(Guid langId) => _langId = langId;

    public Task<IReadOnlyList<LanguageDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LanguageDto>>([]);

    public Task<Guid> ResolveCurrentAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_langId);

    public Task<Guid> ResolveForUserAsync(Guid? userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_langId);

    public Task<bool> SetPreferredLanguageAsync(Guid userId, Guid langId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
