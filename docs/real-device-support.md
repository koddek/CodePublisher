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
- Wireless debugging: discovery over `adb mdns services` **and** the operating system's resolver, pair
  with code, pair by QR code, a connect that is verified against `adb devices -l`, disconnect, a reset
  that clears the computer's Wi-Fi state, and the legacy `adb tcpip` step with the Wi-Fi address read
  from `ip route` (fallback: `dhcp.wlan0.ipaddress`).
- Pair by QR code works the way Android Studio's does, and the direction is the opposite of what the
  scanner suggests: the QR carries the pairing name and password the **computer** invents —
  `WIFI:T:ADB;S:<mDNS instance name>;P:<pairing secret>;;` (`packages/apps/Settings` `WifiQrCode`) — the
  phone that scans it starts its own pairing server and advertises it under that name, and this computer
  finds that `_adb-tls-pairing._tcp` service through whichever discovery path can see it and runs
  `adb pair <address> <secret>`. The host is the pairing client, so no host-side pairing server is needed
  and the password never crosses the network outside the pairing handshake. After pairing, the device's
  `_adb-tls-connect._tcp` port is picked up on the same host and connected, so the target lands in the
  device list selected rather than as instructions (AOSP `docs/dev/adb_wifi.md`).
- The Android target list keeps itself current: while a section that shows targets is open, a cheap
  `adb devices -l` probe runs every three seconds and a full discovery only when the probe disagrees with
  what is on screen, so plugging in a phone, approving its debugging prompt, or unplugging it shows up and
  is selected without pressing Refresh. The same loop reconnects a paired Wi-Fi phone by itself when its
  debugging port starts answering again, with a 15–60 s backoff and no alert. A launch starts a one-second wait that keeps the list live while
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
- A phone is on this network and was used to check discovery on 25 October 2026 (macOS 26.6.2, adb
  37.0.1): merged discovery finds its `_adb-tls-connect` service at 192.168.50.237:42857 while
  `adb mdns services` lists nothing, the resolver's loopback-first answer was reproduced, and the
  connect path reports the phone's refused port honestly. A complete QR pairing followed by a usable
  session against this phone is still unverified, because the phone was not accepting connections on
  its advertised port when the checks ran; re-run the QR flow with the phone's Wireless debugging screen
  open and awake to close that gap.
