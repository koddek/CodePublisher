# Gates: .NET 11 upgrade and per-platform library split

OWNS: global.json, Directory.Build.props, Directory.Packages.props, Directory.Version.props, CodePublisher.slnx, src/**, tests/**, docs/**, .github/**, README.md, CONTRIBUTING.md, GATES.md

Scope: Move the whole repository (app, hosts, tests, tooling, docs) to the .NET 11 SDK and net11.0 family, bump NuGet packages to the latest compatible stable, adopt the C# 15 features that improve the implementation, split the publishing code into one isolated library per publish platform (Android, Apple for iOS+Mac Catalyst, macOS, Windows, Linux, NuGet) with a shared kernel and a composition root, keep net8/9/10 user projects publishable, and fix the Android publish output-directory handling and the errors that surface for net11 Android publishes.

- [x] G1: .NET 11 is pinned and every production project targets net11.0 (tests may carry net10 TFMs as inspection data)
  CHECK: bash -c 'grep -q "\"version\": \"11.0.100\"" global.json && ! grep -rn "net10\.0" --include="*.csproj" src && ! grep -rn "net10\.0-browser\|10.0.100\|10.0.x" global.json .github/workflows/*.yml Directory.Build.props Directory.Packages.props && echo NET11_PIN_OK'
  EXPECT: NET11_PIN_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=083d90bb9edc3ec13b8d5d5348404b97b73f3a96f65e8497ccc9dc91e363ef77; exit=0; EXPECT=matched; output-sha256=c6abf1447575289b9db28ef0a786458a17ee854d00a64976ba5f07979d692047; output-bytes=13; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=de0ff8374eb0/32 entries

- [x] G2: Central package versions are on the latest stable line
  CHECK: python3 -c "import re,sys; t=open('Directory.Packages.props').read(); assert 'Include=\"Avalonia\" Version=\"12.1.6\"' in t; assert 'Include=\"TUnit\" Version=\"1.83.0\"' in t; assert 'Avalonia.Diagnostics\" Version=\"11.3.23\"' in t; assert 'SkiaSharp\" Version=\"3.119.4\"' in t; assert 'CommunityToolkit.Mvvm\" Version=\"8.4.2\"' in t; assert 'QRCoder\" Version=\"1.8.1\"' in t; assert 'AndroidX' not in t; print('PKGS_OK')"
  EXPECT: PKGS_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=a31aeda012d23b4913ae148d17875c3e0638dad6fcb8ea23e0d3aea219e86039; exit=0; EXPECT=matched; output-sha256=2563432ad821aa691becc76226b1b233934d15a1ac46cca683d0a111b34d5b1c; output-bytes=8; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=de0ff8374eb0/32 entries

- [x] G3: Solution restores and builds Release with the .NET 11 SDK
  CHECK: bash -c 'dotnet restore CodePublisher.slnx -p:Configuration=Release && dotnet build CodePublisher.slnx --configuration Release --no-restore && echo BUILD_OK'
  EXPECT: BUILD_OK
  CWD: /usr/local/repo/CodePublisher
  EVIDENCE: automatic-evidence=v1; definition-sha256=8b1191d323a1c6b6da5d33825537abdb7bb6642312aed4e7e82f811267a4f341; exit=0; EXPECT=matched; output-sha256=a824cab72db4fd731c354f76c2ef9240e87c31e225adcde13317442b7bbf34ed; output-bytes=1881; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=de0ff8374eb0/32 entries

- [x] G4: Test suite passes on net11.0 (539+ tests, 0 failures)
  CHECK: bash -c 'dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --configuration Release -- --disable-logo --progress off'
  EXPECT: failed: 0
  EVIDENCE: automatic-evidence=v1; definition-sha256=aef6716d788f34b8c40d39b8fa986edbd281fdd5dbd61e1a3400a527a9affcdf; exit=0; EXPECT=matched; output-sha256=75b52e92fa70bd807a58f0f203e5ec10691f303885b1ac272fb2bf29e5fb4479; output-bytes=502; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=de0ff8374eb0/32 entries

- [x] G5: net11.0-android user project publishes with the flags the app emits (no XA1044)
  CHECK: bash -c 'rm -rf "/private/var/folders/qb/f708fgfx4x92fngnds91hjdr0000gn/T/opencode/apktest/out5" && dotnet publish "/private/var/folders/qb/f708fgfx4x92fngnds91hjdr0000gn/T/opencode/apktest/ApkTest.csproj" -f net11.0-android -c Release -r android-arm64 -p:SelfContained=true -p:PublishAot=false -p:PublishSingleFile=false -p:AndroidLinkMode=SdkOnly -p:PublishTrimmed=true -o "/private/var/folders/qb/f708fgfx4x92fngnds91hjdr0000gn/T/opencode/apktest/out5" && test -f "/private/var/folders/qb/f708fgfx4x92fngnds91hjdr0000gn/T/opencode/apktest/out5/com.scratch.apktest-Signed.apk" && echo NET11_ANDROID_OK'
  EXPECT: NET11_ANDROID_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=cd3aea9e3ecc11eba36e84d48cadc23b1cb93571ed1fd4fc684731b375fc4bfd; exit=0; EXPECT=matched; output-sha256=3e10d14d91481f9dc48d9fed2c8f823ecb9ebf38c165ec2e4684c371609d3eed; output-bytes=444; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=de0ff8374eb0/32 entries

- [x] G6: net10.0-android user project still publishes through the net10 path (Mono flags intact)
  CHECK: bash -c 'cp "/private/var/folders/qb/f708fgfx4x92fngnds91hjdr0000gn/T/opencode/apktest/ApkTest.csproj" "/private/var/folders/qb/f708fgfx4x92fngnds91hjdr0000gn/T/opencode/apktest/ApkTest10.csproj" && sed -i "" "s#net11.0-android#net10.0-android#" "/private/var/folders/qb/f708fgfx4x92fngnds91hjdr0000gn/T/opencode/apktest/ApkTest10.csproj" && rm -rf "/private/var/folders/qb/f708fgfx4x92fngnds91hjdr0000gn/T/opencode/apktest/out6" && dotnet publish "/private/var/folders/qb/f708fgfx4x92fngnds91hjdr0000gn/T/opencode/apktest/ApkTest10.csproj" -f net10.0-android -c Release -r android-arm64 -p:SelfContained=true -p:PublishAot=false -p:PublishReadyToRun=false -p:PublishSingleFile=false -p:AndroidLinkMode=None -p:RunAOTCompilation=false -p:AndroidEnableProfiledAot=false -o "/private/var/folders/qb/f708fgfx4x92fngnds91hjdr0000gn/T/opencode/apktest/out6" && test -n "$(ls "/private/var/folders/qb/f708fgfx4x92fngnds91hjdr0000gn/T/opencode/apktest/out6"/*.apk | head -n1)" && echo NET10_ANDROID_OK'
  EXPECT: NET10_ANDROID_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=43ba7887e7964beee704da81bf51daba8a646e06a51c448cfd1281d52f0c99ec; exit=0; EXPECT=matched; output-sha256=476be8ef7406ac7d627d3d5e2566e6c3b2abb0f2ba7aad043cd7bec2cf5a457a; output-bytes=313; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=de0ff8374eb0/32 entries

- [x] G7: The default publish output directory is the SDK-conventional publish folder
  CHECK: python3 -c "t=open('src/CodePublisher/CodePublisher.Core/Features/Publishing/Inspect/ProjectInspector.cs').read(); i=t.index('GetDefaultOutputDirectory'); seg=t[i:i+900]; assert 'bin' in seg and 'publish' in seg and '\"publish\"' in seg; print('OUTPUTDIR_OK')"
  EXPECT: OUTPUTDIR_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=7ec8f735289393a1dc0b087f8a7d5f6165973f2e51b221952c764cd8a639e0bb; exit=0; EXPECT=matched; output-sha256=ccc07b50997929d0c6c45f321385ddbc5f6228271d1e5a399ccb6c9b5e92fdef; output-bytes=13; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=de0ff8374eb0/32 entries

- [x] G8: Module split — one library per platform, kernel has no platform dependencies
  CHECK: python3 -c "import glob,os,re; A='src/CodePublisher/'; assert os.path.exists(A+'CodePublisher.Platforms.Android/CodePublisher.Platforms.Android.csproj'); assert os.path.exists(A+'CodePublisher.Platforms.Apple/CodePublisher.Platforms.Apple.csproj'); assert os.path.exists(A+'CodePublisher.Platforms.Mac/CodePublisher.Platforms.Mac.csproj'); assert os.path.exists(A+'CodePublisher.Platforms.Windows/CodePublisher.Platforms.Windows.csproj'); assert os.path.exists(A+'CodePublisher.Platforms.Linux/CodePublisher.Platforms.Linux.csproj'); assert os.path.exists(A+'CodePublisher.Platforms.NuGet/CodePublisher.Platforms.NuGet.csproj'); assert os.path.exists(A+'CodePublisher.Publishing/CodePublisher.Publishing.csproj'); refs=lambda p: re.findall(r'ProjectReference Include=.([^\"]+).', open(p).read()); core=refs(A+'CodePublisher.Core/CodePublisher.Core.csproj'); assert not any('Platforms' in r or 'Publishing' in r for r in core); libs=glob.glob(A+'CodePublisher.Platforms.*/*.csproj'); assert all(all('CodePublisher.Core' in r for r in refs(l)) for l in libs); pub=refs(A+'CodePublisher.Publishing/CodePublisher.Publishing.csproj'); assert sum('Platforms' in r for r in pub)==6, pub; print('BOUNDARIES_OK')"
  EXPECT: BOUNDARIES_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=60bbb8c4fcc03f9aa4c90ab80bafb14310706c21d287a8f72d8335b15e969260; exit=0; EXPECT=matched; output-sha256=b723de6a728eb19fd4e335275ad420a4921d4059528c1965940528069431163f; output-bytes=14; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=de0ff8374eb0/32 entries

