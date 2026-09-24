using System.Xml.Linq;

namespace Sethika.Tests;

/// <summary>
/// Reads &lt;ProjectReference&gt; entries straight out of the .csproj files so the
/// dependency rule from CLAUDE.md (Api -> Infrastructure -> Application -> Domain)
/// is enforced by the test run.
/// </summary>
/// <remarks>
/// Deliberately not reflection over the compiled assemblies. The C# compiler only
/// emits an assembly reference that the code actually uses, so while the layers are
/// still empty a reflection-based check passes even when a reference points the
/// wrong way - a green test that guards nothing. The project file is the thing the
/// rule actually lives in, so that is what gets read.
/// </remarks>
internal static class DependencyRule
{
    private static readonly string[] ProjectFolders = ["src", "tests"];

    /// <summary>Names of the projects that <paramref name="projectName"/> references, sorted.</summary>
    internal static string[] ProjectReferencesOf(string projectName)
    {
        var csproj = ProjectFolders
            .Select(folder => Path.Combine(SolutionRoot(), folder, projectName, projectName + ".csproj"))
            .FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"No project file found for '{projectName}'.");

        return [.. XDocument.Load(csproj)
            .Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar))
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(name => name, StringComparer.Ordinal)!];
    }

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sethika.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new DirectoryNotFoundException(
                $"Sethika.sln not found in any parent of '{AppContext.BaseDirectory}'.");
    }
}
