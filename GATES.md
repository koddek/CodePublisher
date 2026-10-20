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

## Project version detection increment

Scope: `ProjectInspector.ReadVersion` scans only the csproj XML, so projects whose version lives in `Directory.Build.props`, `Directory.Version.props`, or any imported props show a blank version. Detect the effective version with MSBuild evaluation (`dotnet msbuild -getProperty:`), keeping the XML scan as fallback for hosts without dotnet; platform property precedence stays unchanged.

- [x] D1: Opening a project whose version lives in an imported props chain yields the effective version.
  CHECK: dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj --no-restore -- --disable-logo --progress off
  EXPECT: ProjectVersionResolver tests pass (evaluated props value wins, per-platform precedence, XML fallback when evaluation fails or the value is empty).
  EVIDENCE: 6 new tests pass in `ProjectVersionResolverTests`, including `ReadVersion_UsesEvaluatedValue_WhenVersionLivesInImportedProps` (no Version element in the csproj, evaluated map supplies 2.3.4) and Android precedence (ApplicationDisplayVersion/ApplicationVersion win over Version/FileVersion).
- [x] D2: The solution builds and the full suite passes with the resolver wired into metadata loading.
  CHECK: dotnet build DotNetAppPublisher.slnx --no-restore && dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj --no-restore -- --disable-logo --progress off
  EXPECT: zero warnings/errors; all tests pass.
  EVIDENCE: `Build succeeded. 0 Warning(s) 0 Error(s)`; TUnit 232/232 passed (226 prior + 6 new).
- [x] D3: The desktop app launches from a clean build tree (the duplicate AppStyles.axaml crash is gone).
  CHECK: launch DotNetAppPublisher.Desktop from the rebuilt Debug bundle and confirm the process stays up without output.
  EXPECT: process alive after several seconds; no ArgumentException about /Styles/AppStyles.axaml.
  EVIDENCE: crash reproduced before the cleanup on the stale Debug bundle, then all bin/obj trees removed and rebuilt: zero errors, process alive 9 s with empty log (twice). Root cause was stale incremental build state from the props-file relocation, not source. Bonus end-to-end proof of the user's exact case: the Desktop csproj declares no `<Version>` element, and the resolver's mechanism reports 0.6.0 from the imported `Directory.Version.props`.

## Central management increment

Scope: One repo-root `Directory.Packages.props` holds every package version (src + tests) and one repo-root `Directory.Build.props` imports `Directory.Version.props`. The four scattered copies are deleted. No version number changes.

- [x] C1: Restore resolves the identical package set from the single central file.
  CHECK: dotnet restore DotNetAppPublisher.slnx && dotnet list DotNetAppPublisher.slnx package --outdated
  EXPECT: the only updates offered are SkiaSharp 3.119.4 to 4.153.1; TUnit 1.75.0 and Avalonia 12.1.4 are resolved and absent from the update list.
  EVIDENCE: after `dotnet restore --force` against the root `Directory.Packages.props`, `--outdated` lists only SkiaSharp 3.119.4 → 4.153.1 and SkiaSharp.NativeAssets.Linux.NoDependencies — the same trailing pair as before the move. TUnit 1.75.0, Avalonia 12.1.4, Diagnostics 11.3.22 resolved with no updates offered.
- [x] C2: Every project reports the central version.
  CHECK: for p in src/DotNetAppPublisher/DotNetAppPublisher/DotNetAppPublisher.csproj src/DotNetAppPublisher/DotNetAppPublisher.Desktop/DotNetAppPublisher.Desktop.csproj src/DotNetAppPublisher/DotNetAppPublisher.Browser/DotNetAppPublisher.Browser.csproj tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj; do dotnet msbuild "$p" -nologo -v:q -getProperty:Version 2>/dev/null; done | grep -c "^0.6.0$"
  EXPECT: 4
  EVIDENCE: count is 4; the Desktop `obj/Debug` AssemblyInfo stamps `AssemblyVersion 0.6.0.0` / informational `0.6.0+4a142ee`.
