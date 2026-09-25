namespace DotNetAppPublisher.Features.Deployment.Android;

public sealed record AndroidDeviceInfo(string Serial, string Name, string Type, bool IsRunning)
{
    public string DisplayName => IsRunning
        ? $"{Name} ({Serial})"
        : $"{Name} (Offline)";
}
