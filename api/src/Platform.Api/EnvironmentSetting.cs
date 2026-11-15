namespace Platform.Api;

/// <summary>Where the API runs. Chooses the implementations of external services (CLAUDE.md rule 2).</summary>
public enum DeploymentEnvironment
{
    /// <summary>The local Docker Compose stack with emulators (US-102).</summary>
    Local,

    /// <summary>Azure. The real service implementations arrive with US-103.</summary>
    Azure,
}

/// <summary>
/// Reads the <c>ENVIRONMENT</c> process variable. Only "local" and "azure" are accepted; anything else, or no value,
/// stops startup.
/// </summary>
/// <remarks>
/// This is the process variable on purpose, not <c>IConfiguration["ENVIRONMENT"]</c>: in ASP.NET Core the
/// configuration key "environment" (keys ignore case) is the framework's own host environment name.
/// </remarks>
public static class EnvironmentSetting
{
    /// <summary>Name of the process environment variable.</summary>
    public const string VariableName = "ENVIRONMENT";

    /// <summary>Reads and validates the ENVIRONMENT process variable.</summary>
    /// <exception cref="InvalidOperationException">The variable is missing or not "local" or "azure".</exception>
    public static DeploymentEnvironment Read() => Parse(Environment.GetEnvironmentVariable(VariableName));

    /// <summary>Validates an ENVIRONMENT value (exact, lower case).</summary>
    /// <exception cref="InvalidOperationException">The value is missing or not "local" or "azure".</exception>
    public static DeploymentEnvironment Parse(string? value) => value switch
    {
        "local" => DeploymentEnvironment.Local,
        "azure" => DeploymentEnvironment.Azure,
        _ => throw new InvalidOperationException(
            $"The {VariableName} environment variable must be set to \"local\" or \"azure\"."),
    };
}
