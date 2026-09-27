using Sethika.Application.Access.SignUp;

namespace Sethika.Application.Tests.Access;

public sealed class SignUpDonorCommandTests
{
    [Fact]
    public void NeverPrintsThePasswordOrTheTicket()
    {
        var command = new SignUpDonorCommand(
            "0771234567", "ticket-abc123", "Dana Donor", null, "Dana!2026Sethika", "si", [], "203.0.113.9", "xunit");

        var printed = command.ToString();

        Assert.DoesNotContain("Dana!2026Sethika", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("ticket-abc123", printed, StringComparison.Ordinal);
        Assert.Contains("0771234567", printed, StringComparison.Ordinal);
    }
}
