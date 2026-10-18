using Microsoft.EntityFrameworkCore;

namespace Sethika.Application.Abstractions;

/// <summary>
/// A save refused by a unique index or constraint, named, so a handler can answer for the one it
/// expects without knowing SQL Server's error numbers.
/// </summary>
/// <remarks>
/// Some uniqueness can only be checked by the database: <c>UX_RegReq_ActiveRegNo</c> spans every
/// applicant's applications, and Row-Level Security hides all but the caller's own, so no query the
/// caller makes can see the clash. The index sees it; this carries its name back. It derives from
/// <see cref="DbUpdateException"/>, so a handler that catches that still catches this.
/// </remarks>
public sealed class DuplicateKeyException : DbUpdateException
{
    public DuplicateKeyException()
    {
    }

    public DuplicateKeyException(string message)
        : base(message)
    {
    }

    public DuplicateKeyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DuplicateKeyException(string keyName, DbUpdateException refused)
        : base($"The save would duplicate a value of {keyName}.", refused, refused?.Entries ?? [])
    {
        KeyName = keyName;
    }

    /// <summary>The unique index or constraint, as named in the SQL project: <c>UX_RegReq_ActiveRegNo</c>.</summary>
    public string? KeyName { get; }
}
