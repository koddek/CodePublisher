# .NET 10 and .NET 11 Publishing Analysis

Reviewed: 2026-09-25

## Decision

Keep the production target on .NET 10 LTS (`net10.0`). Pin the repository SDK to the .NET 10 feature band with `global.json` while .NET 11 is still a release candidate. .NET 11 RC1 is go-live, but it is not GA; the official schedule still targets November 2026. The application should be validated on .NET 11 in an isolated branch or a separate SDK selection after GA, Avalonia/workload compatibility is confirmed, and the full test and publish matrix is rerun.

Do not retarget this Avalonia application to .NET 11 as part of the current stabilization increment. The publisher can inspect and publish multi-targeted `net11.0` projects once the .NET 11 SDK and workloads are installed, but its safe no-project defaults remain `net10.0`.

## What “one SDK” means

The remembered change has two separate candidates:

- **`dotnetup`** is the new cross-platform, user-level toolchain manager shipped on the .NET SDK `release/dnup` branch. It installs, updates, and removes SDKs/runtimes per user without a system package manager and can manage multiple SDK channels side by side. It is still on an experimental documentation branch and its download scripts default to `preview` quality, so this repository does not depend on it.
- **CoreCLR unification** is primarily the .NET MAUI runtime unification, not removal of `Microsoft.NET.Sdk` or workload packages.

Details:

