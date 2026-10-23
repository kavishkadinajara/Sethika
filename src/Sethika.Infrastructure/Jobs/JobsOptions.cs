namespace Sethika.Infrastructure.Jobs;

/// <summary>Whether this process runs background jobs, and how many at once. Section <c>Jobs</c>.</summary>
public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    /// <summary>
    /// On by default, so a deployment that forgets the setting still sends its texts. The
    /// integration tests turn it off, or every test host would start a job server against the
    /// shared test database.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Hangfire workers in this process. The outbox needs one; reminders will want more.</summary>
    public int WorkerCount { get; set; } = 2;
}
