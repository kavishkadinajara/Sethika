using Microsoft.Extensions.DependencyInjection;
using Sethika.Application.Abstractions;

namespace Sethika.Infrastructure.Jobs;

/// <summary>Who background work runs as: the platform, with no user and no institution.</summary>
/// <remarks>
/// CLAUDE.md: background jobs use <c>IsPlatform = 1</c>. Nobody asked for the work, so there is no
/// user to act as, and a reminder job has to read every institution's bookings.
/// </remarks>
public sealed class PlatformJobUser : ICurrentUserContext
{
    public static PlatformJobUser Instance { get; } = new();

    private PlatformJobUser()
    {
    }

    public Guid? UserId => null;

    public int? InstitutionId => null;

    public bool IsPlatform => true;

    public string? TokenId => null;
}

/// <summary>Whether a DI scope is a background job's, which decides who <see cref="ICurrentUserContext"/> is in it.</summary>
/// <remarks>Set only by <see cref="JobScopes.CreatePlatformScope"/>; a request's scope never has it set.</remarks>
public sealed class JobScope
{
    internal bool IsJob { get; set; }
}

/// <summary>Creates the scopes background jobs run in.</summary>
public static class JobScopes
{
    /// <summary>
    /// A new scope in which <see cref="ICurrentUserContext"/> is <see cref="PlatformJobUser"/>, so every
    /// connection it opens is stamped <c>IsPlatform = 1</c> and Row-Level Security lets it see all tenants.
    /// </summary>
    /// <remarks>
    /// For jobs, and for two narrow request-side calls: <c>PlatformOwnStandingDays</c> (plan 08, K35)
    /// runs a single skip of the caller's own standing day in a scope of its own, and
    /// <c>PlatformPledges</c> (plan 09, M14) a donor's own pledge or its cancel. Called from a request
    /// otherwise, it would hand that request the platform's reach, so nothing under <c>Sethika.Api</c>
    /// calls it. The scope is new, so nothing in it can have asked who the user is before it was marked.
    /// </remarks>
    public static AsyncServiceScope CreatePlatformScope(this IServiceScopeFactory scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<JobScope>().IsJob = true;
        return scope;
    }
}
