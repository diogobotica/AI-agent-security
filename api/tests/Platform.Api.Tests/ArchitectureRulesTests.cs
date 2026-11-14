using System.Text.Json;
using Platform.Core;

namespace Platform.Api.Tests;

/// <summary>
/// Architecture rules from CLAUDE.md. Rule 2: no Azure or Stripe SDK in Platform.Core; external
/// services are reached through interfaces implemented outside the core. Later stories extend this class.
/// </summary>
public sealed class ArchitectureRulesTests
{
    private static readonly string[] ForbiddenSdkPrefixes =
    [
        "Azure.",
        "Microsoft.Azure.",
        "WindowsAzure.",
        "Stripe",
    ];

    [Fact]
    public void PlatformCore_ReferencesNoAzureOrStripeSdk()
    {
        var core = typeof(CoreAssemblyMarker).Assembly;
        Assert.Equal("Platform.Core", core.GetName().Name);

        var compiledReferences = core.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty);
        var declaredDependencies = DependenciesReachableFrom("Platform.Core");

        var offenders = compiledReferences
            .Concat(declaredDependencies)
            .Where(IsAzureOrStripeSdk)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Empty(offenders);
    }

    private static bool IsAzureOrStripeSdk(string name) =>
        ForbiddenSdkPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Walks the test project's dependency manifest (deps.json) from the given project and returns every
    /// package or project it depends on, directly or transitively. This also catches package references
    /// that the compiled assembly does not use yet.
    /// </summary>
    private static HashSet<string> DependenciesReachableFrom(string projectName)
    {
        var testAssemblyName = typeof(ArchitectureRulesTests).Assembly.GetName().Name;
        var manifestPath = Path.Combine(AppContext.BaseDirectory, $"{testAssemblyName}.deps.json");
        Assert.True(File.Exists(manifestPath), $"Dependency manifest not found at {manifestPath}.");

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var libraries = manifest.RootElement.GetProperty("targets").EnumerateObject().First().Value;

        var dependenciesByLibrary = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in libraries.EnumerateObject())
        {
            var separator = library.Name.IndexOf('/');
            var name = separator < 0 ? library.Name : library.Name[..separator];
            var dependencies = new List<string>();
            if (library.Value.TryGetProperty("dependencies", out var declared))
            {
                dependencies.AddRange(declared.EnumerateObject().Select(dependency => dependency.Name));
            }

            dependenciesByLibrary[name] = dependencies;
        }

        // The test project must reference the project, so it is listed in the manifest.
        Assert.Contains(projectName, dependenciesByLibrary);

        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        pending.Enqueue(projectName);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!dependenciesByLibrary.TryGetValue(current, out var dependencies))
            {
                continue;
            }

            foreach (var dependency in dependencies)
            {
                if (reachable.Add(dependency))
                {
                    pending.Enqueue(dependency);
                }
            }
        }

        return reachable;
    }
}
