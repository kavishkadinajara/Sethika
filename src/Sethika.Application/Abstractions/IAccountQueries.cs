namespace Sethika.Application.Abstractions;

/// <summary>What the signed-in user's own account shows them.</summary>
/// <param name="FullName">As given at signup.</param>
/// <param name="DisplayName">The shorter name, if they chose one.</param>
/// <param name="PreferredLanguage"><c>en</c>, <c>si</c> or <c>ta</c>: the language the web app opens in.</param>
public sealed record AccountProfile(string FullName, string? DisplayName, string PreferredLanguage);

/// <summary>Reads the caller's own account.</summary>
public interface IAccountQueries
{
    /// <summary>The account, or null when it does not exist or has been deleted.</summary>
    Task<AccountProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);
}
