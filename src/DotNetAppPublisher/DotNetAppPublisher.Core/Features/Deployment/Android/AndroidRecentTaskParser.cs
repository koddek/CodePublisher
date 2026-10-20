using System.Text.RegularExpressions;

namespace DotNetAppPublisher.Features.Deployment.Android;

/// <summary>
/// Parses `dumpsys activity recents` into tasks that can be dismissed with `am stack remove`.
/// Pure and side-effect free, so every output shape (standard app task, home, work profile, tasks
/// without a component) is covered by unit tests instead of a real device.
/// </summary>
public static class AndroidRecentTaskParser
{
    // "  * Recent #0: Task{6168258 #100 type=standard A=10257:com.appeey.soable}"
    // Older releases print "TaskRecord{5d8c25c #1 A=10257:com.appeey.soable U=0 sz=1}" instead.
    private static readonly Regex HeaderPattern = new(
        @"^\s*\*+\s*Recent\s+#(?<index>\d+):\s*Task(?:Record)?\{(?<body>.*)\}\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HeaderTaskIdPattern = new(
        @"#(?<taskId>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TypePattern = new(
        @"\btype=(?<type>[A-Za-z][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // The header carries the owning package as "A=<uid>:<package>" for standard tasks.
    private static readonly Regex AffinityUidPattern = new(
        @"\bA=(?<uid>\d+):(?<package>[A-Za-z][A-Za-z0-9_.]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IdsPattern = new(
        @"\btaskId=(?<taskId>\d+)(?:\s+rootTaskId=(?<rootTaskId>\d+))?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ActivityComponentPattern = new(
        @"\bmActivityComponent=(?<package>[A-Za-z][A-Za-z0-9_.]*)/(?<activity>[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // intent={... cmp=pkg/activity} / baseIntent=Intent { ... cmp=pkg/activity }
    private static readonly Regex IntentComponentPattern = new(
        @"\bcmp=(?<package>[A-Za-z][A-Za-z0-9_.]*)/(?<activity>[^\s}]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RealActivityPattern = new(
        @"\brealActivity=(?<package>[A-Za-z][A-Za-z0-9_.]*)/(?<activity>[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AffinityPackagePattern = new(
        @"\baffinity=(?:\d+:)?(?<package>[A-Za-z][A-Za-z0-9_.]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<AndroidRecentTask> Parse(string? dumpsysOutput)
    {
        var tasks = new List<AndroidRecentTask>();
        if (string.IsNullOrWhiteSpace(dumpsysOutput))
        {
            return tasks;
        }

        string? blockBody = null;
        var lines = dumpsysOutput.Split(['\r', '\n']);
        foreach (var line in lines)
        {
            var header = HeaderPattern.Match(line);
            if (header.Success)
            {
                AppendTask(tasks, blockBody);
                blockBody = header.Groups["body"].Value;
                continue;
            }

            // Later indented lines of the same entry carry the ids.
            if (blockBody is not null && !string.IsNullOrWhiteSpace(line))
            {
                blockBody += Environment.NewLine + line;
            }
        }

        AppendTask(tasks, blockBody);
        return tasks;
    }

    /// <summary>Tasks in Recents that the given package owns and that can be dismissed.</summary>
    public static IReadOnlyList<AndroidRecentTask> FindDismissable(
        IReadOnlyList<AndroidRecentTask> tasks,
        string? packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            return [];
        }

        return tasks
            .Where(task => task.IsDismissable && task.BelongsTo(packageName))
            .GroupBy(task => task.RootTaskId)
            .Select(group => group.First())
            .ToArray();
    }

    private static void AppendTask(List<AndroidRecentTask> tasks, string? block)
    {
        if (string.IsNullOrWhiteSpace(block))
        {
            return;
        }

        var typeMatch = TypePattern.Match(block);
        var type = typeMatch.Success ? typeMatch.Groups["type"].Value : string.Empty;

        var idsMatch = IdsPattern.Match(block);
        var taskId = idsMatch.Success
            ? int.Parse(idsMatch.Groups["taskId"].Value)
            : ParseHeaderTaskId(block);
        var rootTaskId = idsMatch.Success && idsMatch.Groups["rootTaskId"].Success
            ? int.Parse(idsMatch.Groups["rootTaskId"].Value)
            : taskId;

        var componentMatch = ActivityComponentPattern.Match(block);
        if (!componentMatch.Success)
        {
            componentMatch = IntentComponentPattern.Match(block);
        }

        if (!componentMatch.Success)
        {
            componentMatch = RealActivityPattern.Match(block);
        }

        var packageName = componentMatch.Success
            ? componentMatch.Groups["package"].Value
            : MatchPackage(block, AffinityUidPattern) ?? MatchPackage(block, AffinityPackagePattern) ?? string.Empty;
        var component = componentMatch.Success
            ? $"{componentMatch.Groups["package"].Value}/{componentMatch.Groups["activity"].Value}"
            : packageName;

        tasks.Add(new AndroidRecentTask(taskId, rootTaskId, type, packageName, component));
    }

    private static int ParseHeaderTaskId(string block)
    {
        var headerEnd = block.IndexOf("type=", StringComparison.Ordinal);
        var header = headerEnd > 0 ? block[..headerEnd] : block;
        var match = HeaderTaskIdPattern.Match(header);
        return match.Success ? int.Parse(match.Groups["taskId"].Value) : 0;
    }

    private static string? MatchPackage(string block, Regex pattern)
    {
        var match = pattern.Match(block);
        return match.Success ? match.Groups["package"].Value : null;
    }
}