- [x] C3: The solution builds and the full suite passes on the centralized layout.
  CHECK: dotnet build DotNetAppPublisher.slnx --no-restore && dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj --no-restore -- --disable-logo --progress off
  EXPECT: build has zero warnings/errors and all tests pass.
  EVIDENCE: `Build succeeded. 0 Warning(s) 0 Error(s)`; TUnit 226/226 passed. (One transient incremental-build failure with 3 errors appeared immediately after the props move; a rebuild from the same tree is clean — stale incremental state referencing the deleted imports.)

## AAB companion package increment

Scope: Checking AAB builds both APK and AAB in one publish; unchecking AAB builds the APK only. The uncommitted MainView.axaml edit left malformed XAML (AVLN1001, WrapPanel/StackPanel mismatch at line 522) and a duplicate `IncludeApk` checkbox; `ShowAndroidPackageFormatsTogether` is never raised, so its UI never refreshes.

- [x] P1: The solution builds again with the repaired Android package-format UI.
  CHECK: dotnet build DotNetAppPublisher.slnx --no-restore
  EXPECT: Build succeeded with zero errors.
  EVIDENCE: `Build succeeded. 0 Warning(s) 0 Error(s)` on macOS osx-arm64, .NET 10.0.12 (final run after all edits). Before the fix the same command failed with AVLN1001 (`WrapPanel`/`StackPanel` mismatch at MainView.axaml line 522) and, once the XAML parsed, 100 CS errors from the duplicated `IsPaired` and five orphan method-body fragments in MainViewModel.cs — both repaired.
- [x] P2: Policy pins the APK on for Android and treats AAB as the opt-in companion.
  CHECK: dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: all tests pass, including the updated and new package-format policy tests.
  EVIDENCE: 226/226 passed. `Normalize_AlwaysIncludesApk_WhenNoPackageFormatIsSelected` (both unchecked → APK on, AAB off) and `Normalize_SelectingAab_AlsoBuildsApk` (AAB checked → both on) in `PublishSettingsStateTests`; `PublishSettingsPolicy.Normalize` now forces `IncludeApk = true`.
- [x] P3: The desktop app still launches with the new Build-tab controls.
  CHECK: launch DotNetAppPublisher.Desktop briefly and confirm the process stays up without a startup exception.
  EXPECT: process alive after several seconds; no crash output.
  EVIDENCE: `bin/Debug/DotNetAppPublisher.app/Contents/MacOS/DotNetAppPublisher.Desktop` stayed alive for 9 s (pid reported ALIVE) with empty stdout/stderr, then terminated cleanly.
- [x] P4: Open publish folder opens the single folder that holds the published APK/AAB.
  CHECK: dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: FindBestPackageFolder tests pass (package folder is returned for nested artifacts, null when none).
  EVIDENCE: 226/226 passed, including `FindBestPackageFolder_ReturnsTheSingleFolderHoldingThePackages` and `FindBestPackageFolder_ReturnsNull_WhenNoPackagesExist`. `OpenPublishFolderAsync` resolves the folder through `ArtifactLocator.FindBestPackageFolder` for Android and opens that one folder.
- [x] P5: All NuGet packages are on the latest compatible versions and the solution builds on them.
  CHECK: dotnet restore DotNetAppPublisher.slnx && dotnet list DotNetAppPublisher.slnx package --outdated && dotnet build DotNetAppPublisher.slnx --no-restore
  EXPECT: only the Avalonia-pinned SkiaSharp/HarfBuzzSharp lines report as behind; build succeeds with zero errors.
  EVIDENCE: Avalonia family bumped 12.1.2 → 12.1.4, Avalonia.Diagnostics 11.3.21 → 11.3.22, TUnit 1.68.17 → 1.75.0; after restore+build (0 warnings/0 errors), `--outdated` reports only SkiaSharp 3.119.4 → 4.153.1 and SkiaSharp.NativeAssets.Linux.NoDependencies. Those stay pinned because the Avalonia.Skia 12.1.4 nuspec depends on SkiaSharp 3.119.4 / HarfBuzzSharp 8.3.1.3, and the props file records why. No breaking-change adaptations were needed — zero compile errors on first build after the bump.
- [x] P6: The full TUnit suite passes on the updated packages.
  CHECK: dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: all tests pass.
  EVIDENCE: 226/226 succeeded on TUnit 1.75.0, duration ~1 s; zero failed/skipped.

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

