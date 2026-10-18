using Sethika.Application.Access.Bootstrap;

namespace Sethika.Api.Bootstrap;

/// <summary>
/// <c>dotnet run --project src/Sethika.Api -- bootstrap</c>: creates the first platform
/// administrator from configuration, prints what happened, and exits (plan Q5).
/// </summary>
/// <remarks>
/// The details come from configuration like every other secret - User Secrets locally:
/// <code>
/// dotnet user-secrets set "Bootstrap:PhoneNumber" "+9477..." --project src/Sethika.Api
/// dotnet user-secrets set "Bootstrap:Password"    "..."      --project src/Sethika.Api
/// dotnet user-secrets set "Bootstrap:FullName"    "..."      --project src/Sethika.Api
/// </code>
/// Never command-line arguments: those land in shell history and in the process list.
/// </remarks>
public static class BootstrapCommand
{
    /// <summary>The first argument that selects this command instead of the web host.</summary>
    public const string Name = "bootstrap";

    /// <summary>Runs the bootstrap in its own scope.</summary>
    /// <returns>The process exit code: 0 when an administrator exists for the number afterwards.</returns>
    public static async Task<int> RunAsync(
        IServiceProvider services,
        IConfiguration configuration,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(output);

        await using var scope = services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<BootstrapPlatformAdminHandler>();

        var result = await handler.HandleAsync(
            new BootstrapPlatformAdminCommand(
                configuration["Bootstrap:PhoneNumber"],
                configuration["Bootstrap:Password"],
                configuration["Bootstrap:FullName"]),
            cancellationToken);

        await output.WriteLineAsync(Describe(result));
        return result.Succeeded ? 0 : 1;
    }

    private static string Describe(BootstrapPlatformAdminResult result) => result.Outcome switch
    {
        BootstrapOutcome.Created => "Created the platform administrator.",
        BootstrapOutcome.RoleAssigned => "The existing platform account is now the administrator.",
        BootstrapOutcome.AlreadyAdministrator => "Already the platform administrator. Nothing changed.",
        BootstrapOutcome.AnotherAdministratorExists =>
            "Refused: another active platform administrator exists. Make further administrators in the app.",
        BootstrapOutcome.NotAPlatformAccount =>
            "Refused: that number belongs to a donor or institution account, which is not converted.",
        BootstrapOutcome.AccountNotActive => "Refused: that number belongs to a suspended, deactivated or deleted account.",
        BootstrapOutcome.InvalidPhoneNumber => "Refused: Bootstrap:PhoneNumber is missing or not a valid phone number.",
        BootstrapOutcome.InvalidFullName => "Refused: Bootstrap:FullName is missing or longer than 150 characters.",
        BootstrapOutcome.PasswordRejected => $"Refused: Bootstrap:Password breaks the password policy ({result.PasswordFailures}).",
        _ => $"Refused: {result.Outcome}.",
    };
}
