namespace Sethika.Infrastructure.Files;

/// <summary>Where uploaded files are kept. Bound from the <c>Files</c> section.</summary>
public sealed class FileStorageOptions
{
    /// <summary>Configuration section these options bind to.</summary>
    public const string SectionName = "Files";

    /// <summary>
    /// The absolute directory <see cref="LocalFileStorage"/> writes under. Outside the web root and
    /// the content root, so no static-file middleware can ever serve it.
    /// </summary>
    /// <remarks>
    /// Optional in Development, where it defaults to <c>%LOCALAPPDATA%\Sethika\files</c>; required
    /// anywhere else, and the host refuses to start without it.
    /// </remarks>
    public string? LocalRoot { get; set; }
}