## Core library, option graph, and Play track increment

Scope: Move the UI-free domain layer (publish policy, deployment, the Google Play API client, the
shared models) out of the Avalonia app into a referenced class library; replace the ad-hoc
publish-option normalization with one declarative dependency graph that auto-enables required
companions, forbids mutually exclusive combinations, and cascades unchecks; and complete the
Google Play release-track workflow (promote, remove from track, halt, rollout change, closed-track
creation, version codes) so a release can move between the release, beta, and test lines without
the Play Console website.

- [x] L1: The domain layer lives in a UI-free class library that the app and the test suite both reference.
  CHECK: dotnet build DotNetAppPublisher.slnx --no-restore
  EXPECT: Build succeeded.
  EVIDENCE: `Build succeeded. 0 Warning(s) 0 Error(s)` in Debug and in Release. The new `src/DotNetAppPublisher/DotNetAppPublisher.Core` project holds Models, Services, and the whole `Features/` tree; the app project keeps the views, the view models, the Avalonia capture adapter, and the desktop interaction service. `DotNetAppPublisher.slnx` lists all five projects; the test project references Core directly.

- [x] L2: The core library carries no Avalonia reference and no UI types.
  CHECK: if grep -rln "Avalonia" src/DotNetAppPublisher/DotNetAppPublisher.Core --include=*.cs | grep .; then exit 1; else echo no-avalonia-in-core; fi
  EXPECT: no-avalonia-in-core
  EVIDENCE: `no-avalonia-in-core`. The two Avalonia leaks were removed at the source: `PublisherService` used `AssetLoader` for the macOS icon (now the `IAppIconProvider` seam, implemented by `AvaloniaAppIconProvider` in the app project) and forwarded six screenshot methods over `Window`/`Control`/`ScrollViewer` (now `IScreenshotService`, called by the view model). The environment-variable view model also stayed in the app project, because it needs the MVVM toolkit.

- [x] L3: One declarative dependency graph governs every publish option, and the resolver honours it on every toggle.
  CHECK: dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: all tests pass, including the per-platform option matrix.
  EVIDENCE: 288/288 passed (Release, macOS osx-arm64, .NET 10.0.13). `PublishOptionGraph` holds every relationship with its platform scope and reason; `PublishOptionResolver` applies the platform rules and the graph together until the result settles, and `MainViewModel` reports what it changed. `PublishOptionGraphTests` covers the resolution rules per platform plus four invariants on the graph itself.

- [x] L4: Required companions are enabled automatically, mutually exclusive options cannot both be on, and unchecking a prerequisite unchecks its dependents.
  CHECK: dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj --treenode-filter "/*/*/PublishOptionGraphTests/*" --disable-logo --progress off
  EXPECT: option-graph resolution tests pass.
  EVIDENCE: 20/20 passed. AOT turns on self-contained and trimming and turns off ReadyToRun and single-file; unchecking self-contained turns off AOT and trimming; ReadyToRun wins when the user turns it on against AOT; trimming turns on self-contained; single-file turns on the app host; Android RunAOT switches the link mode to SdkOnly and turning the linker off takes RunAOT and Profiled AOT with it; Profiled AOT turns on RunAOT; iOS BuildIpa turns on archiving and turning archiving off takes BuildIpa with it; an optional companion is offered rather than forced. Four more tests guard the graph itself: resolving a settled state changes nothing, resolving twice gives the same result, no rule is declared twice, and every rule that targets a choice option names the values it accepts. The last two caught two real defects while they were being written: a duplicated RunAOT rule and a suggestion rule that pointed at a choice option with no accepted values, which would have switched the source off on every pass.

- [x] L5: Google Play track management covers promote, remove-from-track, halt, rollout change, closed-track creation, and version codes.
  CHECK: dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: track management tests pass.
  EVIDENCE: `GooglePlayTrackCatalogTests` pins the track identifiers and the classification of every kind (production, open testing, internal testing, the legacy closed track, developer-named closed tracks, and the `prefix:name` form-factor tracks) plus the form-factor prefix parsing. `GooglePlayTrackPayloadTests` pins the four request shapes: an empty release array (how a release is removed from a track), a halted release that keeps its version codes, a staged rollout with its user fraction, and a full rollout without one. `GooglePlayTrackWorkflow` exposes promote, remove, halt, rollout change, closed-track creation, used version codes, and the Play app-signing certificate; `GooglePlayViewModel` wires them to the release workspace and the Google Play view.

