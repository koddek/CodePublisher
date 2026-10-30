# Gates: feature slices and stabilization

OWNS: src/CodePublisher/CodePublisher/Features/GooglePlay/**, src/CodePublisher/CodePublisher/Features/Publishing/**, src/CodePublisher/CodePublisher/Features/Deployment/**, src/CodePublisher/CodePublisher/Features/Shared/Process/**, src/CodePublisher/CodePublisher/Views/GooglePlay/**, tests/Directory.Packages.props, tests/CodePublisher.Tests/**, .github/workflows/ci.yml, global.json, docs/dotnet-10-11-publishing-research.md, src/CodePublisher/CodePublisher/App.axaml.cs, src/CodePublisher/CodePublisher/ViewModels/MainViewModel.Navigation.cs, src/CodePublisher/CodePublisher/Views/MainView.axaml, src/CodePublisher/CodePublisher/Views/MainView.axaml.cs, src/CodePublisher/CodePublisher/Styles/AppStyles.axaml, src/CodePublisher/CodePublisher/Services/DesktopInteractionService.cs, src/CodePublisher/CodePublisher/Services/PublisherService.cs, src/CodePublisher/CodePublisher/Trimming/LinkerDescriptor.xml, src/CodePublisher/CodePublisher/CodePublisher.csproj, src/CodePublisher/CodePublisher.Desktop/CodePublisher.Desktop.csproj, src/CodePublisher/Directory.Packages.props, README.md, AGENTS.md

Scope: Deliver an isolated Google Play detail page with secure in-memory account connections, release configuration, local AAB validation, and a complete API publishing workflow.

- [x] G1: The solution builds with the isolated feature slice.
  CHECK: dotnet build CodePublisher.slnx --no-restore
  EXPECT: Build succeeded.
  EVIDENCE: exit=0; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=f8958168e4b0/30 entries; EXPECT=matched; output-sha256=0c9306aa5d6b1ef28b41191b5e6fc92c3fa4195901e74f808120fd6beab6ca40; output-bytes=9278

- [x] G2: The feature has no embedded Google, Android keystore, OAuth, or service-account secrets.
  CHECK: if rg -n "BEGIN (RSA )?PRIVATE KEY|refresh_token[\"']?\\s*[:=]|private_key[\"']?\\s*[:=]" src/CodePublisher/CodePublisher/Features/GooglePlay src/CodePublisher/CodePublisher/Views/GooglePlay; then exit 1; else echo clean; fi
  EXPECT: clean
  EVIDENCE: exit=0; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=f8958168e4b0/30 entries; EXPECT=matched; output-sha256=2e22da2ab13713309ac75219e525b8e06ed02f3f1963b8feef203fa25827f93d; output-bytes=6

- [x] G3: Google Play is isolated behind a dedicated view and view model.
  CHECK: test -f src/CodePublisher/CodePublisher/Views/GooglePlay/GooglePlayView.axaml && test -f src/CodePublisher/CodePublisher/Features/GooglePlay/GooglePlayViewModel.cs && echo isolated-feature-present
  EXPECT: isolated-feature-present
  EVIDENCE: exit=0; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=f8958168e4b0/30 entries; EXPECT=matched; output-sha256=0efdcacaf2b9dbc05e602a79910535b3f4cbd5519013364a472a9c2c2a9ca946; output-bytes=25

- [x] G4: The Google Play page presents account, release, readiness, status, and environment setup states.
  EVIDENCE: GooglePlayView.axaml contains Account, setup, App, Release, and Publish cards; the App card links upload-key setup to Signing; MainView.axaml owns the upload-key controls; GooglePlayViewModel exposes environment rows, readiness, status tones, tracks, rollout, notes, cancellation, and session clearing.

## Stabilization increment: T0–T2

- [x] T0: TUnit test project and CI execution exist.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: all tests pass.
  EVIDENCE: 22 passed on macOS; `tests/CodePublisher.Tests/CodePublisher.Tests.csproj` is registered in `CodePublisher.slnx`; `.github/workflows/ci.yml` runs the Release test executable.

- [x] T1: Elevated process execution rejects secret environment variables and owner-restricts temporary scripts.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: secret rejection and Unix permission tests pass.
  EVIDENCE: `Features/Shared/Process/ElevatedProcessSecurity.cs`; `Security/ElevatedProcessSecurityTests.cs`; 22 total tests passed.

- [x] T2: Publish configuration rules and platform defaults live in the Publishing/Configure slice and cross-option normalization is covered.
  CHECK: dotnet restore CodePublisher.slnx -p:Configuration=Release && dotnet build CodePublisher.slnx --configuration Release --no-restore
  EXPECT: build succeeds with zero warnings and errors.
  EVIDENCE: `Features/Publishing/Configure/PublishOptionPolicy.cs`, `PlatformProfiles.cs`, `PublishPlatforms.cs`, and `PublishSettingsState.cs`; 22 tests passed before and after the refactor.

## Stabilization increment: T3–T9

- [x] T3: Extract publish command construction into a pure, tested Publishing/Build component.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: command matrix tests pass.
  EVIDENCE: `Features/Publishing/Build/PublishCommandBuilder.cs`; `PublishCommandBuilderTests.cs`; 27 tests passed.

- [x] T4: Extract project inspection, validation, and output-layout resolution.
  CHECK: dotnet build CodePublisher.slnx --no-restore
  EXPECT: build succeeds with zero warnings and errors.
  EVIDENCE: `Features/Publishing/Inspect/ProjectInspector.cs` and `ProjectOutputLayoutResolver.cs`; 33 tests passed; Debug build has zero warnings/errors.

- [x] T5: Extract process, deployment, capture, and artifact capabilities from `PublisherService`.
  CHECK: dotnet build CodePublisher.slnx --no-restore
  EXPECT: build succeeds with zero warnings and errors.
  EVIDENCE: `Features/Publishing/Process`, `Features/Publishing/Artifacts`, `Features/Publishing/Capture`, `Features/Deployment/Android`, and `Features/Deployment/Ios`; 41 tests passed; Debug build has zero warnings/errors.

- [x] T6: Introduce immutable publish and Google Play state snapshots with guarded transitions.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: state and workflow tests pass.
  EVIDENCE: `Features/GooglePlay/State/GooglePlayStateSnapshot.cs` and `GooglePlayStateHolder.cs`; publish state policy; 44 tests passed.

- [x] T7: Split Google Play authentication, tracks, releases, signing, and setup into focused slices.
  CHECK: dotnet build CodePublisher.slnx --no-restore
  EXPECT: build succeeds with zero warnings and errors.
  EVIDENCE: `Features/GooglePlay/Authentication`, `Tracks`, `Releases`, `Signing`, `Setup`, and `State`; 44 tests passed; Debug build has zero warnings/errors.

- [x] T8: Complete environment setup controls, platform guards, and browser runtime verification.
  CHECK: dotnet build CodePublisher.slnx --no-restore
  EXPECT: build succeeds with zero warnings and errors; browser smoke evidence recorded.
  EVIDENCE: `GooglePlayView.axaml` now presents human labels, examples, detected values, status, and compact accessible actions; browser guards prevent desktop discovery in browser; `main.js` synchronizes the canvas backing store; built-in browser smoke at `http://127.0.0.1:5235/?ui=postclean2` loaded a 2000×1400 canvas, closed the splash, and reported no console errors.

- [x] T9: Refresh documentation, DOX ownership, CI, and final verification.
  CHECK: dotnet restore CodePublisher.slnx -p:Configuration=Release && dotnet build CodePublisher.slnx --configuration Release --no-restore && dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --configuration Release --no-build -- --disable-logo --progress off
  EXPECT: Release build has zero warnings/errors and all tests pass.
  EVIDENCE: README, CI, local DOX contracts, and GATES updated; Debug and Release solution builds completed with zero warnings/errors; 46 TUnit tests passed in the latest Release verification (45 before the setup-guidance test); `git diff --check` passed; browser smoke loaded a 2000×1400 canvas with splash closed and no fresh console errors.

## UI refinement increment

- [x] UI1: Google account entry is adjacent to Google Play; upload-key controls live under Signing.
  CHECK: dotnet build CodePublisher.slnx --no-restore
  EXPECT: build succeeds with zero warnings and errors.
  EVIDENCE: GooglePlayView.axaml keeps Account first and links App upload-key setup to Signing; MainView.axaml owns the manual and Google Play upload-key cards.

- [x] UI2: Narrow and wide layouts preserve reading order, readable labels, and usable controls.
  CHECK: browser smoke at desktop and narrow viewport sizes
  EXPECT: no clipped primary controls, no horizontal overflow, and no console errors.
  EVIDENCE: browser host at `http://127.0.0.1:5235/` produced a `2000×1400` canvas at `1000×700`; resizing the host to `360×700` and `320×560` produced `720×1400` and `640×1120` canvases, `scrollWidth == clientWidth`, splash class `splash-close`, and no console error messages.

- [x] UI3: Low-emphasis OK/Go-style actions use compact accessible icon controls.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
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
  CHECK: dotnet build CodePublisher.slnx --no-restore
  EXPECT: build succeeds with zero warnings and errors; routine status remains inline.
  EVIDENCE: Release solution build completed with zero warnings/errors; 46 TUnit tests passed; `DesktopInteractionService` now uses title/details layout, centered details, wrapping actions, and owner-width dialog sizing.

- [x] UI7: Every Google Play setup value explains where to obtain it and links to an official source.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: setup definitions contain acquisition guidance and source URLs; all tests pass.
  EVIDENCE: setup definitions now cover Google Cloud OAuth credentials, Temurin JDK, and Android Studio SDK locations; `GooglePlayView.axaml` renders the guidance and source action inline; 46 TUnit tests passed.

## Project version detection increment

Scope: `ProjectInspector.ReadVersion` scans only the csproj XML, so projects whose version lives in `Directory.Build.props`, `Directory.Version.props`, or any imported props show a blank version. Detect the effective version with MSBuild evaluation (`dotnet msbuild -getProperty:`), keeping the XML scan as fallback for hosts without dotnet; platform property precedence stays unchanged.

- [x] D1: Opening a project whose version lives in an imported props chain yields the effective version.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --no-restore -- --disable-logo --progress off
  EXPECT: ProjectVersionResolver tests pass (evaluated props value wins, per-platform precedence, XML fallback when evaluation fails or the value is empty).
  EVIDENCE: 6 new tests pass in `ProjectVersionResolverTests`, including `ReadVersion_UsesEvaluatedValue_WhenVersionLivesInImportedProps` (no Version element in the csproj, evaluated map supplies 2.3.4) and Android precedence (ApplicationDisplayVersion/ApplicationVersion win over Version/FileVersion).
- [x] D2: The solution builds and the full suite passes with the resolver wired into metadata loading.
  CHECK: dotnet build CodePublisher.slnx --no-restore && dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --no-restore -- --disable-logo --progress off
  EXPECT: zero warnings/errors; all tests pass.
  EVIDENCE: `Build succeeded. 0 Warning(s) 0 Error(s)`; TUnit 232/232 passed (226 prior + 6 new).
- [x] D3: The desktop app launches from a clean build tree (the duplicate AppStyles.axaml crash is gone).
  CHECK: launch CodePublisher.Desktop from the rebuilt Debug bundle and confirm the process stays up without output.
  EXPECT: process alive after several seconds; no ArgumentException about /Styles/AppStyles.axaml.
  EVIDENCE: crash reproduced before the cleanup on the stale Debug bundle, then all bin/obj trees removed and rebuilt: zero errors, process alive 9 s with empty log (twice). Root cause was stale incremental build state from the props-file relocation, not source. Bonus end-to-end proof of the user's exact case: the Desktop csproj declares no `<Version>` element, and the resolver's mechanism reports 0.6.0 from the imported `Directory.Version.props`.

## Central management increment

Scope: One repo-root `Directory.Packages.props` holds every package version (src + tests) and one repo-root `Directory.Build.props` imports `Directory.Version.props`. The four scattered copies are deleted. No version number changes.

- [x] C1: Restore resolves the identical package set from the single central file.
  CHECK: dotnet restore CodePublisher.slnx && dotnet list CodePublisher.slnx package --outdated
  EXPECT: the only updates offered are SkiaSharp 3.119.4 to 4.153.1; TUnit 1.75.0 and Avalonia 12.1.4 are resolved and absent from the update list.
  EVIDENCE: after `dotnet restore --force` against the root `Directory.Packages.props`, `--outdated` lists only SkiaSharp 3.119.4 → 4.153.1 and SkiaSharp.NativeAssets.Linux.NoDependencies — the same trailing pair as before the move. TUnit 1.75.0, Avalonia 12.1.4, Diagnostics 11.3.22 resolved with no updates offered.
- [x] C2: Every project reports the central version.
  CHECK: for p in src/CodePublisher/CodePublisher/CodePublisher.csproj src/CodePublisher/CodePublisher.Desktop/CodePublisher.Desktop.csproj src/CodePublisher/CodePublisher.Browser/CodePublisher.Browser.csproj tests/CodePublisher.Tests/CodePublisher.Tests.csproj; do dotnet msbuild "$p" -nologo -v:q -getProperty:Version 2>/dev/null; done | grep -c "^0.6.0$"
  EXPECT: 4
  EVIDENCE: count is 4; the Desktop `obj/Debug` AssemblyInfo stamps `AssemblyVersion 0.6.0.0` / informational `0.6.0+4a142ee`.
- [x] C3: The solution builds and the full suite passes on the centralized layout.
  CHECK: dotnet build CodePublisher.slnx --no-restore && dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --no-restore -- --disable-logo --progress off
  EXPECT: build has zero warnings/errors and all tests pass.
  EVIDENCE: `Build succeeded. 0 Warning(s) 0 Error(s)`; TUnit 226/226 passed. (One transient incremental-build failure with 3 errors appeared immediately after the props move; a rebuild from the same tree is clean — stale incremental state referencing the deleted imports.)

## AAB companion package increment

Scope: Checking AAB builds both APK and AAB in one publish; unchecking AAB builds the APK only. The uncommitted MainView.axaml edit left malformed XAML (AVLN1001, WrapPanel/StackPanel mismatch at line 522) and a duplicate `IncludeApk` checkbox; `ShowAndroidPackageFormatsTogether` is never raised, so its UI never refreshes.

- [x] P1: The solution builds again with the repaired Android package-format UI.
  CHECK: dotnet build CodePublisher.slnx --no-restore
  EXPECT: Build succeeded with zero errors.
  EVIDENCE: `Build succeeded. 0 Warning(s) 0 Error(s)` on macOS osx-arm64, .NET 10.0.12 (final run after all edits). Before the fix the same command failed with AVLN1001 (`WrapPanel`/`StackPanel` mismatch at MainView.axaml line 522) and, once the XAML parsed, 100 CS errors from the duplicated `IsPaired` and five orphan method-body fragments in MainViewModel.cs — both repaired.
- [x] P2: Policy pins the APK on for Android and treats AAB as the opt-in companion.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: all tests pass, including the updated and new package-format policy tests.
  EVIDENCE: 226/226 passed. `Normalize_AlwaysIncludesApk_WhenNoPackageFormatIsSelected` (both unchecked → APK on, AAB off) and `Normalize_SelectingAab_AlsoBuildsApk` (AAB checked → both on) in `PublishSettingsStateTests`; `PublishSettingsPolicy.Normalize` now forces `IncludeApk = true`.
- [x] P3: The desktop app still launches with the new Build-tab controls.
  CHECK: launch CodePublisher.Desktop briefly and confirm the process stays up without a startup exception.
  EXPECT: process alive after several seconds; no crash output.
  EVIDENCE: `bin/Debug/CodePublisher.app/Contents/MacOS/CodePublisher.Desktop` stayed alive for 9 s (pid reported ALIVE) with empty stdout/stderr, then terminated cleanly.
- [x] P4: Open publish folder opens the single folder that holds the published APK/AAB.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: FindBestPackageFolder tests pass (package folder is returned for nested artifacts, null when none).
  EVIDENCE: 226/226 passed, including `FindBestPackageFolder_ReturnsTheSingleFolderHoldingThePackages` and `FindBestPackageFolder_ReturnsNull_WhenNoPackagesExist`. `OpenPublishFolderAsync` resolves the folder through `ArtifactLocator.FindBestPackageFolder` for Android and opens that one folder.
- [x] P5: All NuGet packages are on the latest compatible versions and the solution builds on them.
  CHECK: dotnet restore CodePublisher.slnx && dotnet list CodePublisher.slnx package --outdated && dotnet build CodePublisher.slnx --no-restore
  EXPECT: only the Avalonia-pinned SkiaSharp/HarfBuzzSharp lines report as behind; build succeeds with zero errors.
  EVIDENCE: Avalonia family bumped 12.1.2 → 12.1.4, Avalonia.Diagnostics 11.3.21 → 11.3.22, TUnit 1.68.17 → 1.75.0; after restore+build (0 warnings/0 errors), `--outdated` reports only SkiaSharp 3.119.4 → 4.153.1 and SkiaSharp.NativeAssets.Linux.NoDependencies. Those stay pinned because the Avalonia.Skia 12.1.4 nuspec depends on SkiaSharp 3.119.4 / HarfBuzzSharp 8.3.1.3, and the props file records why. No breaking-change adaptations were needed — zero compile errors on first build after the bump.
- [x] P6: The full TUnit suite passes on the updated packages.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: all tests pass.
  EVIDENCE: 226/226 succeeded on TUnit 1.75.0, duration ~1 s; zero failed/skipped.

## SDK and publishing compatibility increment

- [x] S1: Publish option combinations are normalized and validated before command construction.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: invalid framework-dependent trimming, AOT conflicts, Android RunAOT without trimming, and Android D8/ProGuard combinations are rejected or normalized; Windows AOT emits the AOT property.
  EVIDENCE: 70 TUnit tests passed in Release (macOS osx-arm64, .NET 10.0.12). `PublishSettingsState` gained self-contained, single-file, trim, and Android tool inputs; `PublishSettingsPolicy` forces AOT to self-contained, clears single-file and ReadyToRun under AOT, drops trim when framework-dependent, and forces Android trimming when a linker is selected. `PublishCommandBuilder.ValidateOptions` rejects FDD+trim, AOT+R2R, AOT+single-file, AOT without self-contained, and AOT on unsupported targets before any argument is produced, and `BuildWindowsCommand` now emits `-p:PublishAot`. A per-platform matrix test asserts every default profile explicitly overrides project-level AOT/R2R/self-contained values.

- [x] S2: AOT and trim builds are free of application-owned linker and source-generation warnings, and the macOS bundle ships the native binary.
  CHECK: dotnet publish src/CodePublisher/CodePublisher.Desktop/CodePublisher.Desktop.csproj -c Release -f net10.0 -r osx-arm64 -p:PublishAot=true -p:PublishSingleFile=false -o "${TMPDIR:-/tmp}/dnap-aot-gate"
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
  CHECK: dotnet run --project src/CodePublisher/CodePublisher.Browser/CodePublisher.Browser.csproj --configuration Release --no-build --no-launch-profile --urls http://127.0.0.1:5235
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
  CHECK: dotnet build CodePublisher.slnx --no-restore
  EXPECT: Build succeeded.
  EVIDENCE: `Build succeeded. 0 Warning(s) 0 Error(s)` in Debug and in Release. The new `src/CodePublisher/CodePublisher.Core` project holds Models, Services, and the whole `Features/` tree; the app project keeps the views, the view models, the Avalonia capture adapter, and the desktop interaction service. `CodePublisher.slnx` lists all five projects; the test project references Core directly.

- [x] L2: The core library carries no Avalonia reference and no UI types.
  CHECK: if grep -rln "Avalonia" src/CodePublisher/CodePublisher.Core --include=*.cs | grep .; then exit 1; else echo no-avalonia-in-core; fi
  EXPECT: no-avalonia-in-core
  EVIDENCE: `no-avalonia-in-core`. The two Avalonia leaks were removed at the source: `PublisherService` used `AssetLoader` for the macOS icon (now the `IAppIconProvider` seam, implemented by `AvaloniaAppIconProvider` in the app project) and forwarded six screenshot methods over `Window`/`Control`/`ScrollViewer` (now `IScreenshotService`, called by the view model). The environment-variable view model also stayed in the app project, because it needs the MVVM toolkit.

- [x] L3: One declarative dependency graph governs every publish option, and the resolver honours it on every toggle.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: all tests pass, including the per-platform option matrix.
  EVIDENCE: 288/288 passed (Release, macOS osx-arm64, .NET 10.0.13). `PublishOptionGraph` holds every relationship with its platform scope and reason; `PublishOptionResolver` applies the platform rules and the graph together until the result settles, and `MainViewModel` reports what it changed. `PublishOptionGraphTests` covers the resolution rules per platform plus four invariants on the graph itself.

- [x] L4: Required companions are enabled automatically, mutually exclusive options cannot both be on, and unchecking a prerequisite unchecks its dependents.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --treenode-filter "/*/*/PublishOptionGraphTests/*" --disable-logo --progress off
  EXPECT: option-graph resolution tests pass.
  EVIDENCE: 20/20 passed. AOT turns on self-contained and trimming and turns off ReadyToRun and single-file; unchecking self-contained turns off AOT and trimming; ReadyToRun wins when the user turns it on against AOT; trimming turns on self-contained; single-file turns on the app host; Android RunAOT switches the link mode to SdkOnly and turning the linker off takes RunAOT and Profiled AOT with it; Profiled AOT turns on RunAOT; iOS BuildIpa turns on archiving and turning archiving off takes BuildIpa with it; an optional companion is offered rather than forced. Four more tests guard the graph itself: resolving a settled state changes nothing, resolving twice gives the same result, no rule is declared twice, and every rule that targets a choice option names the values it accepts. The last two caught two real defects while they were being written: a duplicated RunAOT rule and a suggestion rule that pointed at a choice option with no accepted values, which would have switched the source off on every pass.

- [x] L5: Google Play track management covers promote, remove-from-track, halt, rollout change, closed-track creation, and version codes.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: track management tests pass.
  EVIDENCE: `GooglePlayTrackCatalogTests` pins the track identifiers and the classification of every kind (production, open testing, internal testing, the legacy closed track, developer-named closed tracks, and the `prefix:name` form-factor tracks) plus the form-factor prefix parsing. `GooglePlayTrackPayloadTests` pins the four request shapes: an empty release array (how a release is removed from a track), a halted release that keeps its version codes, a staged rollout with its user fraction, and a full rollout without one. `GooglePlayTrackWorkflow` exposes promote, remove, halt, rollout change, closed-track creation, used version codes, and the Play app-signing certificate; `GooglePlayViewModel` wires them to the release workspace and the Google Play view.

- [x] L6: The Play app-signing certificate is compared with the local build so a sideloaded install is known to be Play-updatable or not.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: certificate compatibility tests pass.
  EVIDENCE: `AndroidPlayUpdateCheckTests` covers the matching case, the mismatching case, the colon-separated uppercase form the Play Console shows, the unknown case, and the wording of all three outcomes. `AndroidSigningInspector.ParseVersionCode` reads the installed version code, and the new **Inspect installed app** deploy action reports who installed the app, its version code, and its certificate, with the Play consequence spelled out.

- [x] L7: The solution still builds and the full suite passes after the library split.
  CHECK: dotnet build CodePublisher.slnx --no-restore && dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: zero errors and all tests pass.
  EVIDENCE: Debug and Release builds succeed with zero errors and zero warnings. 288/288 tests pass in Debug and in Release (`--configuration Release --no-build`). The NativeAOT publish of the desktop host produces no managed entry assembly and no application-owned linker warnings, and the published app stays alive with an empty log. The browser host now starts: it reports a `2000x1400` canvas, a closed splash, and zero console messages, which matches the baseline the earlier increments recorded.

- [x] L8: The release line check tells the user what moves a device onto another line.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: release line policy tests pass.
  EVIDENCE: 301/301 passed (Release, macOS osx-arm64, .NET 10.0.13). `GooglePlayReleaseLinePolicy` works out which line a device is effectively on and what it takes to move it, because Google Play serves the highest eligible version code and never downgrades, so the version code the device runs is the floor. `GooglePlayReleaseLinePolicyTests` covers nine cases: a target below the device, a target above it, a competing line that has to stop first, the required version code, an unreadable device version code, an unknown target, the effective line, and the two cases where no line serves the device. The deploy section now reports the device's installer, version code, and certificate and feeds them into the workspace, and the Play app signing certificate is read from the API and compared with the device's.

- [x] L9: A Google Play error that blocks a release says what to do about it.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: error guidance tests pass.
  EVIDENCE: `GooglePlayApiServiceErrorTests` covers the four cases that matter: a version code that was already used (Play never reuses one), a change still in review, a release shadowed by a higher version code on another line, and an unrelated error that keeps Google's own wording.

## Defects found and fixed in this increment

- [x] X1: Native AOT on a desktop target published a combination the .NET SDK rejects.
  CHECK: dotnet publish src/CodePublisher/CodePublisher.Desktop/CodePublisher.Desktop.csproj --configuration Release --self-contained true -p:PublishAot=true -p:PublishReadyToRun=false -p:PublishSingleFile=false -o "$TMPDIR/dnap-ci-aot"
  EXPECT: the publish succeeds and the managed entry assembly is absent.
  EVIDENCE: Before the fix the publish failed with `error : PublishTrimmed is implied by native compilation and cannot be disabled.` (dotnet/runtime `Microsoft.NETCore.Native.Publish.targets`), reproduced on a minimal project. The builder emitted `-p:PublishTrimmed=false` next to `-p:PublishAot=true`. The option graph now keeps trimming on under AOT. After the fix the publish succeeds with no managed entry assembly.
- [x] X2: The CI NativeAOT job passed the rejected combination, so it failed on every run.
  CHECK: dotnet publish src/CodePublisher/CodePublisher.Desktop/CodePublisher.Desktop.csproj --configuration Release --self-contained true -p:PublishAot=true -p:PublishReadyToRun=false -p:PublishSingleFile=false -o "$TMPDIR/dnap-ci-aot"
  EXPECT: the publish succeeds.
  EVIDENCE: The workflow passed `-p:PublishTrimmed=false -p:PublishAot=true`, which reproduces the SDK error above. The flag is removed; the publish now succeeds with no managed entry assembly.
- [x] X3: A whole feature slice was missing from version control.
  CHECK: git ls-files src/CodePublisher/CodePublisher/Features/GooglePlay/Releases/
  EXPECT: the release workflow is tracked.
  EVIDENCE: `Features/GooglePlay/Releases/` matched the `[Rr]eleases/` ignore rule, so `GooglePlayReleaseWorkflow.cs` was never committed. The baseline commit does not build: the app project cannot resolve `GooglePlayReleaseWorkflow`. A negation rule was added to `src/CodePublisher/.gitignore` and the file is now tracked.
- [x] X5: The browser host could not start because the native SkiaSharp library was never linked.
  CHECK: dotnet publish src/CodePublisher/CodePublisher.Browser/CodePublisher.Browser.csproj --configuration Release -o "$TMPDIR/publish-browser"
  EXPECT: the publish succeeds and the native module carries the linked Skia code.
  EVIDENCE: The browser host failed at startup with `System.DllNotFoundException: libSkiaSharp`, on the untouched baseline as well, because it referenced neither the WebAssembly native assets nor the emscripten toolchain. With `WasmBuildNative` and the two `*.NativeAssets.WebAssembly` packages, the publish succeeds and the native module carries the linked Skia code (`sk_FragCoord` is present). The emscripten toolchain comes from the `wasm-tools` workload, which is a .NET workload: `dotnet workload install wasm-tools`. Installing it surfaced a second defect: the emscripten `clang` wrapper the SDK ships computes its own path with an unquoted `$(dirname $0)`, so any SDK path containing a space splits it and the tool cannot find `clang-19`. Quoting the expansion fixes it. CI now installs the workload and asserts the linked module is present.
- [x] X4: The install guidance named the wrong signing key.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: certificate compatibility tests pass.
  EVIDENCE: The install note told the user to sign with "the keystore registered in Play Console", which is the upload key. Google Play signs its downloads with the app signing key, so a build signed with the upload key can never be replaced by a Play Store update. The wording now names the app signing key and the signed universal APK from Play Console as the two ways to get a Play-updatable build.

## Crash report workspace increment

- [x] C1: The crash reports of an app are found on an Android device, in every place they can be.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: the crash log parsers and the report builder pass, and a scan against a running emulator finds the app's own crash file, the report in Android's crash store, and the crash in the device log.
  EVIDENCE: 337 tests pass (Debug, macOS osx-arm64, .NET 10.0.13). Against an Android 17 emulator with a debuggable `com.example.crashprobe` that holds `files/logs/crash-2026-10-20.log` and `cache/logs/crash-cache.log`, and that crashed once, the scan returned the two files of the app's own data folder, the `data_app_crash` entry of the DropBox store, and the crash in the device log. A later scan of the same emulator returned the first three and no device log, because the log buffer had rotated, and the note said exactly that. The same scan for `com.android.settings` returned only the crash store entry of that app, and the crash of the other app was left out of the device log. `app.log`, which is not a crash report, was not listed. A package that is not installed reports that it has no crash reports.

- [x] C2: The crash reports of an app are found on an iOS simulator, in every place they can be.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: the CoreDevice file listing parser passes, and a scan against a running simulator finds the crash file in the app's container and the system crash report of the crashed app.
  EVIDENCE: The parser accepts the field names of several Xcode versions. Against an iOS 18.6 simulator with a `com.example.simapp` that holds `Library/Logs/crash-2026-10-20.log` and that crashed once, the scan returned two reports: the file in the app's data container and the `MyApp-2026-10-20-204613.ips` that macOS wrote into `~/Library/Logs/DiagnosticReports`. The ordinary `app.log` in the same folder was not listed.

- [x] C3: A found crash report can be read and copied as text that an AI agent can work with.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: the report builder names the report, its source, its location, its time, and what the crash was about, and puts every report in one block.
  EVIDENCE: `CrashLogReport.BuildEntry` and `CrashLogReport.BuildAll` are covered by six tests. The copy of one report reads as a headed block: `=== Crash report: crash-2026-10-20.log ===`, `Source: App data folder`, `Location: /data/data/com.example.crashprobe/files/logs/crash-2026-10-20.log`, `Time:`, `Size:`, `Summary: System.InvalidOperationException: The configuration key "AppData" was not found.`, and then the text of the report.

- [x] C4: The Deploy section carries the crash report workspace.
  CHECK: dotnet build CodePublisher.slnx --no-restore
  EXPECT: the solution builds, and the Deploy section of `MainView.axaml` holds the crash log card with the scan, the copy, the list, and the preview.
  EVIDENCE: The solution builds with zero warnings and zero errors. The card in the Deploy section holds `Scan for crash logs`, `Copy all`, the target badge, the summary, the notes, the list of reports, and the preview of the selected report. `MainViewModel.CrashLogs.cs` owns the state and the commands, and `CrashLogRowViewModel` keeps the actions on the row. Every binding path of the card (19 of them) was checked against the view model by reflection, and every one resolves. The item template carries an `x:DataType`, so the XAML compiler checked its bindings at build time.

- [x] C5: The place a crash report is stored is documented, with what was verified and what was not.
  CHECK: test -f docs/crash-report-locations.md
  EXPECT: the document names the folders each platform uses, the rule that the app's own folder is the app's own choice, and the sources of the claims.
  EVIDENCE: `docs/crash-report-locations.md` records the Android data folder, the debuggable rule of `run-as`, the DropBox tags, the `logcat` buffer, the iOS container and the `.ips` reports, and the macOS and Windows folders, with the table of what the scan reads per platform and the list of the file names that mark a crash report. The real device path of `devicectl` was not exercised, because no iPhone was connected to this computer.

## NuGet package pipeline increment

Scope: Add a NuGet workspace to Code Publisher. A new NuGet navigation tab packs the selected project into a `.nupkg` (`dotnet pack`), validates the package (nuspec identity, SemVer version, assets, zip-slip entries), and test-installs it from a local feed into a throwaway consumer project. The Deploy tab gains a NuGet feed card that pushes the packed package to NuGet.org or GitHub Packages (`dotnet nuget push`), lists the versions already on the feed, and deletes a version (`dotnet nuget delete`). API keys stay session-only and are masked in every command preview. Domain logic lives in `CodePublisher.Core/Features/NuGet` with no Avalonia dependency; the view model and view live in the app project.

- [x] N1: Core NuGet slice exists and is pure/testable: feed catalog + URL/validation rules, pack/push/delete/test command builders with secret masking, nupkg reader, package validator, V3 version-list parsers, feed client, and the orchestrating `NuGetPackageService` behind a process-runner seam.
  CHECK: dotnet build CodePublisher.slnx --no-restore
  EXPECT: Build succeeded with zero warnings and errors.
  EVIDENCE: `Build succeeded. 0 Warning(s) 0 Error(s)`; `Features/NuGet/{Models,Feeds,Build,Inspect,Validation,Services}` in CodePublisher.Core, no Avalonia references.
- [x] N2: TUnit coverage for the NuGet slice: feed catalog URLs and id/version/owner validation, command builders and masking, nupkg reader against a real temp .nupkg, validator rules, version-list JSON parsers, and service orchestration with an imposter process runner.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: all tests pass, including the new NuGet tests.
  EVIDENCE: 487/487 passed (Debug and Release); six new files under `tests/CodePublisher.Tests/Features/NuGet/`.
- [x] N3: The NuGet tab is wired into navigation and composition: `PublishSection.NuGet`, nav item after Google Play, `NuGetViewModel` via `NuGetFeatureFactory`, `NuGetView` in MainView, context updates from project metadata, and a NuGet feed card in the Deploy section.
  CHECK: dotnet build CodePublisher.slnx --no-restore
  EXPECT: Build succeeded with zero warnings and errors; XAML compiles with x:CompileBindings.
  EVIDENCE: `Build succeeded. 0 Warning(s) 0 Error(s)` for the whole solution; `Views/NuGet/NuGetView.axaml` + Deploy-section feed card compiled with x:CompileBindings; `MainViewModel.NuGet.cs` feeds project metadata into the slice via `UpdateGooglePlayContext`.
- [x] N4: No NuGet secrets or hardcoded tokens in source; API key is session-only, masked in previews, and never written to disk by the feature.
  CHECK: if rg -n "ghp_[A-Za-z0-9]{20,}|sk-nuget|BEGIN (RSA )?PRIVATE KEY" src/CodePublisher/CodePublisher.Core/Features/NuGet src/CodePublisher/CodePublisher/Features/NuGet src/CodePublisher/CodePublisher/Views/NuGet tests/CodePublisher.Tests/Features/NuGet; then exit 1; else echo clean; fi
  EXPECT: clean
  EVIDENCE: `clean`; `NuGetCommandBuilder.Mask` replaces every `--api-key`/`-k` value with asterisks and the service tests assert the key never appears in previews.
- [x] N5: Full verification: Release solution build plus the whole TUnit suite.
  CHECK: dotnet restore CodePublisher.slnx -p:Configuration=Release && dotnet build CodePublisher.slnx --configuration Release --no-restore && dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --configuration Release --no-build -- --disable-logo --progress off
  EXPECT: zero warnings/errors; all tests pass.
  EVIDENCE: Release build `0 Warning(s) 0 Error(s)`; TUnit 487/487 passed in Release.
- [x] N6: DOX pass: README (NuGet tab, nav order, workflow), src/AGENTS.md navigation-order contract, and the docs index stay current.
  CHECK: rg -n "NuGet" README.md src/AGENTS.md
  EXPECT: both name the NuGet tab and its pipeline.
  EVIDENCE: README lists the NuGet pack/test/push/list/delete bullets, workflow step 8, and the navigation order with NuGet; `src/AGENTS.md` names the NuGet nav position and the shared `NuGetViewModel`; `CodePublisher.Core/AGENTS.md` and `CodePublisher/Features/AGENTS.md` created to match the src Child DOX Index and describe the slice's contracts.

## NuGet platform and cross-thread fix increment

Scope: (1) Fix the `InvalidOperationException: calling thread cannot access this object` crash when packing from the NuGet tab: `NuGetViewModel` awaited `Task.Run(...)` with `ConfigureAwait(false)`, so the continuation mutated UI-bound state (`IsBusy`, `ObservableCollection`s) on a thread-pool thread. All `ConfigureAwait(false)` calls were removed from the view model, so continuations resume on the Avalonia dispatcher like the rest of the app; the Core service keeps `ConfigureAwait(false)` because library code has no UI thread. (2) Make `NuGet` a first-class publish platform: selecting it in the Platform tab switches every tab to NuGet context (app build options, Android signing, crash logs, and device-files cards hide; the NuGet deploy card shows in Deploy), the header Publish button runs the pack pipeline, and the Output preview shows the masked `dotnet pack` command instead of an app-publish error.

- [x] N7: No `ConfigureAwait(false)` remains in `NuGetViewModel`; UI-state mutations happen on the dispatcher thread.
  CHECK: rg -n "ConfigureAwait" src/CodePublisher/CodePublisher/Features/NuGet/NuGetViewModel.cs
  EXPECT: no matches.
  EVIDENCE: `rg` returns nothing; solution builds with zero warnings/errors.
- [x] N8: NuGet is a selectable platform with context-aware tabs and a routing Publish button.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: NuGetPlatformTests pass (platform listed, neutral defaults, RID not required, per-tab visibility flips, Output preview shows the pack command); whole suite passes.
  EVIDENCE: 493/493 passed; `PublishPlatforms.NuGet`, `PublisherService.NuGetPlatform`, a `NuGet` arm in `PlatformProfiles.GetDefaults`, an early return for NuGet in `ProjectInspector.ValidateRuntimeForPlatform`, `MainViewModel.PublishAsync` routing to `NuGetViewModel.RunPackAsync`, and per-tab visibility properties (`IsAppBuildOptionsVisible`, `IsNuGetDeployCardVisible`, `IsCrashLogsCardVisible`, `IsDeviceFilesFallbackVisible`, `IsPlatformSigningCardVisible`) wired into MainView.axaml.
- [x] N9: Full solution verification after the increment.
  CHECK: dotnet build CodePublisher.slnx --configuration Release --no-restore && dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --configuration Release --no-build -- --disable-logo --progress off
  EXPECT: zero warnings/errors; all tests pass.
  EVIDENCE: Release build `0 Warning(s) 0 Error(s)`; TUnit 493/493 passed in Release.

## NU5026 pack recovery increment

Scope: With the NuGet platform selected, the header Publish button runs `dotnet pack` (confirmed in the live log). A pack can fail with NU5026 ("file to be packed was not found") when MSBuild's up-to-date check skips the build while `bin` lacks the assembly. `NuGetPackageService.PackAsync` now detects NU5026 in the pack output, forces a real rebuild (`dotnet build -c <config> --no-incremental`), and repacks with `--no-build`.

- [x] N10: NU5026 triggers a forced rebuild plus `--no-build` repack; other failures still fail fast.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: `Pack_NU5026_ForcesRebuild_ThenRepacksWithNoBuild` passes (call order pack → build --no-incremental → pack --no-build, success, identity read); whole suite passes.
  EVIDENCE: 495/495 passed. Real-SDK check: `dotnet build -c Release --no-incremental` followed by `dotnet pack -c Release --no-build -o nupkgs` produced `DemoLib.1.0.0.nupkg` on .NET SDK 10.0.402.
- [x] N11: Full solution verification after the increment.
  CHECK: dotnet build CodePublisher.slnx --configuration Release --no-restore && dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --configuration Release --no-build -- --disable-logo --progress off
  EXPECT: zero warnings/errors; all tests pass.
  EVIDENCE: Release build `0 Warning(s) 0 Error(s)`; TUnit 495/495 passed in Release.

## Multi-project discovery increment

Scope: Selecting a source root that contains several projects (main library plus a source-generator project, tests, samples) used to find no top-level .csproj. The app now discovers every .csproj under the selected folder (recursive, `bin`/`obj`/`node_modules`/etc. skipped, depth-limited), shows a picker in the Project section when more than one exists, and the chosen project drives metadata, the command preview, and the NuGet pack. Packing the main project already builds referenced projects (including analyzer-style generator references), verified against the real SDK.

- [x] N12: Recursive project discovery skips build folders and feeds a picker; the chosen project drives the preview.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: ProjectDiscoveryTests pass (nested projects found, bin/obj copies excluded, picker shown only for multiple, chosen project appears in the pack preview); whole suite passes.
  EVIDENCE: 498/498 passed. `ProjectInspector.DiscoverProjectFiles`, `PublisherService.LoadProjectMetadataAsync`/`BuildPublishCommandBundle` accept an explicit project file, `MainViewModel.AvailableProjectFiles` + `ResolveProjectFilePath`, and the Project-section ComboBox. Fixed along the way: `SelectedProjectFile` added to the preview-sensitive set and the context update now runs before the preview refresh, so switching projects updates the preview in one pass.
- [x] N13: Real-SDK proof that packing a main project with a referenced generator project works.
  CHECK: dotnet new classlib main + gen, project reference, dotnet pack src/MainLib -c Release -o nupkgs
  EXPECT: package created containing lib/net10.0/MainLib.dll; referenced project built automatically.
  EVIDENCE: `Successfully created package .../MainLib.1.0.0.nupkg` with `MainLib.nuspec` + `lib/net10.0/MainLib.dll` inside on .NET SDK 10.0.402.
- [x] N14: Full solution verification after the increment.
  CHECK: dotnet build CodePublisher.slnx --configuration Release --no-restore && dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --configuration Release --no-build -- --disable-logo --progress off
  EXPECT: zero warnings/errors; all tests pass.
  EVIDENCE: Release build `0 Warning(s) 0 Error(s)`; TUnit 498/498 passed in Release.

## NuGet credential and owner auto-detection increment

Scope: In the NuGet/Deploy feed UI, selecting GitHub Packages auto-fills the owner from the project's csproj repository URL (`RepositoryUrl`/`PackageProjectUrl`/`ProjectUrl`) or the nearest `.git/config` remote (walking up, worktree `gitdir:` pointers included), and a "Detect owner & key" button re-runs detection. API keys are detected from `GH_TOKEN`/`GITHUB_TOKEN` (GitHub Packages) or `NUGET_API_KEY` (nuget.org), falling back to `gh auth token`; the token is kept in memory only and never logged. Parsing is pure (`GitHubOwnerDetector`, `GitConfigParser`, `NuGetProjectRepositoryReader`); credential lookup goes through the env/process-runner seams.

- [x] N15: Owner detection covers https/ssh/credentialed GitHub URLs, csproj properties, and git config; non-GitHub yields null.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: GitHubOwnerDetectorTests pass (URL shapes, git config remotes, csproj-over-remote precedence, walk-up); suite passes.
  EVIDENCE: 507/507 passed.
- [x] N16: API-key detection prefers env vars, falls back to `gh auth token`, returns null when nothing is set, and never runs gh for nuget.org.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: NuGetCredentialDetectionTests pass with the env and process-runner imposters.
  EVIDENCE: 507/507 passed; `DetectApiKey_GitHub_FallsBackToGhCli`, `DetectApiKey_NuGetOrg_UsesNuGetApiKey_AndNeverCallsGh`, `DetectApiKey_NothingConfigured_ReturnsNull` all green.
- [x] N17: Full solution verification after the increment.
  CHECK: dotnet build CodePublisher.slnx --configuration Release --no-restore && dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --configuration Release --no-build -- --disable-logo --progress off
  EXPECT: zero warnings/errors; all tests pass.
  EVIDENCE: Release build `0 Warning(s) 0 Error(s)`; TUnit 507/507 passed in Release.

## Deep offline .nupkg validation increment

Scope: "Test package" now validates the artifact in-process without installing it anywhere: the package is opened with NuGet's own `PackageArchiveReader` (NuGet.Packaging 7.9.0, added centrally), the nuspec must parse with a valid id and SemVer 2.0 version, dependency groups must be well formed, no zip entry may escape the archive, and every `lib/**/*.dll` must carry a real CLR header (own `ManagedAssemblyInspector` PE check), so a broken DLL can never be packed silently. Missing readme/no assets are warnings. The existing consumer-install test (local feed + throwaway project + build) still runs on top. `dotnet nuget verify` was researched and rejected: it only verifies signatures, not content.

- [x] N18: Validator checks the archive dependency-free (NuGet.Packaging was tried first and dropped: its signing P/Invokes raise WASM0001 in the browser host) plus a CLR-header check on every lib DLL.
  CHECK: dotnet build CodePublisher.slnx --no-restore && dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: zero warnings on every host including the browser WASM build; validator tests pass (real managed dll passes, text-as-dll errors, malformed nuspec errors, missing readme warns, malformed dependency errors); whole suite passes.
  EVIDENCE: 511/511 passed; solution build `0 Warning(s) 0 Error(s)` with no new package references.
- [x] N19: Real-SDK proof: validator accepts a real `dotnet pack` artifact with the correct identity.
  CHECK: dotnet pack a fresh classlib, then run NuGetPackageValidator over the produced .nupkg
  EXPECT: valid=true, id/version read from the nuspec, only the readme warning.
  EVIDENCE: `valid=True id=RealLib version=1.0.0 entries=5` + readme warning on SDK 10.0.402.
- [x] N20: Full Release verification.
  CHECK: dotnet build CodePublisher.slnx --configuration Release --no-restore && dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --configuration Release --no-build -- --disable-logo --progress off
  EXPECT: zero warnings/errors; all tests pass.
  EVIDENCE: Release build `0 Warning(s) 0 Error(s)`; TUnit 511/511 passed in Release.

## Crash log button increment

- [x] C6: The crash report scan is available as soon as the Deploy section is open.
  CHECK: dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj -- --disable-logo --progress off
  EXPECT: the scan button is enabled for a device target and for a desktop target, it is disabled only while a scan runs, and a missing package id is reported when the scan is pressed instead of leaving a dead button.
  EVIDENCE: The first version of the button depended on the package id in its enable state, and the package id is read from the project after the project is chosen. The command was never told to check again, so the button stayed disabled for the whole session. The scan now follows the rule of the other deploy actions: the button is enabled for the platform, and the package id is checked when the scan is pressed, with the message that says it is required and why. `MainViewModelCrashLogsTests` covers the enabled state for a device target, the enabled state for a desktop target, and the disabled state while a scan runs, and it fails when the notification that re-checks the button is removed. `AndroidCrashLogServiceTests` covers the missing package id and the missing device. 342 tests pass.
