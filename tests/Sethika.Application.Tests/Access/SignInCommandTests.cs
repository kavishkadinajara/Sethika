using Sethika.Application.Access.SignIn;

namespace Sethika.Application.Tests.Access;

public sealed class SignInCommandTests
{
    [Fact]
    public void NeverPrintsThePassword()
    {
        // A record prints every property by default, so a logged command would carry the plaintext.
        var command = new SignInCommand("0771234567", "Dana!2026Sethika", "203.0.113.7", "xunit");

        var printed = command.ToString();

        Assert.DoesNotContain("Dana!2026Sethika", printed, StringComparison.Ordinal);
        Assert.Contains("0771234567", printed, StringComparison.Ordinal);
    }
}