- .NET 11 makes CoreCLR the default runtime for .NET MAUI Android, iOS, Mac Catalyst, and tvOS targets (`.NET 11 Preview 4` onward). This is one runtime/toolchain story across platforms, with default partial ReadyToRun and packaged PGO profiles.
- .NET for Android and .NET for Apple platforms still ship as workloads, and the project SDK is still `Microsoft.NET.Sdk` (with `Microsoft.NET.Sdk.WebAssembly` for the browser host).
- Blazor WebAssembly is explicitly not affected; WASM continues to use Mono. Avalonia and Uno Platform also rely on Mono-derived paths for browser and mobile hosting, so Avalonia mobile follows its own compatibility timeline.
- NativeAOT on Android is "actively underway" and on iOS/Mac Catalyst builds on the existing Apple ahead-of-time compilation; neither is a shipping feature to depend on in .NET 11.
- Community startup/size regressions exist for larger Android apps (dotnet/android#10588, #10914), and `UseMonoRuntime=true` is the temporary opt-out through .NET 11 servicing.
- For this repository, the practical SDK change is SDK selection and publish tooling, not a MAUI runtime migration.

## Current project baseline

| Area | Current state |
| --- | --- |
| App target | `net10.0` |
| Desktop host | `net10.0`, `Microsoft.NET.Sdk`, Avalonia 12.1.2 |
| Browser host | `net10.0-browser`, `Microsoft.NET.Sdk.WebAssembly` |
| Tests | `net10.0`, TUnit executable, 70 tests passing in Release on macOS osx-arm64 |
| SDK selected locally | 10.0.401; `global.json` selects the latest .NET 10 feature band |
| Android/iOS | .NET 10 workloads plus Android SDK/JDK/Xcode requirements |
| Publish profiles | In-app `PlatformProfiles`, not MSBuild `.pubxml` files |

## .NET 10 features relevant to this app

| Feature | Benefit | Relevance |
| --- | --- | --- |
| JIT, GC, and runtime improvements | Faster code, lower startup/memory pressure in the published app | Runtime benefit; it does not make the publisher’s own build dramatically faster |
| NativeAOT improvements | Smaller native output and better interface-heavy code | Useful for a small CLI; Avalonia desktop AOT needs reflection/XAML validation |
| ReadyToRun | Lower first-use latency while retaining dynamic code | Good desktop option when startup matters; output is larger and RID-specific |
| Trimming | Smaller self-contained output | Safe only with `SelfContained=true`; Avalonia XAML/reflection paths need roots and runtime tests |
| Single-file | Easier distribution | Platform/RID-specific; NativeAOT already produces a native executable |
| `RuntimeIdentifier` behavior | Predictable restore and publish assets | A RID-specific publish needs a RID-aware restore; implicit restore is intentional |
| Platform-specific .NET tools | One tool package can carry several RIDs | Not used by this application today |
| Microsoft.Testing.Platform support | Better `dotnet test` integration | The project still follows the repository contract and runs TUnit with `dotnet run` |
| NuGet package pruning | Smaller dependency graphs for `net10.0` projects | Reduces restored framework-provided package references in newer SDKs |
| MSBuild/.NET task alignment | Custom tasks can run under the CLI and newer Visual Studio MSBuild | No custom MSBuild task is currently shipped |

## .NET 11 RC1 features relevant to this app

| Feature | Benefit | Risk/adoption note |
| --- | --- | --- |
| Smaller SDK installers | Lower install footprint on Linux/macOS | Tooling improvement, not an application runtime change |
| NativeAOT CLI fast path | `dotnet` handles more commands in a native binary; enabled by default in .NET 11 | Changes CLI behavior and troubleshooting paths; do not make it a production dependency |
| MSBuild server by default | Lower repeated build startup overhead | Uses a persistent build server; diagnostics and CI behavior must be checked |
| Mobile device selection in `dotnet run`/`watch` | Better Android/iOS ergonomics | Workload-dependent; this app performs its own device discovery |
| More `dotnet test` controls and mobile test templates | Better test orchestration | Not a replacement for the existing TUnit contract |
| Runtime-native async, JIT, and R2R improvements | Runtime throughput/startup improvements | Requires a new target and a fresh compatibility pass |
| CoreCLR WebAssembly progress | Potential long-term browser runtime unification | Still a separate/evolving path; not a reason to change the current browser host |
| In-process mobile crash logging | Better on-device diagnostics | Mobile-only and workload-dependent |
| RISC-V/s390x enablement and 32-bit GC work | Benefits constrained/IoT-class runtimes | No new first-party “IoT SDK” replaces the separate .NET IoT Libraries |
| Analyzer and container improvements | Better warnings and container publishing | Requires updated tooling and package validation |

Relevant .NET 11 breaking changes for this repository are narrow but real:

- Minimum hardware requirements updated for x86/x64/Arm64.
- NativeAOT uses a `lib` prefix for native library outputs on Unix, which changes artifact names that scripts must match.
- Restore no longer searches the global packages folder for a higher version, so an upgrade can surface a lower cached version that was previously hidden.
- `dnx` scripts bypass `global.json` SDK selection, so the new SDK pin does not cover that execution path.
- .NET MAUI raises the minimum Android API level to 24.
- NativeAOT CLI command handling is enabled by default, and MSBuild server usage changes local build server behavior.

## Publish mode matrix

| Mode | Advantages | Costs/risks | Recommended use |
| --- | --- | --- | --- |
| Framework-dependent | Smallest output; runtime security updates come from the installed runtime | Requires a matching runtime; trimming is invalid | Developer machines and controlled enterprise desktops |
| Self-contained | No runtime prerequisite; predictable deployment | Larger, RID-specific output | Default for this desktop app |
| Self-contained + trimmed | Smallest regular runtime output | Reflection/XAML breakage; only valid self-contained | After trim warnings are clean and the app is smoke-tested |
| ReadyToRun | Lower startup/first-use latency; dynamic code remains | Larger assemblies; mutually exclusive with NativeAOT | Desktop release when startup matters more than size |
| NativeAOT | Smallest/fastest native output; no JIT | No dynamic loading/generation, stricter trimming, platform toolchain, longer build | Controlled desktop/CLI use cases after AOT runtime testing |
| Single-file | One distributable file | Still platform/RID-specific; extraction and diagnostics caveats | Desktop distribution convenience |
| Android APK/AAB | Native Android delivery; AAB is the Play Store format | Workload, JDK, Android SDK, signing, linker/AOT combinations | Play Store and device testing |
| iOS/Mac Catalyst IPA/archive | Apple tooling and store delivery | macOS/Xcode, provisioning, signing, platform-specific compilation | Apple device/store workflows |
| Browser WebAssembly | Browser deployment without a desktop runtime | Separate SDK/workload; no desktop device/process actions | Browser host only |

NativeAOT is already a native executable; do not combine it with the single-file bundler as a required optimization. The in-app policy now disables that combination.

## Combination rules

| Combination | Result | Enforcement |
| --- | --- | --- |
| Framework-dependent + trimmed | Invalid | Normalizes trimming off; command builder rejects invalid direct input |
| Self-contained + trimmed | Valid | Requires trim-safe roots and runtime tests |
| NativeAOT + ReadyToRun | Invalid | Normalizes ReadyToRun off; command builder rejects conflicts |
| NativeAOT + trimmed | AOT implies full trimming | UI leaves the separate trim switch disabled |
| NativeAOT + single-file | Redundant/risky | UI clears single-file; command builder rejects direct input |
| Android RunAOT + `AndroidLinkMode=None` | Invalid | Normalized off; command builder gives an actionable error |
| Android RunAOT + `PublishTrimmed=false` | Invalid (`XA1030`) | Normalized to trimmed; command builder gives an actionable error |
| Android D8 + ProGuard | Invalid (`XA1011`) | Profile falls back to R8/D8; legacy direct values are rejected |
| iOS simulator + archive/IPA | Invalid for the simulator path | Command uses `build` and omits archive/IPA options |
| MacCatalyst + R2R/AOT/single-file | Not applicable | Command emits explicit false values; policy explains the reason |

## Platform matrix

| Target | What works | Prerequisites and caveats |
| --- | --- | --- |
| Windows desktop | FDD, SCD, trim, R2R, AOT, single-file | Win64/WinArm64; NativeAOT needs the MSVC C++ toolchain; AOT is not cross-OS |
| Linux desktop/ARM | FDD, SCD, trim, R2R, AOT, single-file | Use the correct libc and RID; `linux-arm64` is the Raspberry Pi 64-bit direction |
| macOS desktop | FDD, SCD, trim, R2R, AOT, single-file, app bundle | Sign/notarize with Xcode tools; universal binaries require separate per-architecture publishes plus `lipo` |
| Android | APK/AAB, D8/R8, trimming, RunAOT | `net10.0-android`, Android workload, JDK, Android SDK, and `adb` for deployment |
| iOS/Mac Catalyst | Archive/IPA, linker/trimming, Apple compilation | Xcode version must match the Apple workload; device builds need signing/provisioning |
| Browser/WebAssembly | `Microsoft.NET.Sdk.WebAssembly` and `wasm-tools` | Separate from desktop publish; no file browsing, elevated processes, or device actions |
| IoT/embedded | Usually Linux ARM64/RISC-V-class SCD or FDD deployments | .NET IoT Libraries (`System.Device.Gpio`, `Iot.Device.Bindings`) are separate; this app has no hardware profile |

## Findings in this repository

1. The Release macOS self-contained single-file publish succeeds.
2. A ReadyToRun publish succeeds.
3. An initial trimmed publish exposed stale linker roots and reflection-based JSON serialization warnings. The linker descriptor now uses the current Android deployment type and no longer names deleted service types. The Google Play track payload now uses source-generated JSON metadata.
4. An initial NativeAOT publish produced IL2026/IL3050 warnings from anonymous JSON serialization and IL2008 warnings from stale linker roots. After the fixes, the application-owned publish is clean; Avalonia’s third-party `Avalonia.DesignerSupport` still reports an IL2104 warning under trimming.
5. A framework-dependent + trimmed publish reproduces `NETSDK1102`; the in-app normalization and command validation now prevent that combination.
6. The Windows command builder previously omitted `PublishAot=true` when AOT was selected. It now emits explicit AOT/R2R/single-file values.
7. The Android command previously allowed `RunAOTCompilation=true` with trimming disabled, which reproduces `XA1030`; it also exposed the deprecated D8/ProGuard pairing. Both are now normalized/rejected with actionable messages.
8. TFM selection contained hard-coded `net10.0` checks. Multi-targeted `net11.0` Windows/Linux projects are now detected by base-TFM pattern matching.
9. Tool discovery contained a user-specific Android SDK path and did not reliably resolve Windows executable extensions. Tool lookup is now user-independent and handles `.exe`/`.cmd` on Windows.
10. RID-specific `--no-restore` requires a RID-aware restore. The desktop project now declares host-appropriate `RuntimeIdentifiers`; normal publishes still use implicit restore.
11. The custom macOS app-bundle target ran after `Publish` but before the SDK's NativeAOT `CopyNativeBinary` step, so the `.app` bundle kept a 122 KB framework-dependent apphost while the publish root became a 25 MB native binary; launching the bundled app failed with "The application to execute does not exist". The bundle body is now shared by two anchors: `Publish` for normal publishes and `CopyNativeBinary` for NativeAOT. The bundle executable is now byte-identical to the publish root, and 4-second launch smokes of the default self-contained, trimmed, ReadyToRun, and NativeAOT bundles produced no console errors.
12. `PublishAot=true` combined with `--no-restore` silently degrades to a trimmed self-contained publish when the assets file was produced by a restore that did not see `PublishAot` (ILCompiler is then missing from the graph). Observed locally: `dotnet restore -p:Configuration=Release` followed by `dotnet publish -p:PublishAot=true --no-restore` produced a 122 KB apphost plus 71 managed assemblies and no native binary, with no error. The correct invocation restores implicitly, producing a 25 MB native binary with zero managed assemblies. CI now runs the NativeAOT publish without `--no-restore` and fails if the managed entry assembly is present.
13. `Avalonia.iOS` and `Avalonia.Android` were pinned to the 11.3 line while the core Avalonia packages were on 12.1.2. They are not referenced by any current project, but the central version list now keeps them aligned so a future mobile host does not inherit a mixed 11.3/12.1 graph. `Avalonia.Diagnostics` stays on 11.3.21 because NuGet has no 12.x release for that package.

## Stability recommendation

- Default release path: .NET 10 LTS + self-contained + single-file for desktop distribution; keep trimming off until the app has a clean trim run and a startup smoke test.
- Use ReadyToRun for a startup-sensitive desktop build after comparing output size and first-launch behavior.
- Treat NativeAOT as an opt-in profile, not the default. It now publishes without application-owned warnings and launches cleanly, but only when the publish is allowed to restore ILCompiler; verify the output has no managed entry assembly.
- Keep Android `LinkMode=None` as the conservative baseline, use R8, and let the policy force trimming when RunAOT is selected. Test AAB and APK separately.
- Keep iOS signing and Android SDK/JDK setup explicit; do not claim device/store success without the corresponding toolchain and credentials.
- After .NET 11 GA, create a separate upgrade validation pass: restore with the .NET 11 SDK, install matching workloads, build the solution, run the TUnit suite, publish each supported desktop RID, run browser smoke, and test Android/iOS on real devices. Measure startup and package size against the .NET 10 baseline rather than assuming an improvement.

## Verification commands

```bash
dotnet restore DotNetAppPublisher.slnx -p:Configuration=Release
dotnet build DotNetAppPublisher.slnx --configuration Release --no-restore
dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj --configuration Release --no-build -- --disable-logo --progress off

# NativeAOT: no --no-restore, and no -p:PublishSingleFile (NativeAOT is already native).
dotnet publish src/DotNetAppPublisher/DotNetAppPublisher.Desktop/DotNetAppPublisher.Desktop.csproj \
  -c Release -f net10.0 -r osx-arm64 -p:PublishAot=true -p:PublishSingleFile=false \
  -o "${TMPDIR:-/tmp}/dnap-aot"
test ! -f "${TMPDIR:-/tmp}/dnap-aot/DotNetAppPublisher.Desktop.dll"

# Trimming smoke: the only expected warning is third-party Avalonia.DesignerSupport IL2104.
dotnet publish src/DotNetAppPublisher/DotNetAppPublisher.Desktop/DotNetAppPublisher.Desktop.csproj \
  -c Release -f net10.0 -r osx-arm64 -p:PublishTrimmed=true -o "${TMPDIR:-/tmp}/dnap-trim"
```

The test suite does not call live Google Play, OAuth, Android devices, or iOS devices. Those require user credentials, platform tooling, and physical/emulated devices and remain manual validation items.

## Official sources

- [.NET 11 download and release-candidate status](https://dotnet.microsoft.com/en-us/download/dotnet/11.0)
- [What’s new in .NET 11](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/overview)
- [What’s new in the .NET 11 SDK](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/sdk)
- [.NET 11 breaking changes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/11)
- [.NET 10 SDK and tooling](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/sdk)
- [.NET application publishing overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/)
- [ReadyToRun deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run)
- [Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot)
- [Trimming self-contained applications](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-self-contained)
- [`dotnet publish` and `.pubxml` limitations](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish)
- [.NET for Android build properties](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties)
- [.NET for Apple platforms build properties](https://learn.microsoft.com/en-us/dotnet/ios/building-apps/build-properties)
- [Publish .NET apps for macOS](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos)
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [.NET MAUI CoreCLR transition](https://devblogs.microsoft.com/dotnet/dotnet-maui-moves-to-coreclr-in-dotnet-11)
- [Avalonia compiled bindings and NativeAOT guidance](https://docs.avaloniaui.net/docs/xaml/compilation)
- [`dotnetup` toolchain manager documentation (experimental SDK branch)](https://github.com/dotnet/sdk/tree/release/dnup/documentation/general/dotnetup)
