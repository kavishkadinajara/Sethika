using Sethika.Tests;

namespace Sethika.Api.IntegrationTests;

/// <summary>
/// Guards the dependency rule from CLAUDE.md: Api -> Infrastructure -> Application -> Domain.
/// </summary>
public class DependencyRuleTests
{
    [Fact]
    public void Infrastructure_references_only_Application()
    {
        var references = DependencyRule.ProjectReferencesOf("Sethika.Infrastructure");

        Assert.Equal("Sethika.Application", Assert.Single(references));
    }

    [Fact]
    public void Api_references_only_Infrastructure()
    {
        var references = DependencyRule.ProjectReferencesOf("Sethika.Api");

        Assert.Equal("Sethika.Infrastructure", Assert.Single(references));
    }
}
