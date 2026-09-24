using Sethika.Tests;

namespace Sethika.Application.Tests;

/// <summary>
/// Guards the dependency rule from CLAUDE.md: Api -> Infrastructure -> Application -> Domain.
/// </summary>
public class DependencyRuleTests
{
    [Fact]
    public void Application_references_only_Domain()
    {
        var references = DependencyRule.ProjectReferencesOf("Sethika.Application");

        Assert.Equal("Sethika.Domain", Assert.Single(references));
    }
}