- The phone half of QR pairing is Android's own handler (`Developer options > Wireless debugging > Pair
  device with QR code`), which needs a camera, so it could not be driven here: the Android 17 emulator's
  DPP QR activity needs a scan, and enabling its wireless debugging produced no mDNS advertisement. What
  was verified with the production code is the half this app owns: an `_adb-tls-pairing._tcp` service
  published under a generated instance name was found through the computer's mDNS discovery and matched by name, the
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

## Android wireless debugging by version

Wireless debugging works differently across Android versions, and the difference between the pairing
port and the debugging port is the most common cause of intermittent connections. The table below
covers what each version requires and which connection method the app uses.

| Android version | API level | Wireless debugging method | Pairing port | Debugging port | Encrypted |
|---|---|---|---|---|---|
| Android 10 and older | ≤ 29 | Legacy `adb tcpip` over USB once, then `adb connect IP:5555` | n/a | 5555 | No |
| Android 11–13 | 30–33 | Pairing code (6 digits) or QR code, then `adb connect` | Random, closes when dialog closes | Random, shown after pairing | Yes (TLS) |
| Android 14+ | 34+ | QR code or pairing code, mDNS discovery preferred | Random | Random | Yes (TLS) |

### Android 10 and older (API ≤ 29)

These devices do not have the wireless debugging settings screen. The only path is:

1. Connect the device over USB with USB debugging authorized.
2. Run `adb -s SERIAL tcpip 5555` to restart adb on the device listening on TCP port 5555.
3. Read the Wi-Fi IP address (`ip route` or `dhcp.wlan0.ipaddress`).
4. Disconnect USB.
5. Run `adb connect IP:5555`.

This channel is unencrypted, so it is inherently less secure than the Android 11+ pairing flow. The app
offers this as "Wi-Fi debugging over USB" and requires a USB-attached device to start it.

### Android 11–13 (API 30–33)

Developer options > Wireless debugging shows two ports:

* **Pairing port** — used once for `adb pair IP:PAIR_PORT CODE`. The code is a 6-digit number shown on
  the phone. The port closes when the pairing dialog on the phone closes or when pairing times out.
* **Debugging port** — used for `adb connect IP:CONNECT_PORT` after pairing succeeds. This is the live
  ADB session over Wi-Fi.

The app's pairing-code flow runs `adb pair <address> <code>`, then `adb connect <address>` with the
debugging port. If a user pastes the pairing port into the connect field, the connect fails — the app's
diagnostics now detect this and tell the user to use the debugging port instead.

QR code pairing (`WIFI:T:ADB;S:<name>;P:<secret>;;`) works on these versions too. The computer invents
the service name and secret, shows them as a QR code, the phone scans it and starts its pairing server,
and the computer pairs against it with `adb pair`. The host is always the pairing client.

### Android 14+ (API 34+)

mDNS discovery is the recommended path: `adb mdns services` lists both `_adb-tls-pairing._tcp` and
`_adb-tls-connect._tcp` services. The app scans for these, pairs via QR code or pairing code, then
connects to the debugging port that appears after pairing. Android 14+ devices are more likely to
advertise both services reliably, which is why QR pairing works best on newer phones.

### Why Wi-Fi debugging sometimes works and sometimes doesn't

The most common failure modes, and what the app's diagnostics check for:

1. **Wrong port** — The user enters the pairing port where the debugging port belongs, or vice versa.
   The pairing port changes every time and closes when the dialog closes. The diagnostics check which
   port the device is advertising and flag mismatches.

2. **Not paired yet** — Pairing is a one-time trust step per computer and phone. Without it, `adb
   connect` fails. The diagnostics check whether the host is in the pairing history.

3. **Not connected** — Pairing does not automatically create a live debugging session. The user must
   press Connect after pairing. The diagnostics check the live `adb devices -l` target list.

4. **Target not ready** — The device is listed but in `unauthorized`, `offline`, or `no permissions`
   state. The diagnostics report the exact state and the fix (approve the USB debugging prompt,
   reconnect the cable, or connect again over Wi-Fi).

5. **mDNS not visible** — The device is not advertising a wireless debugging service on the network.
   Wireless debugging must be enabled on the phone, and both machines must be on the same Wi-Fi network
   that allows mDNS (guest networks often block it).

6. **Android version** — Android 10 and older need the legacy USB-first `adb tcpip` step; Android 11+
   needs pairing before connecting. The diagnostics report the version-specific requirement.

7. **The phone is asleep, or wireless debugging is off** — see below. The phone advertises its
   debugging service and answers mDNS only while it is awake, so a phone whose screen is off can
   leave the host and the pairing spinner waiting for an advertisement that never arrives.

### QR pairing hangs on "Pairing device…" when the host's mDNS backend sees nothing

Symptom: the phone scans the QR code, shows the pairing spinner, and stays there indefinitely. No
pairing happens and the app eventually reports a timeout.

Cause: QR pairing is host-as-client. The host must find the phone's `_adb-tls-pairing._tcp`
advertisement to know where to send `adb pair`. The adb *server* performs that discovery, and on some
hosts it performs none at all, so the host never issues the pair command and the phone waits forever.

Verified on macOS 26 with platform-tools 36.0.2 (`adb mdns check` reports `adb discovery 0.0.0`):

* `dns-sd -R <name> _adb-tls-pairing._tcp local <port>` publishes an advertisement. `dns-sd -B
  _adb-tls-pairing._tcp` lists it. `adb mdns services` lists nothing, on every poll, after a server
  restart, and either side of the advertisement.
* `adb -s emulator-5554 tcpip 5555` makes the emulator's adbd publish `_adb._tcp`. `dns-sd -B
  _adb._tcp` lists `adb-EMULATOR37X1X11X0-HyXcDn`. `adb mdns services` again lists nothing. The same
  holds with the emulator disconnected and with `ADB_MDNS_AUTO_CONNECT=1`.
* `ADB_MDNS_OPENSCREEN=1` is not a workaround in 36.0.2: `adb mdns check` then fails with
  `error: protocol fault (couldn't read status): Connection reset by peer`.

The app therefore reads discovery on both paths, in one place
(`AndroidDeploymentService.DiscoverAllWirelessServicesAsync`): `adb mdns services`, then the operating
system's own resolver via `dns-sd -B`, `dns-sd -L <instance name> <service type> local` and `dns-sd -G
v4 <host>` keeping the first reachable (non-loopback, non-link-local) IPv4 address, and
`AndroidWirelessDebug.MergeServices` combines the two lists into one row per service. `dns-sd -L` and
`dns-sd -G` listen indefinitely, so each run is stopped as soon as it answers or after a four-second
window.

Every consumer reads that merged list — QR pairing's search for the phone's pairing service, the
debugging port that is connected to after pairing, `Scan nearby devices`, and the diagnostics — because
losing the phone in any one of them is a different visible failure.

The resolver lookup keeps listening past an address it cannot use. Verified on this Mac:
`dns-sd -G v4 MacBook-Air.local` answers `127.0.0.1` on interface 1 first and `192.168.50.158` on
interface 13 second; stopping at the first answer and then rejecting it as loopback lost the name and
left `.local` hostnames in the address, so the lookup now stops only once an address passes
`PickBestIpv4`.

`adb pair` has no timeout of its own. Against a port that accepts the connection and then says nothing
(reproduced with `nc -l` and a published `_adb-tls-pairing._tcp` advertisement) it waits for the
pairing handshake without an end, which is how a phone whose dialog closed leaves the panel spinning.
The app bounds the pairing command at 60 s and reports that the phone stopped answering.

The pairing-code and manual flows do not depend on the host's mDNS at all, which is why they keep
working when QR pairing does not.

### What the app now does about the adb server and the socket errors

Google's wireless debugging troubleshooting reads three fields off `adb server-status`: `version`
(37.0.0 or newer), `mdns_enabled` (true), and `mdns_backend` (LIBADBMDNS). The remedy for each is
named: update platform-tools, set `ADB_MDNS=1`, set `ADB_MDNS_OPENSCREEN=0`, and restart the server
with `adb kill-server` then `adb start-server`.

`AndroidAdbServerStatus.Parse` reads that output, and `AndroidWirelessDiagnostics` reports the failed
checks as one diagnosis row. `AndroidDeploymentService.RepairWirelessDiscoveryAsync` applies the only
step it safely can: it restarts the server with the two documented variables, after checking that
`adb devices -l` lists no target, because a restart drops every attached session. The result states
what it applied and how many services the restarted server now sees, so an older server that cannot
get the LIBADBMDNS backend (platform-tools 36.x does not have it) still says "update platform-tools"
instead of claiming to be fixed.

Every `adb connect` is preceded by `adb disconnect <endpoint>`. The documented reason: a half-open
transport for the endpoint makes the phone sit on its "connecting" spinner while the host reports
nothing, and the disconnect is the documented reset. On failure the socket error adb prints is read
by `AndroidWirelessDebug.ReadConnectFailure`, which separates "no route to host" (a network problem:
the phone is not on a network this computer can reach) from "connection refused" (the port closed,
so the address or the port is stale) from "timed out" (the phone did not answer).

### Verified against a real phone (25 October 2026)

Environment: macOS 26.6.2 arm64, `/opt/homebrew/bin/adb` 36.0.2 (`adb version`), host 192.168.50.158
on 192.168.50.0/24, phone 192.168.50.237, `adb server-status` reporting
`mdns_backend: OPENSCREEN`, `version: "36.0.2"`, `mdns_enabled: true`.

* `adb mdns track-services --proto-text` reports the phone while it is responsive:

  ```
  tls {
    service {
      instance: "adb-R5CN20DFRKA-0z8hFB (2)"
      service: "_adb-tls-connect._tcp"
      ipv4: "192.168.50.237"
      port: 42857
      product_model: "SM-G986U1"
      build_version_sdk_full: ""
    }
    known_device: true
  }
  ```

  `known_device: true` means this computer is already paired with the phone, and the `(2)` marker on
  the instance name is the service version of ADB Wi-Fi 2.0. `product_model` SM-G986U1 is a Samsung
  Galaxy S20+ 5G. `adb mdns services` prints the same row as
  `adb-R5CN20DFRKA-0z8hFB (2)  _adb-tls-connect._tcp  192.168.50.237:42857`, which
  `AndroidWirelessDebug.ParseMdnsServices` reads as name `adb-R5CN20DFRKA-0z8hFB`, address
  `192.168.50.237:42857`, service version 2.

* The advertisement is intermittent on the phone's side, not the host's. Four back-to-back windows of
  `adb mdns services` every 3 s, each window on a freshly restarted server, with
  `ADB_MDNS=1 ADB_MDNS_OPENSCREEN=0` for the odd windows: 18 of 20 samples, 20 of 20 (no variables),
  18 of 20, then 0 of 20. The same server configuration therefore both saw the phone and missed it,
  and the configuration that changed was the phone's, so the app does not claim the environment
  variables fix discovery on this host. When the phone is quiet, `adb mdns services` is empty for
  both backends and `ping` still succeeds.

* When the phone is quiet, every TCP port refuses while ICMP passes. `ping -c 3 192.168.50.237`
  returns 3 of 3 packets, and `nc -z` to 42857, 5555, 5554, 80, 443, and 7000 each fails with
  `Connection refused`. The adb server log records the same thing as
  `failed to connect to '192.168.50.237:42857': Connection refused` and, from the earlier attempts
  on 23–24 October, `failed to connect to '192.168.50.237:41023': No route to host`. A phone that
  answers mDNS and refuses every port is not advertising a usable debugging port, so the phone has to
  be woken with Developer options > Wireless debugging open before the host can connect to it.

* The phone auto-connected once while it was responsive: `adb devices -l` listed the Wi-Fi target for
  about a minute and then dropped it. That is the ADB Wi-Fi 2.0 behaviour (the device connects to the
  workstation on a trusted network), and it needed no `adb connect` from the host.

Not verified: pairing from this host against this phone, and a debugging session over the resulting
connection, because the phone stops offering its port whenever the screen is off. Re-check on the
phone with its screen on before reporting Wi-Fi support as proven.

### The pairing protocol fault, and why the newest adb is the one the app runs

The next real attempt found the pairing service (the resolver fallback worked) and `adb pair` failed
with:

```
error: protocol fault (couldn't read status message): Undefined error: 0
```

That is the phone answering the pairing port but not speaking the pairing protocol this adb offers,
and it points at the adb build rather than at the network. The adb log from the same window shows the
same failure for the connect service a little earlier:

```
I adb : tls_connection.cpp:211 [client]: Starting adbwifi tls handshake
E adb : tls_connection.cpp:290 [client]: Post-handshake SSL_peek failed [SSLV3_ALERT_CERTIFICATE_UNKNOWN]
```

`SSLV3_ALERT_CERTIFICATE_UNKNOWN` is the phone refusing this computer's key, so the pairing has to be
repeated from the phone side too (Wireless debugging > Paired devices > Forget). Both messages are
read by `AndroidWirelessDebug.ReadPairingFailure`, which reports the fix for each: update adb to
37.0.0 or newer, then forget this computer on the phone and pair again.

The host had two adb installations, and the app was using the older one:

* `/opt/homebrew/bin/adb` -> `/opt/homebrew/Caskroom/android-platform-tools/36.0.2/platform-tools/adb`,
  first on `PATH`.
* `/Users/<user>/Library/Android/sdk/platform-tools/adb` at `Version 37.0.1-15733141`.

`PublisherService.ResolveExecutable` returned the first `PATH` hit, so the app ran 36.0.2 while 37.0.1
sat unused. It now reads `adb version` for every adb it can find and runs the newest
(`AndroidAdbVersion.Parse`, ordered by major.minor.patch, `ResolveAdbCandidates`). Wireless debugging
against a current phone needs 37.0.0 or newer, and the server picks up whatever the client that
starts it runs, so the oldest copy on the machine is the worst one to pick.

With 37.0.1 the server reports what Google's checks ask for:

```
usb_backend: NATIVE
mdns_backend: LIBADBMDNS
version: "37.0.1"
```

A running server holds port 5037, so a newer client cannot replace it on its own; the app's repair
restarts the server (only when `adb devices -l` is empty) and reports the version and backend it
ended up with.

### Older adb copies: named, never removed

`PublisherService` reads `adb version` for every adb on the machine and keeps the list as
`AdbInstallations` (newest first, the app's choice flagged). The ones it does not run are reported as
`UnusedAdbNotes`, with the command that removes each:

```
/opt/homebrew/bin/adb (36.0.2-14143358) — remove it yourself with: brew uninstall --cask android-platform-tools
```

The app does not delete them. A Homebrew cask or an IDE owns those files, and removing them would
leave that installer's own records wrong, so the app names them and hands over the command instead.

### Forgetting and revoking: phone-side only, no adb equivalent

Both actions live on the phone and there is no adb command for either:

* Forget one computer: Developer options > Wireless debugging > the computer under Paired devices >
  Forget.
* Start over with every computer: Developer options > Revoke adb debugging authorizations.

The app cannot press those buttons, so it opens the screen for you
(`adb -s SERIAL shell am start -a android.settings.APPLICATION_DEVELOPMENT_SETTINGS`) as the "Open
Developer options on the phone" action on a connected device, and says what to press when it gets
there. The QR invitation itself is always fresh: every press of "Pair with QR code" invents a new
pairing name and password, so nothing is reused from an earlier attempt.

### Keeping the phone from looking frozen

The phone's pairing dialog and the host are two independent timers, and the phone's dialog closes
when it times out or is dismissed. Waiting out the host's full window after that is what makes the
panel look stuck, so the app:

* counts four visible steps (code shown, phone scanned, paired, connected) with a progress bar and a
  plain-language line for each, so the wait is legible rather than silent;
* keeps the code on screen until the pairing finishes, so a slow phone can still scan it;
* notices once the phone's advertisement stops answering after it was seen, tells the user that the
  phone closed its dialog, and asks for a fresh code instead of running the countdown down.

### The protocol fault is a disconnected phone, and mDNS records outlive it

A pairing attempt on 25 October found a pairing service at `192.168.50.237:39495` and `adb pair`
reported:

```
error: protocol fault (couldn't read status message): Undefined error: 0
```

That text is not a diagnosis. Two probes against a local listener settle what it means, because adb
prints it for both: a port that accepts the connection and closes it, and a port where nothing
listens at all. The adb *server log* does distinguish them, and for this attempt it said:

```
W adb : network.cpp:149 failed to connect to '192.168.50.237:39495': No route to host
E adb : pairing_client.cpp:133 Failed to start pairing connection client [failed to connect to '192.168.50.237:39495': No route to host]
```

So the TCP connection never opened. The `adb pair` command does not print the socket error; only the
server log carries it, and a caller waiting on `adb pair` sees no difference between this and a phone
that answered and rejected the handshake.

The phone's advertisement is also not proof of anything: `adb mdns services` still listed
`_adb-tls-connect._tcp` at `192.168.50.237:42857` while `ping` to that address failed, and `nc` to
every port returned `Connection refused`. An mDNS record cached by the host outlives the phone
leaving the network, and AOSP's responder keeps answering queries about it.

The app therefore probes the address itself before it hands adb a password
(`AndroidDeploymentService.ProbeEndpointAsync`, a raw TCP connect with a four-second bound) and
reports the socket error in words:

| Probe result | What the app says |
| --- | --- |
| `Connection refused` | nothing is accepting connections on that port; the dialog closed or the port is the wrong one |
| `timed out` / `No route to host` | the phone did not answer; rescan, because the address from an earlier scan goes stale |

The guidance for an unreachable phone names the stale-address case, because that is what a user sees
when the phone was advertising a minute ago: rescan rather than retry. The QR loop uses the same
probe and waits for the phone to come back instead of spending the pairing password on a dead
address; after ten consecutive failed probes it says the pairing port stopped answering and asks for
a fresh code, because a pairing server that stops answering belongs to a dialog that closed.

### A pairing attempt starts clean, and a connection is only reported when adb confirms it

Verified on 25 October 2026 with macOS 26.6.2 arm64, adb 37.0.1 (`mdns_backend: LIBADBMDNS`,
`mdns_enabled: true`), host 192.168.50.158 and the phone at 192.168.50.237 (SM-G986U1).

One Wireless debugging QR attempt now runs in this order, and each step exists because of a failure
seen on real hardware:

1. **Clean slate first.** The transport the previous attempt left is dropped (`adb disconnect`), this
   app's pairing record for that host is discarded so the next attempt is a real pairing rather than a
   reconnect that trusts a stale entry, and the target list is re-read. A new code is never shown on top
   of an old half-open connection.
2. **A snapshot of what is already advertised.** A pairing service exists only while some phone has its
   pairing dialog open, so the names visible before the code appears cannot belong to this attempt.
3. **Match the requested name, then any new pairing service.** The phone is asked for the exact instance
   name in the QR's `S:` field, and a vendor build that prefixes or rewrites it still starts a pairing
   server when it scans. Accepting a pairing service that appeared after the snapshot keeps a correct
   scan from ending in a timeout; a name that matches is always preferred.
4. **Probe, then pair.** The advertised address is probed with a raw TCP connect before the password is
   spent, and `adb pair` is bounded at 60 s. Without the bound, a port that accepts and then says
   nothing leaves the command waiting with no end — reproduced with `nc -l` and a published
   `_adb-tls-pairing._tcp` advertisement.
5. **Connect to what is published now.** `adb devices -l` is checked first, because adb's own pairing
   client tries the device straight after the handshake. If that does not happen, every debugging port
   advertised for that host is connected to, and the saved address is only the last resort: the port
   changes whenever wireless debugging restarts, and the adb mDNS cache can still hold the old one.
6. **Success means `device`.** `adb connect` is not a success signal: on this host
   `adb connect 192.168.50.237:42857` printed `failed to connect to '192.168.50.237:42857': No route to
   host` and **exited 0**. The row `adb devices -l` reports for the endpoint decides
   (`AndroidWirelessDebug.FindWirelessTargetState`), and `offline` is retried while `unauthorized` stops
   with the approval instruction. The loop keeps trying for 75 s, so a phone that needs a moment after
   pairing still connects.

The host half of that flow was exercised live: with the snapshot taken first, publishing
`dns-sd -R codepublisher-livetest _adb-tls-pairing._tcp local 55337` was found by the merged
discovery (`codepublisher-livetest 192.168.50.158:55337`) while `adb mdns services` still listed
nothing, the differing-name fallback matched it against the requested name, the TCP probe reached the
listener, and `adb pair` ended at the 60 s bound with the sentence about a phone that stopped
answering.

What the host cannot fix: this phone advertises `192.168.50.237:42857` while `nc -z` reports
`Connection refused`, `adb connect` reports `No route to host`, and `ping` succeeds. That is the phone
publishing an address whose debugging port is not accepting connections (screen off, or Wireless
debugging closed) — no host-side command opens it. The app says exactly that, keeps retrying inside its
window, and leaves the host under "Paired devices (not connected)" with a Connect action instead of
pretending the device is connected.

### A listed endpoint is not a connection: the `offline` row a failed connect leaves behind

Verified on 26 October 2026 on the same host and phone (macOS 26.6.2 arm64, adb 37.0.1, host
192.168.50.158, phone 192.168.50.237).

`adb connect` can add a row to `adb devices -l` even when it prints a failure. With a plain TCP listener
on this host and no device behind it:

```
$ adb connect 127.0.0.1:45999
failed to connect to 127.0.0.1:45999
$ echo $?
0
$ adb devices -l
List of devices attached
127.0.0.1:45999        offline transport_id:4
emulator-5554          device product:sdk_gphone16k_arm64 ...
```

The phone leaves the same row behind when its adbd answers the port but refuses this computer's key
(`SSLV3_ALERT_CERTIFICATE_UNKNOWN` in the server log) or when an earlier session was left half open.
Such a row is a transport adb holds, not a session: `shell`, install, and file work all fail on it and
the phone shows nothing connected.

The wireless panel used to present every Wi-Fi target in that list as connected, which produced the
report "it shows the connection but the phone does not connect" and also hid the fix, because the host
counted as connected and its Connect action disappeared. `AndroidWirelessPanel.Describe` now splits the
live list once for the whole panel:

* targets adb reports as `device` are Connected devices (each with Disconnect / Unpair);
* Wi-Fi targets adb lists that cannot accept work are shown as not connected, with the state adb
  reports, what to do about it, and their own Connect, which tries the ports the phone publishes now and
  falls back to the address adb lists.

`WirelessStatusLine`, the diagnostics' `connected` check, and the diagnose option all read that one
split, so no two parts of the panel can disagree about whether a phone is connected. Verified with the
production code against a live `offline` row: the panel reported no device connected, named
`127.0.0.1:45999` as `offline`, and offered Connect, where the same state previously read as connected.

`AndroidWirelessDebug.ReadStalledTransport` names the ambiguous case once a TCP connect to the endpoint
succeeds: reachable but not usable means the phone is not accepting this computer's key, so the app says
to Forget this computer on the phone and pair again instead of blaming the network. Verified against a
host listener the app can reach while adb reports `offline` (it produced that guidance) and against the
unreachable case (it makes no claim, because the port is simply not answering).

What no host-side change can fix, and the state the phone was in while this was re-checked: it advertises
`_adb-tls-connect._tcp` at 192.168.50.237:42857 through the operating-system resolver while `nc` reported
`Connection refused` on ten consecutive probes over 30 s, with `adb mdns services` empty, `ping` clean,
and `adb server-status` healthy (`LIBADBMDNS`, `mdns_enabled: true`). A phone that advertises an address
whose port accepts nothing has to be woken with Developer options > Wireless debugging open before any
host can connect to it.

### Reset Wi-Fi debugging: what can be cleared where

The wireless card has a **Reset Wi-Fi debugging** action
(`MainViewModel.ResetWirelessDebuggingAsync`) for the state that accumulates across attempts:

* every Wi-Fi transport is dropped (`adb disconnect <serial>` per target);
* the app's pairing history and recent-address list are cleared, so the next attempt pairs from scratch;
* the adb server is restarted, which also empties its cached mDNS list, but only when `adb devices -l`
  shows no USB device or emulator, because a restart drops every attached session.

What it deliberately does not touch: `adbkey` (the computer's key, shared with USB) and the phone's own
Paired devices list, which has no adb command. A phone that still refuses this computer needs
Forget or Revoke on the phone, which "Open Developer options on the phone" puts one tap away.

### Automatic reconnect while the app is open

A paired phone that goes quiet — screen off, Wireless debugging closed, a Wi-Fi drop — is reconnected on
its own once its debugging port answers again, so the user does not have to press Connect after waking
the phone. The rules live in `AndroidWirelessReconnect` (pure, unit-tested) and the attempt itself in
`MainViewModel`:

* Only hosts this session paired are retried. Pairing is the only state that makes a later connection
  possible without another code, and retrying a host the user never paired would be a surprise.
* A host adb already lists as `device` is never touched.
* Attempts are serialized and spaced: 15 s after the previous attempt, then 20 s, 30 s, and 60 s after
  repeated failures. A phone that wakes is therefore picked up within about a minute, while a phone that
  stays away is not probed in a tight loop (each attempt costs a merged discovery plus `adb connect`).
* Nothing is attempted while a publish or install runs, while a manual pair/connect is in progress, while
  the QR panel is waiting, or while an emulator launch is being awaited.
* A reconnect refreshes the target list silently, selects the phone, and writes one log line. A failed
  run writes one line, not one per attempt. No alert sound: the watcher is repairing a connection the app
  already made, not completing an action the user asked for.
* What it cannot do: pair a phone that either side forgot (that needs a code or a QR scan again), reach a
  phone whose Wireless debugging is switched off, or survive an app restart — this app's pairing history
  is per session.

### Forget and Revoke are phone-side, and a list that does not refresh is not a failed Forget

On the phone, "Forget" removes a paired computer and the paired-devices list often keeps showing the
entry until the Wireless debugging screen is reopened or the phone restarts. The thorough reset is
"Revoke adb debugging authorizations", which clears the phone's own store of debugging keys. The app
opens that screen for you and says both, and it notes the refresh, so the missing line under Paired
devices is not read as "the pairing still exists".

### Complete device deletion ("forget everything")

When a device is deleted from the app, all connection configuration for that host is wiped:

* The live adb connection is terminated (`adb disconnect`).
* The host is removed from the pairing history (`_pairedWirelessHosts`), so the next connection starts
  from an unpaired state and the pairing step is offered again.
* The host is removed from recent devices, so it no longer appears under "Recent devices."
* The context selection is cleared if it pointed at the deleted device.

This means reconnecting to the same device later is a completely fresh pairing + connect flow, with no
residue from the previous connection.

### macOS 15 and newer: Local Network access is a host prerequisite (26 October 2026)

Symptom: the app lists the phone, offers Pair and Connect, and every attempt fails while the phone is
fine. `adb pair` answers `error: protocol fault (couldn't read status message)`, `adb connect` answers
`failed to connect to '192.168.50.237:42857': No route to host`, and pairing a code the phone is showing
fails the same way. Nothing on the phone or in the app's own logic is wrong.

What the host says, measured on macOS 26.6.2 with platform-tools 37.0.1:

* `adb` from a plain shell reached the phone (`failed to connect to '192.168.50.237:5555': Connection
  refused`, i.e. the phone answered with a reset) and reached the gateway (`192.168.50.1:80` opened) at
  the same moment that the app's own adb server answered `No route to host` for both.
* The blocked address was a resolved ARP entry with an open port, so the failure is not ARP or a dead
  route: the packet never left the machine.
* macOS names the reason in its own log:
  `nehelper: Local network denied by preference for Rider (com.jetbrains.rider)` — 85 occurrences in two
  hours, plus `UserEventAgent: Got local network blocked notification: pid: 22197` (the running Rider).
  `log show --last 2h --debug --info --predicate 'eventMessage CONTAINS "denied by preference"'`
  reproduces it.
* adb is a client-server pair, and the **server** process makes the device connection, so what matters is
the Local Network decision of the process tree the server was started from: a server started by the app
(or by the IDE that launched the app) inherits that app's denial, while the same binary started from a
shell granted access reaches the phone. `adb kill-server` before the next attempt is therefore part of
the fix, not a superstition.

The app-side fix: a macOS bundle that connects to other devices on the local network must declare it.
`src/CodePublisher/CodePublisher.Desktop/macos/Info.plist` now carries
`NSLocalNetworkUsageDescription` and `NSBonjourServices` (`_adb-tls-pairing._tcp`,
`_adb-tls-connect._tcp`). Without the usage description macOS denies the app's LAN traffic silently and
never offers a switch for it, which is why the failure had no visible cause on the host.

What the user must still do, because it is a per-app consent and cannot be set from code:

* System Settings > Privacy & Security > Local Network: switch on the app that starts adb — **JetBrains
  Rider** when the app is run from the IDE (macOS logs that denial by name), and **Code Publisher**
  when the bundle is started on its own. Then relaunch it, so a fresh adb server inherits the grant.
* The failure text built from `AndroidWirelessDebug.ReadConnectFailure` names this cause for macOS 15 and
  newer, so the app no longer blames the phone or the network for it.

Not verified: a successful Wi-Fi session after the switch is turned on. The grant is the user's to give,
so the last step was left to them; everything up to and including the denial in the OS log is reproduced.

Unrelated and worth an update: Little Snitch 5.7's content filter refuses to start on this macOS
(`at.obdev.littlesnitch.networkextension: Started with error Not compatible with operating system
version`). It is not what blocks adb — the block is logged as an OS preference denial — but the filter is
broken on macOS 26 until Little Snitch is updated.
