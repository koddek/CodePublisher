# Real device support (Android and iOS)

Research and design record for deploying to real phones and tablets, and for browsing their files.
Checked October 2026 against Android platform-tools, Android 11–16 device behaviour, Xcode 15–26, and
the iOS 17–26 CoreDevice tooling.

## What the app did before this change

| Capability | Before | Gap |
| --- | --- | --- |
| Physical Android over USB | Listed by `adb devices -l`, usable | No model/Android version; unusable states dropped from the list |
| Physical Android over Wi-Fi | Worked only after a manual `adb connect` in a terminal | No pairing, no `adb mdns` scanning, no address/code entry |
| Emulator detection | Serial prefix `emulator-` | Emulators that report another serial looked like phones |
| Install | `adb install -r`, then an automatic uninstall + reinstall on `INSTALL_FAILED_UPDATE_INCOMPATIBLE` | The automatic uninstall silently deleted app data |
| Launch | `adb shell monkey` | Some OEM builds restrict `monkey` |
| Device files | One-way `adb push` into `/sdcard/Download` | No listing, no delete, no pull |
| Physical iOS | Not supported at all (`simctl` only) | No discovery and no install path |

## Answers to the original questions

- **Does the current design detect real Android devices over USB or Wi-Fi?** Yes for USB and for Wi-Fi
  devices that adb already connected to: discovery parsed `adb devices -l` and classified every target
  whose serial did not start with `emulator-` as a device. It did **not** discover a wireless device on
  its own, could not pair one, and hid `unauthorized`/`offline` rows.
- **Does it deploy the APK to a real device?** Yes, `adb install -r`. That is the update-in-place form:
  the package is replaced and app data survives.
- **Did it uninstall first?** Only in one case, and that was the wrong behaviour: an
  `INSTALL_FAILED_UPDATE_INCOMPATIBLE` failure triggered an automatic `adb uninstall` + reinstall, which
  deletes app data. This change removes that fallback. Install now updates in place and a signature clash
  fails with the manual path spelled out (Uninstall, which deletes app data, then Install).
- **Did it detect real iOS devices?** No. `IosSimulatorService` was simulator-only by contract.

## Android: how real devices are reached

