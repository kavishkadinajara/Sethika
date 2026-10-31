namespace Sethika.Application.Access;

/// <summary>The languages Sethika speaks: the values <c>CK_Users_PreferredLanguage</c> allows.</summary>
public static class SupportedLanguages
{
    /// <summary>Used when a request names none.</summary>
    public const string Default = "en";

    private static readonly string[] All = ["en", "si", "ta"];

    /// <summary>
    /// The canonical code for <paramref name="requested"/>, <see cref="Default"/> when it is blank,
    /// or null when it is not a language Sethika supports.
    /// </summary>
    public static string? Resolve(string? requested) =>
        string.IsNullOrWhiteSpace(requested)
            ? Default
            : All.FirstOrDefault(l => string.Equals(l, requested.Trim(), StringComparison.OrdinalIgnoreCase));
}