- [x] L6: The Play app-signing certificate is compared with the local build so a sideloaded install is known to be Play-updatable or not.
  CHECK: dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: certificate compatibility tests pass.
  EVIDENCE: `AndroidPlayUpdateCheckTests` covers the matching case, the mismatching case, the colon-separated uppercase form the Play Console shows, the unknown case, and the wording of all three outcomes. `AndroidSigningInspector.ParseVersionCode` reads the installed version code, and the new **Inspect installed app** deploy action reports who installed the app, its version code, and its certificate, with the Play consequence spelled out.

- [x] L7: The solution still builds and the full suite passes after the library split.
  CHECK: dotnet build DotNetAppPublisher.slnx --no-restore && dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: zero errors and all tests pass.
  EVIDENCE: Debug and Release builds succeed with zero errors and zero warnings. 288/288 tests pass in Debug and in Release (`--configuration Release --no-build`). The NativeAOT publish of the desktop host produces no managed entry assembly and no application-owned linker warnings, and the published app stays alive with an empty log. The browser host still serves its page with the same pre-existing `libSkiaSharp` startup failure it has on the untouched baseline; that failure needs the emscripten workload, which is not installed here, and it is unrelated to this increment.

## Defects found and fixed in this increment

- [x] X1: Native AOT on a desktop target published a combination the .NET SDK rejects.
  CHECK: dotnet publish src/DotNetAppPublisher/DotNetAppPublisher.Desktop/DotNetAppPublisher.Desktop.csproj --configuration Release --self-contained true -p:PublishAot=true -p:PublishReadyToRun=false -p:PublishSingleFile=false -o "$TMPDIR/dnap-ci-aot"
  EXPECT: the publish succeeds and the managed entry assembly is absent.
  EVIDENCE: Before the fix the publish failed with `error : PublishTrimmed is implied by native compilation and cannot be disabled.` (dotnet/runtime `Microsoft.NETCore.Native.Publish.targets`), reproduced on a minimal project. The builder emitted `-p:PublishTrimmed=false` next to `-p:PublishAot=true`. The option graph now keeps trimming on under AOT. After the fix the publish succeeds with no managed entry assembly.
- [x] X2: The CI NativeAOT job passed the rejected combination, so it failed on every run.
  CHECK: dotnet publish src/DotNetAppPublisher/DotNetAppPublisher.Desktop/DotNetAppPublisher.Desktop.csproj --configuration Release --self-contained true -p:PublishAot=true -p:PublishReadyToRun=false -p:PublishSingleFile=false -o "$TMPDIR/dnap-ci-aot"
  EXPECT: the publish succeeds.
  EVIDENCE: The workflow passed `-p:PublishTrimmed=false -p:PublishAot=true`, which reproduces the SDK error above. The flag is removed; the publish now succeeds with no managed entry assembly.
- [x] X3: A whole feature slice was missing from version control.
  CHECK: git ls-files src/DotNetAppPublisher/DotNetAppPublisher/Features/GooglePlay/Releases/
  EXPECT: the release workflow is tracked.
  EVIDENCE: `Features/GooglePlay/Releases/` matched the `[Rr]eleases/` ignore rule, so `GooglePlayReleaseWorkflow.cs` was never committed. The baseline commit does not build: the app project cannot resolve `GooglePlayReleaseWorkflow`. A negation rule was added to `src/DotNetAppPublisher/.gitignore` and the file is now tracked.
- [x] X4: The install guidance named the wrong signing key.
  CHECK: dotnet run --project tests/DotNetAppPublisher.Tests/DotNetAppPublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: certificate compatibility tests pass.
  EVIDENCE: The install note told the user to sign with "the keystore registered in Play Console", which is the upload key. Google Play signs its downloads with the app signing key, so a build signed with the upload key can never be replaced by a Play Store update. The wording now names the app signing key and the signed universal APK from Play Console as the two ways to get a Play-updatable build.
