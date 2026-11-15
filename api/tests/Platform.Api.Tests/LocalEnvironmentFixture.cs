[assembly: AssemblyFixture(typeof(Platform.Api.Tests.LocalEnvironmentFixture))]

namespace Platform.Api.Tests;

/// <summary>
/// US-102: the API reads the ENVIRONMENT process variable at startup (local or azure). The test process runs as
/// ENVIRONMENT=local. Tests that change the variable belong to <see cref="ProcessEnvironmentCollection"/>, which
/// runs alone and restores it.
///
/// ENVIRONMENT is read from the process environment, not through IConfiguration: there the key "environment"
/// (keys ignore case) is ASP.NET Core's own host environment name (Development, Production…).
/// </summary>
public sealed class LocalEnvironmentFixture
{
    public const string Variable = "ENVIRONMENT";
    public const string Local = "local";

    public LocalEnvironmentFixture() => Environment.SetEnvironmentVariable(Variable, Local);
}

/// <summary>Tests that change process environment variables; xUnit runs this collection after the others, alone.</summary>
[CollectionDefinition(nameof(ProcessEnvironmentCollection), DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
}