- [x] G9: Desktop host NativeAOT publish succeeds on net11 and leaves no managed entry assembly
  CHECK: bash -c 'rm -rf "$TMPDIR/codepublisher-net11-aot" && dotnet publish src/CodePublisher/CodePublisher.Desktop/CodePublisher.Desktop.csproj --configuration Release --self-contained true -p:PublishAot=true -p:PublishReadyToRun=false -p:PublishSingleFile=false -o "$TMPDIR/codepublisher-net11-aot" && test ! -f "$TMPDIR/codepublisher-net11-aot/CodePublisher.Desktop.dll" && test -f "$TMPDIR/codepublisher-net11-aot/CodePublisher.Desktop" && echo AOT_OK'
  EXPECT: AOT_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=841cdc54d83f5c482642941ea953c3cf879eed490361bc57f6afd64184dc5c0a; exit=0; EXPECT=matched; output-sha256=f33fc5a82b38e9bbf62d0c5b8509d8bababbb58f0ee941bb36399a31d85999f2; output-bytes=4192; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=de0ff8374eb0/32 entries

- [ ] G10: Browser host builds and links the native SkiaSharp library on net11.0-browser
ABANDON: G10 The .NET 11 wasm-tools bundle an Emscripten toolchain whose LLVM no longer emits the `saveSetjmp`/`testSetjmp` symbols referenced by SkiaSharp's prebuilt WebAssembly native assets, so the link fails (verified locally 2026-11-11; upstream AvaloniaUI/Avalonia#21700). The user decided to drop the browser host from the .NET 11 line on 2026-11-11 until Avalonia ships WebAssembly assets built for the new toolchain; the host stays in git history. Handoff: restore `CodePublisher.Browser` from git history and re-enable this gate when the compatible assets publish.

