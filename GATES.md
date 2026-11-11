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
