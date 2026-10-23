using Sethika.Application.Abstractions;

namespace Sethika.Api.IntegrationTests.Persistence;

/// <summary>Stands in for the authenticated principal the Api would supply.</summary>
internal sealed class TestCurrentUserContext : ICurrentUserContext
{
    public Guid? UserId { get; init; }
    public int? InstitutionId { get; init; }
    public bool IsPlatform { get; init; }
    public string? TokenId { get; init; }

    /// <summary>Institution staff: sees one tenant.</summary>
    internal static TestCurrentUserContext ForInstitution(int institutionId) =>
        new() { UserId = Guid.NewGuid(), InstitutionId = institutionId };

    /// <summary>A signed-in person with no institution selected: sees only what they own.</summary>
    internal static TestCurrentUserContext ForUser(Guid userId) =>
        new() { UserId = userId };

    /// <summary>Platform staff or a background job: sees across tenants.</summary>
    internal static TestCurrentUserContext Platform() =>
        new() { IsPlatform = true };

    /// <summary>Anonymous: no tenant, no platform rights.</summary>
    internal static TestCurrentUserContext Anonymous() => new();
}
