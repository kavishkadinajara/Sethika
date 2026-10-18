namespace Sethika.Application.Abstractions;

/// <summary>A level the actor may create and assign roles at, and what such a role may be given.</summary>
/// <param name="Level">The level's code.</param>
/// <param name="Name">The level's name.</param>
/// <param name="Rank">1 is the most senior.</param>
/// <param name="Permissions">The level's ceiling intersected with the actor's own effective permissions.</param>
public sealed record GrantableLevel(string Level, string Name, byte Rank, IReadOnlyList<string> Permissions);

public interface IRoleLevelQueries
{
    /// <summary>
    /// The institution levels strictly more junior than the actor's most senior rank in the
    /// institution, each with exactly the permissions the actor may grant at it (plan 5.9).
    /// </summary>
    Task<IReadOnlyList<GrantableLevel>> GetGrantableAsync(
        Guid userId,
        int institutionId,
        CancellationToken cancellationToken = default);
}
