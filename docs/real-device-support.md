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
- **Stop**: `adb -s SERIAL shell am force-stop PACKAGE` kills every process of the package but keeps
  the installed app and its data. It succeeds whether or not the app is running, so the UI action is
  safe to press repeatedly.
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
  and a plain-language reason for targets that cannot be used yet.
- Wireless debugging: `adb mdns services` scanning, pair with code, connect, disconnect, and the legacy
  `adb tcpip` step with the Wi-Fi address read from `ip route` (fallback: `dhcp.wlan0.ipaddress`).
- Install is update-in-place and never uninstalls. One automatic retry with `--no-streaming`; every other
  failure is mapped to a concrete next action. Launch resolves the launcher activity first and only falls
  back to `monkey`.
- A Device Files workspace: public folder shortcuts, path bar, folder navigation, drag-and-drop upload
  from the host, per-row delete with confirmation, and pull to this computer.
- Real iOS device discovery plus install/launch through `devicectl`, with the missing prerequisite named
  when a device is unpaired or has Developer Mode off.
- A Stop action next to Install/Uninstall/Launch on the Deploy tab for every target: Android device or
  emulator (`am force-stop`), iOS simulator (`simctl terminate`), and real iOS device (devicectl process
  discovery plus SIGKILL). Stopping never removes the app or its data, and stopping an app that is not
  running reports "nothing to stop" instead of an error.

## Not verified in this environment

- No Android device or emulator and no iOS device was attached while building this, so the adb/devicectl
  command lines are covered by parsers, unit tests, and documented behaviour rather than a live deployment.
  The device-specific behaviours listed above (mDNS on a guest network, `Android/data` access, OEM
  `monkey` restrictions, `--no-streaming` retries) need a real-device pass.
- `devicectl`'s JSON schema is versioned per Xcode release and is parsed defensively; a schema change
  would need a re-check against the Xcode version in use. The real-device Stop path depends on that
  schema most (apps/processes field names, `--signal SIGKILL` spelling); its parsers accept the known
  variants and are unit-tested against documented shapes, but the three-step terminate has never run
  against hardware — that is the first thing to check on a real iPhone.
