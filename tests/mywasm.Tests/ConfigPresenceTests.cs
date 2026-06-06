using System;
using Xunit;

namespace Mywasm.Tests;

// These tests run on the CI runner (server-side), where GitHub Actions has
// injected the configuration values as environment variables for the step.
// They assert each value is present and non-empty, so the pipeline fails fast
// if a repo Variable or Secret is missing or misnamed — before build/deploy.
//
// Note: this project does NOT reference the Blazor app. It only inspects the
// process environment, so it builds and runs standalone.
public class ConfigPresenceTests
{
    [Theory]
    [InlineData("DEMO_VAR")]     // non-secret -> GitHub "Variables" tab -> ${{ vars.DEMO_VAR }}
    [InlineData("DEMO_SECRET")]  // secret     -> GitHub "Secrets" tab   -> ${{ secrets.DEMO_SECRET }}
    public void EnvironmentVariable_IsPresentAndNonEmpty(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);

        Assert.False(
            string.IsNullOrWhiteSpace(value),
            $"Environment variable '{name}' was not set or was empty. " +
            "Check it exists under Settings -> Secrets and variables -> Actions " +
            "(at REPO level, not the github-pages environment), and that the " +
            "workflow step maps it via env:.");
    }
}