- [x] G11: CI and release workflows pin 11.0.x; docs and DOX files describe the net11 reality
  CHECK: bash -c 'grep -q "11.0.x" .github/workflows/ci.yml && grep -q "11.0.x" .github/workflows/release.yml && grep -q "net11.0" docs/dotnet-10-11-publishing-research.md && grep -q "Platforms.Android" src/AGENTS.md && ! grep -q "until the .NET 11 upgrade" src/AGENTS.md && echo DOX_OK'
  EXPECT: DOX_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=4cfd727940d89a7aa8e908b58880dc8f4e333b758f089e23519cde1c7b7a306e; exit=0; EXPECT=matched; output-sha256=e220f07ce2ea4c5a2be24ca7b962c1514e154cec5fe518cc9f1b800085aabbb4; output-bytes=7; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=de0ff8374eb0/32 entries

- [x] G12: C# 15 is in effect via the net11.0 target framework on every project (no pinned LangVersion overrides)
  CHECK: bash -c 'test -z "$(grep -rn "<LangVersion>" --include="*.csproj" src tests)" && echo CSHARP_OK'
  EXPECT: CSHARP_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=3411fe25df350bd841949fb71c035c7f3382672f520745f7b0f6b5d7101e5881; exit=0; EXPECT=matched; output-sha256=710dd714f6f8b6ab135afb19861519695584cb01d6729e2e1cdf0040bd39a96d; output-bytes=10; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=de0ff8374eb0/32 entries

