# Gates: feature slices and stabilization

OWNS: src/DotNetAppPublisher/DotNetAppPublisher/Features/GooglePlay/**, src/DotNetAppPublisher/DotNetAppPublisher/Features/Publishing/**, src/DotNetAppPublisher/DotNetAppPublisher/Features/Deployment/**, src/DotNetAppPublisher/DotNetAppPublisher/Features/Shared/Process/**, src/DotNetAppPublisher/DotNetAppPublisher/Views/GooglePlay/**, tests/Directory.Packages.props, tests/DotNetAppPublisher.Tests/**, .github/workflows/ci.yml, global.json, docs/dotnet-10-11-publishing-research.md, src/DotNetAppPublisher/DotNetAppPublisher/App.axaml.cs, src/DotNetAppPublisher/DotNetAppPublisher/ViewModels/MainViewModel.Navigation.cs, src/DotNetAppPublisher/DotNetAppPublisher/Views/MainView.axaml, src/DotNetAppPublisher/DotNetAppPublisher/Views/MainView.axaml.cs, src/DotNetAppPublisher/DotNetAppPublisher/Styles/AppStyles.axaml, src/DotNetAppPublisher/DotNetAppPublisher/Services/DesktopInteractionService.cs, src/DotNetAppPublisher/DotNetAppPublisher/Services/PublisherService.cs, src/DotNetAppPublisher/DotNetAppPublisher/Trimming/LinkerDescriptor.xml, src/DotNetAppPublisher/DotNetAppPublisher/DotNetAppPublisher.csproj, src/DotNetAppPublisher/DotNetAppPublisher.Desktop/DotNetAppPublisher.Desktop.csproj, src/DotNetAppPublisher/Directory.Packages.props, README.md, AGENTS.md

Scope: Deliver an isolated Google Play detail page with secure in-memory account connections, release configuration, local AAB validation, and a complete API publishing workflow.

- [x] G1: The solution builds with the isolated feature slice.
  CHECK: dotnet build DotNetAppPublisher.slnx --no-restore
  EXPECT: Build succeeded.
  EVIDENCE: exit=0; shell=/bin/sh; cwd=/usr/local/repo/DotNetAppPublisher; path=f8958168e4b0/30 entries; EXPECT=matched; output-sha256=0c9306aa5d6b1ef28b41191b5e6fc92c3fa4195901e74f808120fd6beab6ca40; output-bytes=9278

- [x] G2: The feature has no embedded Google, Android keystore, OAuth, or service-account secrets.
  CHECK: if rg -n "BEGIN (RSA )?PRIVATE KEY|refresh_token[\"']?\\s*[:=]|private_key[\"']?\\s*[:=]" src/DotNetAppPublisher/DotNetAppPublisher/Features/GooglePlay src/DotNetAppPublisher/DotNetAppPublisher/Views/GooglePlay; then exit 1; else echo clean; fi
  EXPECT: clean
  EVIDENCE: exit=0; shell=/bin/sh; cwd=/usr/local/repo/DotNetAppPublisher; path=f8958168e4b0/30 entries; EXPECT=matched; output-sha256=2e22da2ab13713309ac75219e525b8e06ed02f3f1963b8feef203fa25827f93d; output-bytes=6

- [x] G3: Google Play is isolated behind a dedicated view and view model.
  CHECK: test -f src/DotNetAppPublisher/DotNetAppPublisher/Views/GooglePlay/GooglePlayView.axaml && test -f src/DotNetAppPublisher/DotNetAppPublisher/Features/GooglePlay/GooglePlayViewModel.cs && echo isolated-feature-present
  EXPECT: isolated-feature-present
  EVIDENCE: exit=0; shell=/bin/sh; cwd=/usr/local/repo/DotNetAppPublisher; path=f8958168e4b0/30 entries; EXPECT=matched; output-sha256=0efdcacaf2b9dbc05e602a79910535b3f4cbd5519013364a472a9c2c2a9ca946; output-bytes=25

- [x] G4: The Google Play page presents account, release, readiness, status, and environment setup states.
  EVIDENCE: GooglePlayView.axaml contains Account, setup, App, Release, and Publish cards; the App card links upload-key setup to Signing; MainView.axaml owns the upload-key controls; GooglePlayViewModel exposes environment rows, readiness, status tones, tracks, rollout, notes, cancellation, and session clearing.

## Stabilization increment: T0–T2

- [x] T0: TUnit test project and CI execution exist.
  CHECK: dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: all tests pass.
  EVIDENCE: 22 passed on macOS; `tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj` is registered in `DotNetAppPublisher.slnx`; `.github/workflows/ci.yml` runs the Release test executable.

- [x] T1: Elevated process execution rejects secret environment variables and owner-restricts temporary scripts.
  CHECK: dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: secret rejection and Unix permission tests pass.
  EVIDENCE: `Features/Shared/Process/ElevatedProcessSecurity.cs`; `Security/ElevatedProcessSecurityTests.cs`; 22 total tests passed.

- [x] T2: Publish configuration rules and platform defaults live in the Publishing/Configure slice and cross-option normalization is covered.
  CHECK: dotnet restore DotNetAppPublisher.slnx -p:Configuration=Release && dotnet build DotNetAppPublisher.slnx --configuration Release --no-restore
  EXPECT: build succeeds with zero warnings and errors.
  EVIDENCE: `Features/Publishing/Configure/PublishOptionPolicy.cs`, `PlatformProfiles.cs`, `PublishPlatforms.cs`, and `PublishSettingsState.cs`; 22 tests passed before and after the refactor.

## Stabilization increment: T3–T9

- [x] T3: Extract publish command construction into a pure, tested Publishing/Build component.
  CHECK: dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: command matrix tests pass.
  EVIDENCE: `Features/Publishing/Build/PublishCommandBuilder.cs`; `PublishCommandBuilderTests.cs`; 27 tests passed.

- [x] T4: Extract project inspection, validation, and output-layout resolution.
  CHECK: dotnet build DotNetAppPublisher.slnx --no-restore
  EXPECT: build succeeds with zero warnings and errors.
  EVIDENCE: `Features/Publishing/Inspect/ProjectInspector.cs` and `ProjectOutputLayoutResolver.cs`; 33 tests passed; Debug build has zero warnings/errors.

- [x] T5: Extract process, deployment, capture, and artifact capabilities from `PublisherService`.
  CHECK: dotnet build DotNetAppPublisher.slnx --no-restore
  EXPECT: build succeeds with zero warnings and errors.
  EVIDENCE: `Features/Publishing/Process`, `Features/Publishing/Artifacts`, `Features/Publishing/Capture`, `Features/Deployment/Android`, and `Features/Deployment/Ios`; 41 tests passed; Debug build has zero warnings/errors.

- [x] T6: Introduce immutable publish and Google Play state snapshots with guarded transitions.
  CHECK: dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: state and workflow tests pass.
  EVIDENCE: `Features/GooglePlay/State/GooglePlayStateSnapshot.cs` and `GooglePlayStateHolder.cs`; publish state policy; 44 tests passed.

- [x] T7: Split Google Play authentication, tracks, releases, signing, and setup into focused slices.
  CHECK: dotnet build DotNetAppPublisher.slnx --no-restore
  EXPECT: build succeeds with zero warnings and errors.
  EVIDENCE: `Features/GooglePlay/Authentication`, `Tracks`, `Releases`, `Signing`, `Setup`, and `State`; 44 tests passed; Debug build has zero warnings/errors.

- [x] T8: Complete environment setup controls, platform guards, and browser runtime verification.
  CHECK: dotnet build DotNetAppPublisher.slnx --no-restore
  EXPECT: build succeeds with zero warnings and errors; browser smoke evidence recorded.
  EVIDENCE: `GooglePlayView.axaml` now presents human labels, examples, detected values, status, and compact accessible actions; browser guards prevent desktop discovery in browser; `main.js` synchronizes the canvas backing store; built-in browser smoke at `http://127.0.0.1:5235/?ui=postclean2` loaded a 2000×1400 canvas, closed the splash, and reported no console errors.

- [x] T9: Refresh documentation, DOX ownership, CI, and final verification.
  CHECK: dotnet restore DotNetAppPublisher.slnx -p:Configuration=Release && dotnet build DotNetAppPublisher.slnx --configuration Release --no-restore && dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj --configuration Release --no-build -- --disable-logo --progress off
  EXPECT: Release build has zero warnings/errors and all tests pass.
  EVIDENCE: README, CI, local DOX contracts, and GATES updated; Debug and Release solution builds completed with zero warnings/errors; 46 TUnit tests passed in the latest Release verification (45 before the setup-guidance test); `git diff --check` passed; browser smoke loaded a 2000×1400 canvas with splash closed and no fresh console errors.

## UI refinement increment

- [x] UI1: Google account entry is adjacent to Google Play; upload-key controls live under Signing.
  CHECK: dotnet build DotNetAppPublisher.slnx --no-restore
  EXPECT: build succeeds with zero warnings and errors.
  EVIDENCE: GooglePlayView.axaml keeps Account first and links App upload-key setup to Signing; MainView.axaml owns the manual and Google Play upload-key cards.

- [x] UI2: Narrow and wide layouts preserve reading order, readable labels, and usable controls.
  CHECK: browser smoke at desktop and narrow viewport sizes
  EXPECT: no clipped primary controls, no horizontal overflow, and no console errors.
  EVIDENCE: browser host at `http://127.0.0.1:5235/` produced a `2000×1400` canvas at `1000×700`; resizing the host to `360×700` and `320×560` produced `720×1400` and `640×1120` canvases, `scrollWidth == clientWidth`, splash class `splash-close`, and no console error messages.

- [x] UI3: Low-emphasis OK/Go-style actions use compact accessible icon controls.
  CHECK: dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: all tests pass.
  EVIDENCE: compact `PathIcon` buttons now cover refresh, browse, open, copy, cancel, environment actions, and signing-key actions with tooltips and automation names; Release suite passed 45 tests.

## UI correction increment

- [x] UI4: The minimum desktop layout keeps header actions readable and removes clipped navigation chrome.
  CHECK: desktop screenshot at the minimum window size plus browser narrow-host smoke
  EXPECT: no overlapping header controls, clipped Configure label, or horizontal overflow.
  EVIDENCE: desktop `320×560` screenshot showed wrapped header actions, hidden compact navigation labels, and no header overlap; separate `IsPublishing` state keeps the cancel action hidden during background device discovery; browser host at `320×560` produced a `640×1120` canvas, `scrollWidth == clientWidth`, splash closed, and no console error messages.

- [x] UI5: Signing and Google Play are adjacent in the left master-detail navigation.
  CHECK: `MainViewModel.Navigation.cs` NavItems order
  EXPECT: Signing is immediately followed by Google Play.
  EVIDENCE: `NavItems` order is Project, Platform, Build, Signing, Google Play, Output, Deploy, Log.

- [x] UI6: Error and confirmation dialogs use a title, separate details, vertically centered message content, and responsive width.
  CHECK: dotnet build DotNetAppPublisher.slnx --no-restore
  EXPECT: build succeeds with zero warnings and errors; routine status remains inline.
  EVIDENCE: Release solution build completed with zero warnings/errors; 46 TUnit tests passed; `DesktopInteractionService` now uses title/details layout, centered details, wrapping actions, and owner-width dialog sizing.

- [x] UI7: Every Google Play setup value explains where to obtain it and links to an official source.
  CHECK: dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: setup definitions contain acquisition guidance and source URLs; all tests pass.
  EVIDENCE: setup definitions now cover Google Cloud OAuth credentials, Temurin JDK, and Android Studio SDK locations; `GooglePlayView.axaml` renders the guidance and source action inline; 46 TUnit tests passed.

## SDK and publishing compatibility increment

- [x] S1: Publish option combinations are normalized and validated before command construction.
  CHECK: dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: invalid framework-dependent trimming, AOT conflicts, Android RunAOT without trimming, and Android D8/ProGuard combinations are rejected or normalized; Windows AOT emits the AOT property.
  EVIDENCE: 70 TUnit tests passed in Release (macOS osx-arm64, .NET 10.0.12). `PublishSettingsState` gained self-contained, single-file, trim, and Android tool inputs; `PublishSettingsPolicy` forces AOT to self-contained, clears single-file and ReadyToRun under AOT, drops trim when framework-dependent, and forces Android trimming when a linker is selected. `PublishCommandBuilder.ValidateOptions` rejects FDD+trim, AOT+R2R, AOT+single-file, AOT without self-contained, and AOT on unsupported targets before any argument is produced, and `BuildWindowsCommand` now emits `-p:PublishAot`. A per-platform matrix test asserts every default profile explicitly overrides project-level AOT/R2R/self-contained values.

- [x] S2: AOT and trim builds are free of application-owned linker and source-generation warnings, and the macOS bundle ships the native binary.
  CHECK: dotnet publish src/DotNetAppPublisher/DotNetAppPublisher.Desktop/DotNetAppPublisher.Desktop.csproj -c Release -f net10.0 -r osx-arm64 -p:PublishAot=true -p:PublishSingleFile=false -o "${TMPDIR:-/tmp}/dnap-aot-gate"
  EXPECT: publish succeeds; stale linker roots and reflection-based Google Play payload warnings are absent; the `.app` bundle executable is byte-identical to the publish-root executable. Third-party Avalonia warnings, if any, remain explicitly identified.
  EVIDENCE: NativeAOT publish produced a 25 MB root executable, zero managed assemblies, no managed entry assembly, and a byte-identical `.app` bundle executable (`cmp` clean), with no warnings. Trim publish reports only the third-party `Avalonia.DesignerSupport` IL2104 warning and zero application-owned IL warnings. The Google Play track payload moved from anonymous reflection to `GooglePlayJsonContext` source generation, and the linker descriptor now names the current `Features.Deployment.Android.AndroidDeviceInfo` type with the deleted `PlatformDefaults`/`PublishOptionRules` roots removed. 4-second launch smokes of the default self-contained (88 MB), ReadyToRun (142 MB), trimmed, and NativeAOT bundles produced no console output or errors.

- [x] S3: SDK and platform compatibility findings are documented with a stability recommendation.
  CHECK: test -f docs/dotnet-10-11-publishing-research.md
  EXPECT: research covers .NET 10/.NET 11 terminology, profiles, combinations, platform support, project findings, limitations, and official sources.
  EVIDENCE: `docs/dotnet-10-11-publishing-research.md` records the 2026-09-25 review, clarifies `dotnetup` versus the MAUI CoreCLR unification, maps deploy modes, platform, and combination matrices, lists 13 project findings with reproduction evidence, and links official Microsoft and Avalonia sources. `README.md` links the report and states the SDK pin.

- [x] S4: The repository remains pinned to the supported .NET 10 LTS line until .NET 11 GA and ecosystem validation.
  CHECK: test -f global.json && dotnet --version
  EXPECT: the selected SDK is a .NET 10 SDK; .NET 11 RC tooling is not selected accidentally.
  EVIDENCE: `global.json` pins `10.0.100` with `rollForward: latestFeature` and `allowPrerelease: false`; `dotnet --version` in the repository resolves 10.0.401. CI installs `10.0.x`, and the report documents the post-GA validation plan.

- [x] S5: The shared application code still starts in the browser host after the publish-policy and source-generation changes.
  CHECK: dotnet run --project src/DotNetAppPublisher/DotNetAppPublisher.Browser/DotNetAppPublisher.Browser.csproj --configuration Release --no-build --no-launch-profile --urls http://127.0.0.1:5235
  EXPECT: the Avalonia canvas resizes with the host, the splash closes, and the browser console stays free of warnings and errors.
  EVIDENCE: the Release browser host reported a `2000×1400` canvas at the default viewport (`scrollWidth == clientWidth == 1000`), `640×1120` after constraining the host to `320×560`, splash class `avalonia-splash splash-close` with `opacity: 0`, and zero console messages at warning level or above.

- [x] S6: CI verifies publish output, including the NativeAOT and macOS bundle invariants that a build cannot catch.
  CHECK: review of .github/workflows/ci.yml
  EXPECT: the Windows job publishes self-contained, Linux/macOS publish NativeAOT without `--no-restore`, the managed entry assembly assertion guards against a silent trim fallback, and the macOS job compares the bundle executable with the publish root.
  EVIDENCE: workflow updated with the three publish/verify steps. Cross-OS execution is deferred to GitHub Actions because only the macOS osx-arm64 host is available locally; the same commands were executed locally for the AOT, trim, ReadyToRun, and self-contained paths.


