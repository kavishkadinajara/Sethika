using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Sethika.Api.IntegrationTests;

/// <summary>Gives every test host the key that seals merchant secrets (plan 09, M21).</summary>
/// <remarks>
/// A host without a key does not start, and every test class builds its own host. An environment
/// variable, for the reason <see cref="JobsOffInTests"/> gives. The key seals nothing real: it opens only
/// what the tests sealed in <c>Sethika_Test</c>.
/// </remarks>
internal static class PaymentKeysInTests
{
    [ModuleInitializer]
    [SuppressMessage(
        "Usage",
        "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "A test assembly: it is loaded only by the test runner, and it must set this before any host is built.")]
    internal static void UseTestKey()
    {
        Environment.SetEnvironmentVariable("Payments__CurrentKeyId", "test1");
        Environment.SetEnvironmentVariable("Payments__Keys__test1", Convert.ToBase64String(new byte[32]));
    }
}
