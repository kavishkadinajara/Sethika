using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Sethika.Api.IntegrationTests;

/// <summary>Gives every test host production's cap on codes sent, three an hour.</summary>
/// <remarks>
/// A test host runs as Development, so it reads <c>appsettings.Development.json</c>, whose cap of 10 is
/// there to save a developer waiting (plan 09, M28). The tests prove the cap a real number meets. An
/// environment variable, for the reason <see cref="JobsOffInTests"/> gives.
/// </remarks>
internal static class OtpCapInTests
{
    [ModuleInitializer]
    [SuppressMessage(
        "Usage",
        "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "A test assembly: it is loaded only by the test runner, and it must set this before any host is built.")]
    internal static void UseProductionCap() =>
        Environment.SetEnvironmentVariable("Otp__MaxSendsPerWindow", "3");
}
