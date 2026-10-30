using System.Text.Json;

namespace CodePublisher.Features.NuGet.Feeds;

/// <summary>
/// Parsers for the NuGet V3 protocol payloads: the service index (which names the flat-container
/// resource) and the flat-container version list. Pure JSON handling, no I/O, so tests feed strings.
/// </summary>
public static class NuGetVersionListParser
{
    public const string PackageBaseAddressResourceType = "PackageBaseAddress";
    public const string RegistrationsBaseAddressResourceType = "RegistrationsBaseUrl";

    /// <summary>
    /// Reads a V3 service index and returns the PackageBaseAddress URL (the flat container base),
    /// or null when the document does not declare it.
    /// </summary>
    public static string? GetPackageBaseAddress(string serviceIndexJson)
    {
        using var document = Parse(serviceIndexJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("resources", out var resources)
            || resources.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var resource in resources.EnumerateArray())
        {
            if (resource.ValueKind == JsonValueKind.Object
                && resource.TryGetProperty("@type", out var type)
                && type.ValueKind == JsonValueKind.String
                && string.Equals(type.GetString(), PackageBaseAddressResourceType, StringComparison.Ordinal)
                && resource.TryGetProperty("@id", out var id)
                && id.ValueKind == JsonValueKind.String)
            {
                return id.GetString();
            }
        }

        return null;
    }

    /// <summary>The flat-container index URL that lists every version of one package.</summary>
    public static string BuildFlatContainerIndexUrl(string packageBaseAddress, string packageId)
    {
        var baseUrl = packageBaseAddress.TrimEnd('/');
        return $"{baseUrl}/{NuGetFeedCatalog.NormalizeIdForUrl(packageId)}/index.json";
    }

    /// <summary>Reads a flat-container index (`{"versions": ["1.0.0", ...]}`) into version strings.</summary>
    public static IReadOnlyList<string> ParseFlatContainerVersions(string flatContainerJson)
    {
        using var document = Parse(flatContainerJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("versions", out var versions)
            || versions.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var version in versions.EnumerateArray())
        {
            if (version.ValueKind == JsonValueKind.String && version.GetString() is { Length: > 0 } value)
            {
                result.Add(value);
            }
        }

        return result;
    }

    private static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"The feed returned a response that is not valid JSON: {exception.Message}");
        }
    }
}