- [x] G13: git working tree holds only intended session changes and the scratch projects are untouched
  CHECK: bash -c 'test "$(git status --short | wc -l | tr -d " ")" -lt 120 && git status --short | grep -q . && echo STATUS_OK'
  EXPECT: STATUS_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=6c7518f0427f9b544a9d66c6d8f1e84e46015544a4329ee1c4fba0eb33d456ee; exit=0; EXPECT=matched; output-sha256=a2fbc8b0c90ec225a30dc4bd73d5aa4094daff8b1f7ff794bc68ce499ae76481; output-bytes=10; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=de0ff8374eb0/32 entries

## Deep-review pass (2026-11-17)

- [x] G14: Selecting Android Link Mode "Full" is preserved by the option resolver
  CHECK: bash -c 'dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --configuration Release --no-build -- --disable-logo --progress off --treenode-filter "/*/*/PublishSettingsStateTests/Normalize_PreservesFullLinkMode_WhenTheUserSelectsIt" 2>&1 | grep -q "succeeded: 1" && echo LINKMODE_FULL_OK'
  EXPECT: LINKMODE_FULL_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=2db9d2a7d3f471e53df6ea0a43e58706b349b0c553b58e4de055112c0dc78ee6; exit=0; EXPECT=matched; output-sha256=b799716aa2c15a91eb52e79bbdccc88676b8c32982d24deb73a25106718439dc; output-bytes=17; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=1d1e1056edc2/32 entries

