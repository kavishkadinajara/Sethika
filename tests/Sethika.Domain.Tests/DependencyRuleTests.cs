using Sethika.Tests;

namespace Sethika.Domain.Tests;

/// <summary>
/// Guards the dependency rule from CLAUDE.md: Api -> Infrastructure -> Application -> Domain.
/// </summary>
public class DependencyRuleTests
{
    [Fact]
    public void Domain_references_nothing()
    {
        Assert.Empty(DependencyRule.ProjectReferencesOf("Sethika.Domain"));
    }
}
