using System.Text.RegularExpressions;

namespace CodePublisher.Features.Publishing.Inspect;

/// <summary>
/// Fallback-name lookup for an MSBuild evaluation result. The dictionary's own indexer throws for a
/// missing name, so this adds an indexing form that takes several candidate names and returns the
/// first one that has a value. A null evaluation result reads as "no value" instead of throwing.
/// </summary>
public static class EvaluatedPropertyLookup
{
    extension(IReadOnlyDictionary<string, string>? values)
    {
        public string? this[params string[] names]
        {
            get
            {
                if (values is null)
                {
                    return null;
                }

                foreach (var name in names)
                {
                    if (values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }

                return null;
            }
        }
    }
}

/// <summary>
/// Shared identifier mechanics: reading a property the build evaluated, and turning a package
/// name into a bundle identifier. Each platform resolves its own identifier (Android from the
/// manifest, iOS from the Info.plist, and so on) in its own library; these helpers are the
/// pieces they all reuse, so they live in the kernel.
/// </summary>
public static class ProjectIdentifier
{
    /// <summary>The first named property present in the MSBuild evaluation result, trimmed.</summary>
    public static string? FromEvaluated(IReadOnlyDictionary<string, string>? evaluated, params string[] names)
        => evaluated[names];

    public static string BuildBundleIdentifier(string packageId, string projectName)
    {
        if (!string.IsNullOrWhiteSpace(packageId))
        {
            var normalizedPackageId = packageId.Trim().Replace('_', '.');
            if (normalizedPackageId.Contains('.', StringComparison.Ordinal))
            {
                return normalizedPackageId;
            }
        }

        var normalizedName = Regex.Replace(projectName.ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            normalizedName = "codepublisher";
        }

        return $"com.{normalizedName}";
    }
}
