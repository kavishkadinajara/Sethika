namespace Sethika.Application.Abstractions;

/// <summary>How an attempt on a lock ended: a slot-day's (<see cref="ISlotLocks"/>) or leaders' (<see cref="ILeaderLocks"/>).</summary>
public enum LockOutcome
{
    /// <summary>The caller holds the lock until it disposes the <see cref="ILockLease"/>.</summary>
    Acquired,

    /// <summary>Somebody else held it for the whole wait. The caller answers 409 and the person tries again.</summary>
    Busy,

    /// <summary>
    /// The lock store could not be reached. The caller goes on without the lock (plan 03, C2; Phase E,
    /// V3): the database is correct on its own, and an outage must not stop the work.
    /// </summary>
    Unavailable,
}

/// <summary>One attempt on a lock. Disposing it releases the lock if it was taken.</summary>
public interface ILockLease : IAsyncDisposable
{
    LockOutcome Outcome { get; }
}
