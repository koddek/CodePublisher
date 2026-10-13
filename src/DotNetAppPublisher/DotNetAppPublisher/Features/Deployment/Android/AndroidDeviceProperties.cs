using System.Text.RegularExpressions;

namespace DotNetAppPublisher.Features.Deployment.Android;

/// <summary>Device identity read from a single `adb shell getprop` call.</summary>
public sealed record AndroidDeviceProperties(
    string? Model,
    string? AndroidVersion,
    string? ApiLevel,
    bool IsEmulator)
{
    private static readonly Regex PropertyLine = new(
        @"^\[(?<key>[^\]]*)\]:\s*\[(?<value>.*)\]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static AndroidDeviceProperties Parse(string? getpropOutput)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(getpropOutput))
        {
            foreach (var rawLine in getpropOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var match = PropertyLine.Match(rawLine);
                if (match.Success)
                {
                    values[match.Groups["key"].Value] = match.Groups["value"].Value;
                }
            }
        }

        // emulator signals come from more than one property and OEM builds vary, so check both.
        var isEmulator = IsSet(values, "ro.kernel.qemu") || IsSet(values, "ro.boot.qemu");

        return new AndroidDeviceProperties(
            FirstNonEmpty(values, "ro.product.model", "ro.product.vendor.model", "ro.product.name"),
            FirstNonEmpty(values, "ro.build.version.release", "ro.system.build.version.release"),
            FirstNonEmpty(values, "ro.build.version.sdk"),
            isEmulator);
    }

    private static bool IsSet(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && string.Equals(value.Trim(), "1", StringComparison.Ordinal);

    private static string? FirstNonEmpty(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
