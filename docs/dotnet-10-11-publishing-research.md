# .NET 10 and .NET 11 Publishing Analysis

Reviewed: 2026-11-11

## Decision

The repository moved to the .NET 11 line after .NET 11 GA (2026-11-10, SDK 11.0.100). `global.json` pins the .NET 11 SDK feature band, every production project targets `net11.0`, and the test suite runs on `net11.0`. Avalonia stays on the latest stable line it supports (12.1.6; the pinned SkiaSharp 3.119.4 / HarfBuzzSharp 8.3.1.3 are the versions Avalonia.Skia is compiled against).

User projects targeting `net10.0`, `net9.0`, `net8.0`, and `net11.0` remain supported publish targets. The publish command builders gate settings on the target framework's major version: the Mono-only Android AOT properties (`RunAOTCompilation`, `AndroidEnableProfiledAot`, `PublishReadyToRun`) are emitted only for `net10.0-android` and earlier. On `net11.0-android` the .NET 11 SDK stops the build with error XA1044 when either Mono property is set at any value, so the app omits them entirely and leaves ReadyToRun to the CoreCLR default (partial R2R when trimming is enabled).

The browser host was removed in the .NET 11 line (see "The browser host is blocked" below).

## What “one SDK” means

The remembered change has two separate candidates:

- **`dotnetup`** is the new cross-platform, user-level toolchain manager shipped on the .NET SDK `release/dnup` branch. It installs, updates, and removes SDKs/runtimes per user without a system package manager and can manage multiple SDK channels side by side. It is still on an experimental documentation branch and its download scripts default to `preview` quality, so this repository does not depend on it.
- **CoreCLR unification** is primarily the .NET MAUI runtime unification, not removal of `Microsoft.NET.Sdk` or workload packages.

Details:

