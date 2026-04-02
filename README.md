# Maui App Publisher

![Maui App Publisher icon](docs/maui-app-publisher-icon.png)

Maui App Publisher is a desktop utility for .NET MAUI developers who want a faster, safer way to build Android publish commands, inspect exactly what will run, and handle a few common emulator tasks without living in the terminal.

It is built with Avalonia and currently focuses on Android publishing workflows for MAUI projects.

## What it does

- Select a MAUI project folder and auto-detect the `.csproj`
- Read Android app metadata such as package id and target framework
- Generate APK, AAB, or both
- Toggle common Android publish settings including trimming, AOT, profiled AOT, linker mode, shrinker, dex compiler, and cleanup behavior
- Preview the exact `dotnet publish` command before running it
- Stream live publish output in the app
- Open the publish folder
- Install, uninstall, and launch the app through `adb`
- Discover available Android emulators and launch the one you want
- Capture the app window to clipboard or disk with a visible countdown

## Why this exists

MAUI Android publish commands can become long and easy to get wrong, especially when switching between safer test builds and more aggressive optimized builds. This app keeps the command visible, keeps the options grouped in one place, and helps you iterate faster without losing track of what changed.

## Getting started

### Requirements

- .NET 10 SDK
- Android SDK command-line tools
- `adb` available on `PATH` for emulator install/launch actions
- Android `emulator` tool available on `PATH` or in the default SDK install location for AVD discovery and launch

### Run locally

```bash
dotnet build MauiAppPublisher.slnx
dotnet run --project src/MauiAppPublisher/MauiAppPublisher.Desktop/MauiAppPublisher.Desktop.csproj
```

### Main workflow

1. Pick the MAUI project directory.
2. Review the detected `.csproj`, package id, and target framework.
3. Toggle the Android publish settings you want.
4. Confirm the command preview looks correct.
5. Publish and watch the live output panel.
6. Open the output folder or push the build to an emulator.

## Project structure

```text
MauiAppPublisher/
├── .github/
│   └── workflows/
├── docs/
│   └── maui-app-publisher-icon.png
├── MauiAppPublisher.slnx
└── src/
    └── MauiAppPublisher/
        ├── MauiAppPublisher/
        ├── MauiAppPublisher.Browser/
        └── MauiAppPublisher.Desktop/
```

## GitHub Actions

This repository includes Actions workflows for:

- CI validation on pushes and pull requests
- cross-platform desktop publish packaging on version tags

That gives the repo a solid starting point for sharing on GitHub without having to set up the basics later.

## Roadmap and wishlist

### Publish workflows

- Add guided presets for safe test builds, store-ready Android builds, and repeatable release profiles
- Add validation rules that disable unsupported combinations before publish
- Add saved publish profiles per project

### Google Play support

- Add support for publishing directly to Google Play
- Support the common rollout stages:
  - internal testing
  - closed testing
  - open testing
  - production rollout
- Add release notes entry and staged rollout percentage support

### Signing improvements

- Add better keystore validation before publish starts
- Add secure secret storage integration instead of plain text fields
- Add signing key rotation helpers
- Add automation for switching between debug, local release, and store signing identities

### Platform expansion

- Add iOS publish support
- Add iOS archive/export presets
- Add simulator discovery and launch
- Add platform-specific validation so unsupported options are hidden or disabled automatically

## Current status

The app is already useful for MAUI Android publish flows and emulator checks, but it is still evolving. The roadmap above reflects the next useful improvements for shipping and store workflows.

## Development notes

- The command preview is the source of truth for the publish command the app will run.
- Some MAUI projects override trim-related properties in custom ways. Maui App Publisher now detects common project-specific trim property patterns so AOT and trimming combinations can be emitted correctly.

## Contributing

Small focused improvements are welcome, especially around publish validation, emulator ergonomics, packaging support, and release automation.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
