# Where an app keeps its crash reports

Research date: 2026-10-20. Verified on macOS 26.6.2 with an Android 17 emulator (API 36,
`emulator-5554`) and an iOS 18.6 simulator (iPhone 16 Pro).

## The short answer

No platform puts an app's crash reports in one agreed place, and there is no standard folder
called `AppData/logs` or a standard file called `crash.bin`. That path is one app's own choice.

What is true everywhere is different: the operating system writes a crash report of its own for
every app that crashed. That report is the reliable source, and the app's own files are the second
source, which only exists when the app ships a crash reporter that writes files.

The scan in the Deploy section therefore looks in both places, and says which of them it could
read.

## What the platforms actually do

### Android

- The app's private data lives in `/data/data/<package>` (`/data/user/0/<package>`).
  `.NET MAUI` maps `FileSystem.AppDataDirectory` to `files/` inside it, and
  `FileSystem.CacheDirectory` to `cache/`. There is no `logs` folder in either: an app that
  writes one creates it itself.
- `adb` may only read that folder for a **debuggable** build, through `run-as <package>`.
  A released build answers `run-as: Package '<package>' is not debuggable`, and no other adb
  command may read it. This is the single most important fact for the app-data path.
- The **system** crash store is the DropBox service. `adb shell dumpsys dropbox --print <tag>`
  writes the report the system made. The tags that matter are `system_app_crash`,
  `data_app_crash`, `system_app_anr`, and `data_app_anr`. The report names the process, so the
  reports of other apps can be told apart. The service keeps the last 1000 entries.
- The **log buffer** (`adb logcat -d -v threadtime`) still holds the stack trace of the last
  crash, and it is the only source that is there for a native crash of a released build. The
  buffer is small and is overwritten, so an old crash is gone from it.
- `/data/tombstones` holds the native crash files, and it needs root.

### iOS

- The app's container holds `Library/Logs` next to the app's own data, and an app that writes a
  crash file puts it there. A **real device** exposes that container through
  `xcrun devicectl device info files --domain-type appDataContainer --domain-identifier <bundle id>`.
- The **system** crash reports are `.ips` files. On a real device they live in the
  `systemCrashLogs` domain of the same `devicectl` command. On a **simulator** the macOS writes
  them into `~/Library/Logs/DiagnosticReports/<ProcessName>-<date>.ips`, a few seconds after the
  crash.
- `devicectl` is Xcode 15 or newer, and it needs macOS. Its JSON is not a published schema, so
  the file listing is read with a parser that accepts the field names of several Xcode versions.

### macOS and Windows

- macOS writes the crash report into `~/Library/Logs/DiagnosticReports/<ProcessName>-<date>.ips`.
- Windows writes a memory dump into `%LOCALAPPDATA%\CrashDumps\<app>.exe.<pid>.dmp` when local
  dumps are turned on, and a text report into
  `C:\ProgramData\Microsoft\Windows\WER\ReportArchive\<app>\<...>\Report.wer`.

## What the scan in this app does

| Source | Android | iOS simulator | iOS device | macOS / Windows |
| --- | --- | --- | --- | --- |
| The app's own data folder | `run-as`, debuggable builds only | the container on disk | the `appDataContainer` domain | not read |
| The system's crash store | the DropBox service | `~/Library/Logs/DiagnosticReports` | the `systemCrashLogs` domain | DiagnosticReports, CrashDumps, WER |
| The device log | `logcat` | not read | not read | not read |

Every source is read only. Nothing is written to the device and no report is deleted.

## The names that mark a crash report

The scan recognizes the file names the common crash reporters and log writers use: `crash*`,
`*.crash`, `*.stacktrace`, `*.trace`, `*.dmp`, `*.hprof`, `*.ips`, a `*.log` whose name holds
`crash`, `error`, or `exception`, and any name that holds `crash`, `stacktrace`, or `exception`.
An ordinary `app.log` is not a crash report and is not listed.

The folders inside the app's data that are looked at are `files/logs`, `files/crash`,
`files/crashes`, `files/dumps`, `files`, `cache/logs`, and `cache` on Android, and
`Library/Logs`, `Library/Caches`, `Library/Application Support`, and `tmp` on iOS.

## Sources

- Android `dumpsys dropbox` usage: <https://developer.android.com/tools/dumpsys>
- Android app data and `run-as`: <https://developer.android.com/studio/command-line/adb#run-as>
- CoreDevice `devicectl` file domains:
  <https://developer.apple.com/documentation/xcode/devicectl-device-info-files>
- Apple crash reports: <https://developer.apple.com/documentation/bundleresources/inspecting-crash-reports>
- Windows Error Reporting: <https://learn.microsoft.com/windows/win32/wer/>