- .NET 11 makes CoreCLR the default runtime for .NET MAUI Android, iOS, Mac Catalyst, and tvOS targets (`.NET 11 Preview 4` onward). This is one runtime/toolchain story across platforms, with default partial ReadyToRun and packaged PGO profiles.
- .NET for Android and .NET for Apple platforms still ship as workloads, and the project SDK is still `Microsoft.NET.Sdk` (with `Microsoft.NET.Sdk.WebAssembly` for the browser host).
- Blazor WebAssembly is explicitly not affected; WASM continues to use Mono. Avalonia and Uno Platform also rely on Mono-derived paths for browser and mobile hosting, so Avalonia mobile follows its own compatibility timeline.
- NativeAOT on Android is "actively underway" and on iOS/Mac Catalyst builds on the existing Apple ahead-of-time compilation; neither is a shipping feature to depend on in .NET 11.
- Community startup/size regressions exist for larger Android apps (dotnet/android#10588, #10914). Mono is gone in .NET 11 for Android (`UseMonoRuntime=true` is rejected with NETSDK1242); the migration path is CoreCLR with default partial ReadyToRun.
- For this repository, the practical SDK change is SDK selection and publish tooling, not a MAUI runtime migration of the published projects.

## .NET 11 verification on this machine

Verified 2026-11-11 with SDK 11.0.100 (dotnetup), `maui-android 11.0.0/11.0.100`, `maui-ios 11.0.0/11.0.100`, `maui-maccatalyst 11.0.0/11.0.100`, `wasm-tools 11.0.100/11.0.100`:

- A plain `net11.0-android` project builds and publishes with `-r android-arm64 -p:SelfContained=true -p:AndroidLinkMode=SdkOnly -p:PublishTrimmed=true` produced `com.scratch.apktest-Signed.apk` and `-Signed.aab` in the `-o` folder; the app's baseline command publishes the APK alone.
- The app's pre-upgrade Android command (`-p:RunAOTCompilation=false -p:AndroidEnableProfiledAot=false`) reproduces **error XA1044** on `net11.0-android`: "The MSBuild property 'RunAOTCompilation' is not compatible with the CoreCLR runtime." After the upgrade the generated command omits both properties on `net11.0` and keeps them for `net10.0-android`, and the publish succeeds.
- On `.NET 11` Android, `RunAOTCompilation` and `AndroidEnableProfiledAot` configure the Mono AOT pipeline, which no longer exists. `AndroidEnableProfiledAot=false` is not an escape hatch; removing the property is the only valid state. `$(UseMonoRuntime)=true` is rejected with NETSDK1242.
- ReadyToRun on `net11.0-android` requires trimming: without `PublishTrimmed=true`, the build prints `warning XA0119: ReadyToRun has been disabled because trimming is disabled`.
- Android `net11.0` defaults to CoreCLR with default partial ReadyToRun and packaged PGO. NativeAOT on Android remains experimental and is not offered by this app. Android API 24 is the minimum.
- `net11.0-ios` builds for `iossimulator-arm64`. The minimum `SupportedOSPlatformVersion` is 15.0 (the .NET 11 workloads reject lower). The iOS 27 SDK additionally requires a `UIApplicationSceneManifest` in Info.plist (error MT7199) or `ValidateSceneManifest=false` for validation-only scenarios.
- `net11.0-maccatalyst` builds for `maccatalyst-arm64`. The minimum `SupportedOSPlatformVersion` is 17.0.
- The iOS and Catalyst device builds still require Xcode and signing/provisioning; the app emits `ArchiveOnBuild`/`BuildIpa` for device RIDs and a plain `build` for simulator RIDs.
- A Release build of an Android app without an RID produced both `.aab` and `.apk`; with `-r android-arm64` the APK/AAB are copied into the `-o` folder the way the desktop RIDs behave.

## The browser host is blocked

The `.NET 11` wasm-tools workload bundles an Emscripten toolchain whose LLVM (through LLVM 23) no longer emits the `saveSetjmp` / `testSetjmp` symbols that the prebuilt SkiaSharp WebAssembly static libraries reference. Linking `dotnet.native.wasm` fails with `undefined symbol: saveSetjmp`. Avalonia has tracked the same problem (AvaloniaUI/Avalonia#21700: "Emscripten/LLVM upgrade for Net11 preview 6", which moved to Emscripten 5.0.6 and will bump again to the Emscripten release backed by LLVM 23) and notes that `libSkiaSharp.a` and `libHarfBuzzSharp.a` need to be recompiled with the new toolchain. No Avalonia or SkiaSharp release on NuGet ships those assets yet (latest stable Avalonia is 12.1.6; SkiaSharp stable line is 4.155/4.156, and Avalonia.Skia is compiled against SkiaSharp 3.119.4).

The browser host was therefore removed from the .NET 11 line by decision on 2026-11-11. It stays in git history and should be restored when Avalonia ships WebAssembly assets built for the .NET 11 Emscripten toolchain (expected in a later 12.x release). Until then, `net10.0-browser` is the last published configuration of the host.

## Current project baseline

| Area | Current state |
| --- | --- |
| App target | `net11.0` |
| Desktop host | `net11.0`, `Microsoft.NET.Sdk`, Avalonia 12.1.6 |
| Browser host | **Removed** (blocked; see "The browser host is blocked") |
| Tests | `net11.0`, TUnit 1.83.0 executable, 545 tests passing in Release on macOS osx-arm64 |
| SDK selected | 11.0.100; `global.json` selects the latest .NET 11 feature band |
| Android/iOS | .NET 11 workloads (`maui-android`, `maui-ios`, `maui-maccatalyst` 11.0.0/11.0.100) plus Android SDK/JDK/Xcode requirements |
| Publish profiles | In-app platform libraries (`Features/Publishing` per platform), not MSBuild `.pubxml` files |
| Project layout | Per-platform libraries (`CodePublisher.Platforms.*`) composed by `CodePublisher.Publishing` over the `CodePublisher.Core` kernel |

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

## .NET 11 features relevant to this app

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
| Framework-dependent + trimmed | Invalid | The graph turns self-contained on with the trimming request; the SDK would fail with NETSDK1102 |
| Self-contained + trimmed | Valid | Requires trim-safe roots and runtime tests |
| NativeAOT + ReadyToRun | Invalid | The graph turns ReadyToRun off; the SDK does not error but the two are mutually exclusive (dotnet/runtime#126598) |
| NativeAOT + trimmed | AOT implies trimming | The graph keeps trimming on. The SDK fails the build with "PublishTrimmed is implied by native compilation and cannot be disabled." when trimming is explicitly off |
| NativeAOT + single-file | Redundant | The graph turns single-file off; the SDK skips the single-file bundler under AOT without an error |
| Single-file + no app host | Invalid | The graph turns the app host on; the SDK fails with NETSDK1098 |
| Android RunAOT + `AndroidLinkMode=None` | Invalid (`XA1030`) on .NET 10 and earlier | The graph switches the link mode to SdkOnly; the SDK fails without trimming |
| Android RunAOT on .NET 11 | Invalid (`XA1044`) | The Mono AOT pipeline is gone; `net11.0-android` strips `RunAOTCompilation` and `AndroidEnableProfiledAot` before the command is emitted |
| .NET 11 Android + trimming disabled | ReadyToRun disabled (`XA0119`) | `PublishTrimmed=true` (or a link mode that implies it) is required for partial R2R |
| Android D8 + ProGuard | Invalid (`XA1011`) | Profile falls back to R8/D8; legacy direct values are rejected |
| iOS + `PublishTrimmed=false` | Invalid | The Apple workload forces trimming; `TrimMode=copy` is the supported opt-out |
| Non-RID Release Android | Both `aab` and `apk` by default | `AndroidPackageFormats` defaults to `aab;apk` for Release; this app always sends the selected formats explicitly |
| iOS simulator + archive/IPA | Invalid for the simulator path | Command uses `build` and omits archive/IPA options |
| MacCatalyst + R2R/AOT/single-file | Not applicable | Command emits explicit false values; policy explains the reason |
| iOS BuildIpa without ArchiveOnBuild | Incomplete | The graph turns archiving on, because the .ipa is packaged from the archive |

## Platform matrix

| Target | What works | Prerequisites and caveats |
| --- | --- | --- |
| Windows desktop | FDD, SCD, trim, R2R, AOT, single-file | Win64/WinArm64; NativeAOT needs the MSVC C++ toolchain; AOT is not cross-OS |
| Linux desktop/ARM | FDD, SCD, trim, R2R, AOT, single-file | Use the correct libc and RID; `linux-arm64` is the Raspberry Pi 64-bit direction |
| macOS desktop | FDD, SCD, trim, R2R, AOT, single-file, app bundle | Sign/notarize with Xcode tools; universal binaries require separate per-architecture publishes plus `lipo` |
| Android | APK/AAB, D8/R8, trimming, RunAOT (Mono/`net10.0-android` and earlier only); CoreCLR partial R2R (net11.0-android) | `net10.0-android`/`net11.0-android`, Android workload, JDK, Android SDK, and `adb` for deployment |
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
14. The publish options were related by scattered checks: a capability check in `PublishOptionPolicy` and a separate normalization pass in `PublishSettingsPolicy`. The two could disagree, and the normalization ran after the fact, so a combination the SDK rejects could reach the command. A single declarative graph now holds every relationship, and the platform rules and the graph are applied together until the result settles.
15. Native AOT on a desktop target published `-p:PublishTrimmed=false` alongside `-p:PublishAot=true`. The .NET SDK rejects that combination with "PublishTrimmed is implied by native compilation and cannot be disabled." (dotnet/runtime `Microsoft.NETCore.Native.Publish.targets`). Reproduced locally: the same publish succeeds with `PublishTrimmed=true` and fails with `PublishTrimmed=false`. The graph now keeps trimming on under AOT.
16. The .NET 8 SDK infers a self-contained publish for a trimmed publish, so a trimmed request no longer has to be dropped for a framework-dependent publish. The graph turns self-contained on with the trimming request instead, matching the SDK's own inference.
17. Google Play signs every download with the app signing key, not the upload key. A build signed with the upload key is a different signer to the platform, so Android refuses to update it and Play never offers one. The install guidance and the new device inspection report the certificate rather than implying that the upload key is the right one.
18. The Play API cannot update a track by itself. `edits.tracks.update` replaces the desired state of the track, so an empty release array is how a release is removed from it, and a `halted` status is how a rollout is stopped. Promotion is the same call against the target track with the same version code; the bundle is not uploaded again.

## Stability recommendation

- Default release path: .NET 11 self-contained + single-file for desktop distribution; keep trimming off until the app has a clean trim run and a startup smoke test.
- Use ReadyToRun for a startup-sensitive desktop build after comparing output size and first-launch behavior.
- Treat NativeAOT as an opt-in profile, not the default. On .NET 11, AOT publish skips the full `Build` target (`UseOptimizedPublish`); verify the output has no managed entry assembly. It publishes without application-owned warnings and launches cleanly, but only when the publish is allowed to restore ILCompiler.
- For `net11.0-android`, ship `AndroidLinkMode=SdkOnly` with `PublishTrimmed=true` (the baseline profile) or `None` for a fast test loop; the app forces neither. RunAOT and ProfiledAOT no longer exist; ReadyToRun comes with trimming by default.
- Test AAB and APK separately. The `.NET 11` Android workload supports `android-arm`, `android-arm64`, and `android-x64` (minimum API 24).
- Keep iOS signing and Android SDK/JDK setup explicit; do not claim device/store success without the corresponding toolchain and credentials.
- The .NET 11 upgrade validation pass ran on 2026-11-11: restore, solution Release build, 545-test TUnit suite, scratch publishes of `net11.0-android` (APK + AAB), `net10.0-android` (fallback Mono flags intact), `net11.0-ios` (simulator), `net11.0-maccatalyst`, and a desktop NativeAOT publish. Remaining manual items: real iOS/Android device deploys, Google Play submission, and a startup/size comparison against the .NET 10 baseline.

## Verification commands

```bash
dotnet restore CodePublisher.slnx -p:Configuration=Release
dotnet build CodePublisher.slnx --configuration Release --no-restore
dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --configuration Release --no-build -- --disable-logo --progress off

# NativeAOT: no --no-restore, and no -p:PublishSingleFile (NativeAOT is already native).
dotnet publish src/CodePublisher/CodePublisher.Desktop/CodePublisher.Desktop.csproj \
  -c Release -f net11.0 -r osx-arm64 -p:PublishAot=true -p:PublishSingleFile=false \
  -o "${TMPDIR:-/tmp}/dnap-aot"
test ! -f "${TMPDIR:-/tmp}/dnap-aot/CodePublisher.Desktop.dll"

# Trimming smoke: the only expected warning is third-party Avalonia.DesignerSupport IL2104.
dotnet publish src/CodePublisher/CodePublisher.Desktop/CodePublisher.Desktop.csproj \
  -c Release -f net11.0 -r osx-arm64 -p:PublishTrimmed=true -o "${TMPDIR:-/tmp}/dnap-trim"

# net11.0-android scratch publish (observed output: Signed APK and AAB in the -o folder):
dotnet publish Scratch.csproj -f net11.0-android -c Release -r android-arm64 \
  -p:SelfContained=true -p:AndroidLinkMode=SdkOnly -p:PublishTrimmed=true -o <dir>
```

The test suite does not call live Google Play, OAuth, Android devices, or iOS devices. Those require user credentials, platform tooling, and physical/emulated devices and remain manual validation items.

## Official sources

- [.NET for Android build properties](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties) — CoreCLR default in .NET 11, XA1044, minimum API 24, `ApplicationArtifact`, `GetApplicationArtifacts`.
- [.NET 11 Announcing](https://devblogs.microsoft.com/dotnet/announcing-dotnet-11/) — GA status, NativeAOT CLI host, MSBuild server default, smaller SDK installers.
- [What's new in .NET 11](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/overview) and [What's new in the .NET 11 SDK](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/sdk) — SDK features, minimum OS/architecture requirements, container tooling.
- [Breaking change: AOT publish skips full Build target](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/11/aot-publish-skips-full-build) — `UseOptimizedPublish` in .NET 11 RC2.
- [.NET for Android error XA1044](https://learn.microsoft.com/en-us/dotnet/android/messages/xa1044) and [XA1030](https://learn.microsoft.com/en-us/dotnet/android/messages/xa1030) — Mono-only AOT properties on CoreCLR.
- [.NET Target Framework Monikers / iOS build properties](https://learn.microsoft.com/en-us/dotnet/ios/building-apps/build-properties) — iOS 15.0+ and Mac Catalyst 17.0+ minimums, scene manifest requirement.
- [Avalonia issue #21700](https://github.com/AvaloniaUI/Avalonia/issues/21700) — the browser-host block.

- [.NET 11 download and release-candidate status](https://dotnet.microsoft.com/en-us/download/dotnet/11.0)
- [How Android handles app updates](https://developer.android.com/google/play/app-updates) — the update rules: same application ID, same signing certificate (or a proof of rotation), and a version code at least as high as the installed one.
- [Use Play App Signing](https://support.google.com/googleplay/android-developer/answer/9842756) — the upload key signs what you upload; the app signing key signs what the device receives.
- [Version codes and testing track statuses](https://support.google.com/googleplay/android-developer/answer/9845334) — the highest eligible version code wins, production is always eligible, and internal testing is exclusive.
- [Google Play release tracks](https://developers.google.com/android-publisher/tracks) — the track identifiers, the release statuses, and the halt and rollout rules.
- [Ending a test](https://support.google.com/googleplay/android-developer/answer/9845334) — removing a release from a testing track so testers fall back to production.
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
