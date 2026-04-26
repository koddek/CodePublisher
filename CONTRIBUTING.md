# Contributing

Thanks for helping improve .NET App Publisher.

## Before you start

- Open an issue for bug reports, UX rough edges, or larger feature ideas before starting implementation.
- Keep changes focused. Small pull requests are much easier to review and safer to merge.
- Do not commit generated build output such as `bin/`, `obj/`, or packaged artifacts.

## Local setup

### Requirements

- .NET 10 SDK
- Android SDK command-line tools
- `adb` on `PATH` if you want to use Android install or launch actions
- Android `emulator` on `PATH` or installed in the default SDK location if you want AVD discovery and launch

### Run the app

```bash
dotnet build DotNetAppPublisher.slnx
dotnet run --project src/DotNetAppPublisher/DotNetAppPublisher.Desktop/DotNetAppPublisher.Desktop.csproj
```

## Pull requests

- Describe the user-facing change and why it is needed.
- Call out platform-specific impact if the change affects Android, macOS, Windows, or iOS differently.
- Include screenshots or command-output snippets when they make review easier.
- If you changed publish behavior, mention one manual verification path you exercised.

## Release notes

- Tag-based GitHub Actions builds are intended to produce desktop release archives.
- Keep release changes easy to audit: prefer one logical change per commit where practical.
