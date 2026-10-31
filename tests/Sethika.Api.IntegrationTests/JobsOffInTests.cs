using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Sethika.Api.IntegrationTests;

/// <summary>Keeps every test host from starting a Hangfire server against the shared test database.</summary>
/// <remarks>
/// An environment variable, because each test class builds its own host and the host reads the
/// variable before any of them can be told. <c>HangfireJobsTests</c> turns jobs back on for its own
/// host with a setting, which outranks the variable.
/// </remarks>
internal static class JobsOffInTests
{
    [ModuleInitializer]
    [SuppressMessage(
        "Usage",
        "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "A test assembly: it is loaded only by the test runner, and it must set this before any host is built.")]
    internal static void TurnJobsOff() => Environment.SetEnvironmentVariable("Jobs__Enabled", "false");
}
