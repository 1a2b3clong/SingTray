# SingTray

Language: [English](README.md) | [中文](README_CN.md)

SingTray is a Windows tray controller for `sing-box`.

It runs `sing-box` through a Windows Service and provides a tray menu for start, stop, import, and status checks.

## Quick Start

1. Install SingTray.
2. Start `SingTray` from the Start Menu.
3. Right-click the tray icon.
4. Import a `sing-box` core zip with `Import Core...`.
5. Import your JSON config with `Import Config...`.
6. Click `Start`.

Download `sing-box` from the official project:

```text
https://github.com/SagerNet/sing-box
```

## Tray Menu

![SingTray tray menu](docs/images/tray-menu.png)

Status values:

- `Running`: sing-box is running
- `Stopped`: sing-box is stopped
- `Starting`: start request is in progress
- `Stopping`: stop request is in progress
- `Error`: last start or runtime attempt failed
- `Unavailable`: tray cannot connect to the service

Core values:

- Version number: core is ready
- `Missing`: core is not installed
- `Error`: core validation failed
- `Ready`: core exists but version text is unavailable

Config values:

- File name: config is ready
- `Unconfigured`: config is not installed
- `Waiting`: core is missing or invalid
- `Error`: config validation failed

## Import Rules

- Import is disabled while sing-box is running, starting, or stopping.
- Imported config keeps its original file name.
- The menu and the stored file use the same config name.
- Temporary import files under `tmp\imports` are cleaned after import attempts.
- Importing core or config does not automatically start or restart sing-box.

## Logs

Logs are stored in:

```text
C:\ProgramData\SingTray\logs\
```

Files:

- `app.log`: SingTray service events

Behavior:

- `app.log` is recreated when the service starts.
- sing-box stdout/stderr is not written to a log file.

## Data Folder

Default data folder:

```text
C:\ProgramData\SingTray\
```

Layout:

```text
C:\ProgramData\SingTray\
  core\
    sing-box.exe
  configs\
    <imported config name>.json
  logs\
    app.log
  state\
    state.json
  tmp\
    imports\
```

Use `Open Data Folder` from the tray menu to open this folder.

## Installed Components

Default install folder:

```text
C:\Program Files\SingTray\
```

Installed behavior:

- `SingTray.Service` runs as a Windows Service.
- `SingTray.Client` runs as the tray app.
- The service starts with Windows.
- The tray app starts after user login.

## Build

Build the solution:

```powershell
dotnet build SingTray.sln
```

Build installer:

```powershell
.\Installer\build-release.ps1 -Version v0.1.0 -Mode self-contained
.\Installer\build-release.ps1 -Version v0.1.0 -Mode framework
```

Installer output:

```text
Installer\output\
```

## License

MIT License. See [LICENSE](LICENSE).