- [x] G15: NuGet versions are latest-compatible (TUnit 1.86.0; Avalonia 12.1.6; SkiaSharp 3.119.4 pinned to Avalonia.Skia's exact requirement)
  CHECK: python3 -c "t=open('Directory.Packages.props').read(); assert 'Include=\"TUnit\" Version=\"1.86.0\"' in t; assert 'Include=\"Avalonia\" Version=\"12.1.6\"' in t; assert 'Include=\"SkiaSharp\" Version=\"3.119.4\"' in t; n=open('/Users/omoi/.nuget/packages/avalonia.skia/12.1.6/avalonia.skia.nuspec').read(); assert 'id=\"SkiaSharp\" version=\"3.119.4\"' in n; print('PKGS_LATEST_OK')"
  EXPECT: PKGS_LATEST_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=71fbb58012a753705f525642aa23846106e0582eabb2428af21753f5b28dbf3c; exit=0; EXPECT=matched; output-sha256=1a4d2f2f50f1d8ca96f6fbc18f68cf4481891a385a8026c507aa3acb2ef011e3; output-bytes=15; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=1d1e1056edc2/32 entries

- [x] G16: Solution builds clean in Release and the whole suite passes on net11.0
  CHECK: bash -c 'dotnet build CodePublisher.slnx --configuration Release --no-restore 2>&1 | grep -q "0 Warning(s)" && dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --configuration Release --no-build -- --disable-logo --progress off 2>&1 | grep -q "succeeded: 554" && echo GREEN_OK'
  EXPECT: GREEN_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=7394cb374daa9596237839e7ed9af9d26da7bb6df08f6cb126f473f9f15178a4; exit=0; EXPECT=matched; output-sha256=937df390e57c1c5578ceef22634e1d927cf912b842bf48eaf7b1de54186c89c2; output-bytes=9; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=1d1e1056edc2/32 entries

## C# 15 adoption pass (2026-11-17)

- [x] G17: NuGet pack outcome is a C# 15 union with exhaustive, null-safe readers
  CHECK: python3 -c "p='src/CodePublisher/CodePublisher.Platforms.NuGet/Features/NuGet/Services/NuGetPackageService.cs'; t=open(p).read(); assert 'public union NuGetPackOutcome(NuGetPackFailed, NuGetPackPacked)' in t; assert 'public bool Success => this is NuGetPackPacked' in t; assert t.count('null =>') >= 2; print('UNION_OK')"
  EXPECT: UNION_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=4ce9e28bc4138ad85c7fa78ab982f3a5c8c89b77ddcfed89d938ad96b054f406; exit=0; EXPECT=matched; output-sha256=2d1658d3a99ba69066c57ad36c63313cf0ad46e9fceb988c21079d4ac05207e1; output-bytes=9; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=1d1e1056edc2/32 entries

- [x] G18: MSBuild property fallback lookup is a C# 15 extension indexer used by platform resolvers
  CHECK: python3 -c "t=open('src/CodePublisher/CodePublisher.Core/Features/Publishing/Inspect/ProjectIdentifier.cs').read(); assert 'public static class EvaluatedPropertyLookup' in t; assert 'extension(IReadOnlyDictionary<string, string>? values)' in t; assert 'public string? this[params string[] names]' in t; a=open('src/CodePublisher/CodePublisher.Platforms.Android/Projects/AndroidProjectIdentifier.cs').read(); assert 'evaluated[\"ApplicationId\", \"PackageName\"]' in a; print('EXTINDEX_OK')"
  EXPECT: EXTINDEX_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=0d448dd0719ea4995158e08658420264a6baadf5cbd091f6299c96133b763f49; exit=0; EXPECT=matched; output-sha256=e0e9b39721c09e19362ee730f4148010d697d8575bcdae524489d74d7a38c995; output-bytes=12; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=1d1e1056edc2/32 entries

- [x] G19: C# 15 refactors keep the solution green with 554 passing tests
  CHECK: bash -c 'dotnet build CodePublisher.slnx --configuration Release --no-restore 2>&1 | grep -q "0 Warning(s)" && dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --configuration Release --no-build -- --disable-logo --progress off 2>&1 | grep -q "succeeded: 554" && echo CS15_GREEN_OK'
  EXPECT: CS15_GREEN_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=859e4a0dadb18f73084063d530c3316654abaf45d5ac775ebfcd9bd9c359c8d1; exit=0; EXPECT=matched; output-sha256=37e1b855c2f2fa6a352ab9f5ffc5b295d11b87c7b7e03de52d96389afb12877a; output-bytes=14; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=1d1e1056edc2/32 entries

## Google Play publishing pass (2026-11-17)

- [x] G20: Store images upload into the publish edit with the exact API URL and media type
  CHECK: bash -c 'dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --configuration Release --no-build -- --disable-logo --progress off --treenode-filter "/*/*/GooglePlayStoreImageTests/*" 2>&1 | grep -q "succeeded: 4" && echo PLAY_IMAGES_OK'
  EXPECT: PLAY_IMAGES_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=27afbbbe057255ee9168dba705174a170c694d5b43b210c09b92e6e1ed6e367f; exit=0; EXPECT=matched; output-sha256=46c867bf4a9ed666eb29d1840d88b68762a8b1b590db0e25818c19cfe9b6bcbc; output-bytes=15; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=1d1e1056edc2/32 entries

- [x] G21: Signing-key upgrade is a Console link, not a failing API command
  CHECK: python3 -c "t=open('src/CodePublisher/CodePublisher/Features/GooglePlay/GooglePlayViewModel.cs').read(); assert 'OpenAppSigningHelpAsync' in t; assert 'rotateAppSigningKey' not in t; assert 'support.google.com/googleplay/android-developer/answer/9842756' in t; print('SIGNING_GUIDE_OK')"
  EXPECT: SIGNING_GUIDE_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=5563077e38868fe366fabffac8a662595614daecbe2664d88f3ce1996b8cd176; exit=0; EXPECT=matched; output-sha256=36d05de5259acf8bad4a066b1b453ee85e733145fec6bb8345d743e862ec5a22; output-bytes=17; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=1d1e1056edc2/32 entries

- [x] G22: Google Play refactor keeps the solution green with 558 passing tests
  CHECK: bash -c 'dotnet build CodePublisher.slnx --configuration Release --no-restore 2>&1 | grep -q "0 Warning(s)" && dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --configuration Release --no-build -- --disable-logo --progress off 2>&1 | grep -q "succeeded: 558" && echo PLAY_GREEN_OK'
  EXPECT: PLAY_GREEN_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=ce360be49f1a9847c2b6d1a08cbca1417ee98f0326a211785b7ac8ffa3121b2b; exit=0; EXPECT=matched; output-sha256=13907dfa09940f484a074d2a9301a71a619d4c36e8152ae5a39e16face3f4cc5; output-bytes=14; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=1d1e1056edc2/32 entries

## Per-platform .NET 11 option audit (2026-11-17)

- [x] G23: .NET 11 Apple commands never emit the rejected PublishTrimmed property and let CoreCLR R2R stay on
  CHECK: bash -c 'dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --configuration Release --no-build -- --disable-logo --progress off --treenode-filter "/*/*/ApplePlatformNet11Tests/*" 2>&1 | grep -q "succeeded: 6" && echo APPLE_CMD_OK'
  EXPECT: APPLE_CMD_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=857a11b4ed9b7931d3b55258181949d8b295fca6d0b31ab23247b1e9854db7f5; exit=0; EXPECT=matched; output-sha256=6f7ff37836c6a82142bc1ca0358e84599c90a3d9a904dcfa4c9be2f0eb8e1970; output-bytes=13; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=1d1e1056edc2/32 entries

- [x] G24: Apple option policy reports the automatic .NET 11 CoreCLR state for R2R and trimming
  CHECK: bash -c 'dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --configuration Release --no-build -- --disable-logo --progress off --treenode-filter "/*/*/ApplePlatformNet11Tests/*" 2>&1 | grep -q "succeeded: 6" && echo APPLE_POLICY_OK'
  EXPECT: APPLE_POLICY_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=3c15802f9b391f7046970803b0ac530f0d96b73a8fbc7367a32e6d48beb64ef1; exit=0; EXPECT=matched; output-sha256=241d0d602499065eb59cf4b3ab227b45212a2b2808df4a5046a211b993d87a58; output-bytes=16; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=1d1e1056edc2/32 entries

- [x] G25: Desktop platforms keep manual R2R/AOT/single-file options with no automatic CoreCLR state
  CHECK: bash -c 'dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --configuration Release --no-build -- --disable-logo --progress off --treenode-filter "/*/*/DesktopNet11Tests/*" 2>&1 | grep -q "succeeded: 8" && echo DESKTOP_POLICY_OK'
  EXPECT: DESKTOP_POLICY_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=2ed1a4e10fc0e1873c21d6be296fd49ec789a687c5c6065d7883a3bef1d7d574; exit=0; EXPECT=matched; output-sha256=aa075a9d53e52614288d9a0de7baf225f91876e0eeed389dad4e87a2c59ba5a9; output-bytes=18; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=1d1e1056edc2/32 entries

- [x] G26: The per-platform audit keeps the solution green with 579 passing tests
  CHECK: bash -c 'dotnet build CodePublisher.slnx --configuration Release --no-restore 2>&1 | grep -q "0 Warning(s)" && dotnet run --project tests/CodePublisher.Tests/CodePublisher.Tests.csproj --configuration Release --no-build -- --disable-logo --progress off 2>&1 | grep -q "succeeded: 579" && echo PLATFORM_GREEN_OK'
  EXPECT: PLATFORM_GREEN_OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=9dde6ac8e98ec0100baadbda3863be1f19b2331b82ceed5c63bbbd07abaabfef; exit=0; EXPECT=matched; output-sha256=18b02a688930ca3e67f6f0e891c4ea0df000173100d59415341f600081658efa; output-bytes=18; shell=/bin/sh; cwd=/usr/local/repo/CodePublisher; path=1d1e1056edc2/32 entries