Source: [adb](https://developer.android.com/tools/adb),
[device setup](https://developer.android.com/studio/run/device),
[AOSP adb man page](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/master/docs/user/adb.1.md).

- **Listing**: `adb devices -l`. Columns are `serial`, `state`, then `key:value` fields (`usb:1-2`,
  `product:`, `model:`, `transport_id:`). States seen in practice: `device`, `unauthorized`, `offline`,
  `bootloader`, `recovery`, `sideload`, `connecting`, plus `no permissions` when the host lacks USB
  permissions (Linux `plugdev`, i.e. the udev rule).
- **Real vs emulator**: the `emulator-` serial prefix is a hint, not proof. `ro.kernel.qemu` (deprecated
  but still set by AOSP images) and `ro.boot.qemu` report `1` on emulators, whatever the serial looks like.
- **Identity**: one `adb shell getprop` call per target gives `ro.product.model`,
  `ro.build.version.release`, `ro.build.version.sdk`, and the qemu signals.
- **Wireless, Android 11+**: enable Developer options → Wireless debugging, then `adb pair IP:PAIR_PORT CODE`
  followed by `adb connect IP:CONNECT_PORT`. The pairing port and the debugging port are different, and
  the pairing port closes when the dialog closes. `adb mdns services` lists both as
  `_adb-tls-pairing._tcp` and `_adb-tls-connect._tcp`.
- **Wireless, Android 10 and older**: USB once, `adb -s SERIAL tcpip 5555`, read the Wi-Fi address, then
  `adb connect IP:5555`. This path uses an unencrypted channel, so Android 11+ pairing is preferred.
- **Install semantics**: `adb install` is a fresh install, `adb install -r` replaces the package and keeps
  app data, `-d` allows a downgrade for debuggable packages, `--no-streaming` copies the APK first (older
  devices reject streaming installs), `--user` targets a specific Android user. Signature mismatches fail
  with `INSTALL_FAILED_UPDATE_INCOMPATIBLE`; a lower version code fails with
  `INSTALL_FAILED_VERSION_DOWNGRADE`; other common failures are `INSTALL_FAILED_NO_MATCHING_ABIS`,
  `INSTALL_FAILED_INSUFFICIENT_STORAGE`, `INSTALL_FAILED_USER_RESTRICTED`, `INSTALL_FAILED_VERIFICATION_FAILURE`.
- **Google Play compatibility**: a sideloaded build can only be updated later by Google Play when its
  signing certificate matches the certificate Play delivers (Play App Signing signs downloads with the
  app-signing key, not the upload key) and each Play release carries a higher version code. Install
  therefore runs a preflight first: `apksigner verify --print-certs` on the APK, `adb shell pm path`
  plus a pull of the installed `base.apk` for the same fingerprint, and `pm list packages -i` for the
  installer of record (`installer=com.android.vending` is Google Play). A certificate mismatch stops
  the install with both fingerprints and the Play impact spelled out before any command touches the
  device; a Play-installed copy is never steered toward uninstall, because a differently-signed
  replacement can never be updated by Play again.
- **Close**: two steps, because stopping alone is not closing. `adb -s SERIAL shell am force-stop PACKAGE`
  kills every process of the package but leaves its card in Recents (verified: the task stays listed, just
  further down). `am stack remove <ROOT_TASK_ID>` then dismisses that card — the ids come from
  `adb shell dumpsys activity recents`, where each entry reports `taskId`/`rootTaskId` plus the owning
  package and component. Only tasks the dump attributes to the requested package are dismissed, and
  `type=home` (and the Recents surface itself) is never touched, so closing one app cannot remove another
  app's card or the launcher's task. App data and the installed app are kept, and the operation succeeds
  whether or not the app is running.
  - `am stack remove` is the legacy-but-present `am` subcommand for this (`am help` lists it on
    Android 11 through 17); `am task remove` and `cmd activity remove-task` do not exist. Its exit code
    is not a success signal — it also exits 0 for an unknown id — so the app re-reads `dumpsys activity
    recents` afterwards and reports what is genuinely gone. A device that refuses the command reports
    "removing its Recents card failed" with the device's own message and the manual swipe-away fix
    instead of claiming success.
- **Files**: `/sdcard` is the shell alias of `/storage/emulated/<user>` for the foreground user.
  `ls -la`, `rm -rf`, `mkdir -p`, `adb push`, and `adb pull` work on the public folders (Download, Pictures,
  DCIM, Documents, Movies, Music, and friends). `ls -la` needs toybox, so the browser requires Android 7+.
  `Android/data` and `Android/obb` are restricted on Android 11+ and are not promised. Names with spaces
  and apostrophes must be quoted for the remote shell.

## iOS: what is actually possible

Sources: Apple's
[command-line device guide](https://developer.apple.com/documentation/xcode/interacting-with-devices-using-the-command-line),
[Xcode system requirements](https://developer.apple.com/xcode/system-requirements/).

- **Listing**: `xcrun devicectl list devices --json-output FILE` reports `result.devices[]` with
  `hardwareProperties.udid`, `deviceProperties.name`, `deviceProperties.osVersionNumber`,
  `deviceProperties.developerModeStatus`, and `connectionProperties.pairingState` / `transportType`
  (`wired` or `localNetwork`). `xcrun xctrace list devices` is a parsing fallback for devices CoreDevice
  does not know (seen with some iOS 16 devices).
- **Deploy**: `xcrun devicectl device install app --device UDID App.app` then
  `xcrun devicectl device process launch --device UDID BUNDLE_ID`.
- **Stop**: simulators use `xcrun simctl terminate UDID BUNDLE_ID`. CoreDevice has no terminate-by-
  bundle-id command, so a real device needs three steps: resolve the installed bundle path
  (`devicectl device info apps --device UDID --json-output FILE`), find the running processes whose
  executable lives inside that bundle (`devicectl device info processes --device UDID --json-output
  FILE`), then signal each PID with `devicectl device process signal --device UDID --pid PID --signal
  SIGKILL`. The apps/processes JSON is not a published schema, so `IosStopParsers` accepts the
  field-name variants seen in the wild (`bundleIdentifier`/`bundleId`, `bundlePath`/`bundleURL`/nested
  path objects, `processIdentifier`/`pid`, string or object `executable`).
- **Prerequisites that cannot be automated here**: one-time USB pairing with trust, Developer Mode on
  iOS 16+, and a build signed with a development profile that includes the device.
- **Files**: real iOS storage is sandboxed. There is no public command-line file browser equivalent to
  `adb shell ls`, so the app does not offer one; `simctl` File Provider copying remains simulator-only.

## What this change implements

- Real devices and emulators in one ranked list with model, Android version, transport (USB/Wi-Fi/emulator),
  and a plain-language reason for targets that cannot be used yet. The selected target also gets a detail
  row — model, `Android <version> (API <level>)`, CPU ABI, transport, and readiness — read from one
  `getprop` call, so the ABI needed for the Runtime Identifier is visible before publishing.
- Wireless debugging: `adb mdns services` scanning, pair with code, pair by QR code, connect, disconnect,
  and the legacy `adb tcpip` step with the Wi-Fi address read from `ip route` (fallback:
  `dhcp.wlan0.ipaddress`).
- Pair by QR code works the way Android Studio's does, and the direction is the opposite of what the
  scanner suggests: the QR carries the pairing name and password the **computer** invents —
  `WIFI:T:ADB;S:<mDNS instance name>;P:<pairing secret>;;` (`packages/apps/Settings` `WifiQrCode`) — the
  phone that scans it starts its own pairing server and advertises it under that name, and this computer
  finds that `_adb-tls-pairing._tcp` service through the adb server's mDNS discovery and runs
  `adb pair <address> <secret>`. The host is the pairing client, so no host-side pairing server is needed
  and the password never crosses the network outside the pairing handshake. After pairing, the device's
  `_adb-tls-connect._tcp` port is picked up on the same host and connected, so the target lands in the
  device list selected rather than as instructions (AOSP `docs/dev/adb_wifi.md`).
- The Android target list keeps itself current: while a section that shows targets is open, a cheap
  `adb devices -l` probe runs every three seconds and a full discovery only when the probe disagrees with
  what is on screen, so plugging in a phone, approving its debugging prompt, or unplugging it shows up and
  is selected without pressing Refresh. A launch starts a one-second wait that keeps the list live while
  the emulator boots and then selects the AVD that was started, which is the case that used to need a
  manual refresh.
- Install is update-in-place and never uninstalls. One automatic retry with `--no-streaming`; every other
  failure is mapped to a concrete next action. A certificate preflight runs before install: the APK and
  the copy already installed are compared with apksigner, and a mismatched build is blocked with both
  fingerprints and its Google Play update consequences instead of being steered toward an uninstall.
  Launch resolves the launcher activity first and only falls back to `monkey`.
- A Device Files workspace: public folder shortcuts, path bar, folder navigation, drag-and-drop upload
  from the host, per-row delete with confirmation, and pull to this computer.
- Real iOS device discovery plus install/launch through `devicectl`, with the missing prerequisite named
  when a device is unpaired or has Developer Mode off.
- A **Close app** action next to Install/Uninstall/Launch on the Deploy tab for Android device or
  emulator (`am force-stop` plus `am stack remove` for the Recents card), and Stop for iOS simulator
  (`simctl terminate`) and real iOS device (devicectl process discovery plus SIGKILL). Closing never
  removes the app or its data, and closing an app that is not running reports "nothing to stop" / "no
  card in Recents" instead of an error.

## Not verified in this environment

- No physical Android device or iOS device was attached while building this, so hardware-specific
  behaviour is covered by parsers, unit tests, and documented sources rather than a live deployment.
  The device-specific behaviours listed above (mDNS on a guest network, `Android/data` access, OEM
  `monkey` restrictions, `--no-streaming` retries) need a real-device pass.
- The phone half of QR pairing is Android's own handler (`Developer options > Wireless debugging > Pair
  device with QR code`), which needs a camera, so it could not be driven here: the Android 17 emulator's
  DPP QR activity needs a scan, and enabling its wireless debugging produced no mDNS advertisement. What
  was verified with the production code is the half this app owns: an `_adb-tls-pairing._tcp` service
  published under a generated instance name was found through `adb mdns services` and matched by name, the
  pairing command was aimed at that endpoint with the generated password (the endpoint recorded the
  connection), the failure named both, and the match disappeared when the advertisement stopped. The code
  the phone reads was decoded by an independent decoder (Apple Vision) from the rendered window, and it
  decoded to the exact payload the view model generated. A real-phone pass is still the first thing to do
  on hardware; the pairing password, the generated name, and a phone that prefixes the requested name with
  its own serial are all covered by unit tests.
- Close was exercised end-to-end against the same Android 17 emulator (API 37) with the production service code:
  an app in the foreground and an app in the background were both stopped **and** removed from Recents,
  a stopped app with no card reported the no-card no-op, and a second app's card plus the launcher stayed
  untouched. `am stack remove` behaviour on older OEM builds (a command rejection) is covered by the
  reported-failure path, not by a live test.
- The Android install preflight was exercised end-to-end against an Android 17 emulator (API 37): fresh
  install, same-key update, and a differently-signed build blocked with both fingerprints and guidance.
  The Google Play-installed branch (`installer=com.android.vending`) could not be produced live — adb
  installs record `installer=null` — so it is covered by unit tests on the captured output shape only,
  and the sideloaded build's version code still has to stay below the next Play release for Play to
  offer its update.
- `devicectl`'s JSON schema is versioned per Xcode release and is parsed defensively; a schema change
  would need a re-check against the Xcode version in use. The real-device Stop path depends on that
  schema most (apps/processes field names, `--signal SIGKILL` spelling); its parsers accept the known
  variants and are unit-tested against documented shapes, but the three-step terminate has never run
  against hardware — that is the first thing to check on a real iPhone.
