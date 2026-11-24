using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Sethika.Api.IntegrationTests;

/// <summary>Keeps every test host off the geo index a running API keeps (plan 06, G4).</summary>
/// <remarks>
/// The tests and the developer's API share Redis database 3, and the index is one key: a test host
/// that rebuilt <c>geo:institutions</c> from <c>Sethika_Test</c> would hand the API the test's
/// institutions. An environment variable, for the reason <see cref="JobsOffInTests"/> gives; the
/// search tests give each host a key of its own with a setting, which outranks it.
/// </remarks>
internal static class GeoIndexKeyInTests
{
    [ModuleInitializer]
    [SuppressMessage(
        "Usage",
        "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "A test assembly: it is loaded only by the test runner, and it must set this before any host is built.")]
    internal static void UseTestKey() => Environment.SetEnvironmentVariable("GeoIndex__Key", "geo:institutions:test");
}
