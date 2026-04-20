# SingTray

Language:

- [English](README.md)
- [中文](README_CN.md)

SingTray is a Windows desktop controller for `sing-box` built with a clear split architecture:

- `SingTray.Service`: a real Windows Service and the only source of truth
- `SingTray.Client`: a normal-user tray GUI for daily interaction
- `SingTray.Shared`: shared contracts, models, constants, and path conventions

It is designed to be a maintainable desktop application, not a temporary one-process helper tool.

## How To Use

1. Download a Windows x64 `sing-box` core zip from the official repository:
   `https://github.com/sagernet/sing-box`
2. Start `SingTray` from the Start Menu, or let it start automatically after login.
3. Right-click the tray icon and use `Import Core` to import the downloaded zip package directly.
4. Use `Import Config` to import your `config.json`.
5. Use the first menu item to switch runtime state:
   - `SingTray - Stopped` -> Start
   - `SingTray - Running` -> Stop
   - `SingTray - Error` -> Start or Restart
   - `SingTray - Starting` / `SingTray - Stopping` -> disabled
6. Use `Exit` when you want to close the tray app. The client records whether `sing-box` was running, and on the next tray startup it tries to restore that previous state once.

Default behavior:

- `SingTray.Service` starts automatically with Windows
- `SingTray.Client` starts automatically after user login
- `sing-box` does not auto-start on a fresh install until you start it
- Importing core or config does not auto-start or auto-restart `sing-box`
- If `sing-box` is running, import is rejected with:
  `Please stop sing-box first.`

## Tray Status

Top runtime state:

- `Running`: `sing-box` is currently running
- `Stopped`: `sing-box` is currently stopped
- `Starting`: a start request is in progress
- `Stopping`: a stop request is in progress
- `Error`: the last start or runtime attempt failed
- `Unavailable`: the tray client cannot reach `SingTray.Service`

Import state hints:

- `Import Config` shows the current config file name when valid
- `Import Config` may show `Unconfigured`, `Waiting`, or `Error` when config is missing or not ready
- `Import Core` shows the imported core version when valid
- `Import Core` may show `Missing` or `Error` when the core is missing or invalid

## Installation Paths

Default locations:

- Program files: `C:\Program Files\SingTray\`
- Data files: `C:\ProgramData\SingTray\`

Installed behavior:

- `SingTray.Service` is registered as an automatic service
- `SingTray.Client.exe` is added to:
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- A Start Menu shortcut named `SingTray` is created

## Data Layout

```text
C:\ProgramData\SingTray\
  core\
    sing-box.exe
  configs\
    config.json
  logs\
    app.log
    singbox.log
  tmp\
  tmp\imports\
  state\
    state.json
```

What the files are for:

- `app.log`: service-side event log
- `singbox.log`: stdout/stderr from `sing-box`
- `state.json`: persisted runtime, core, and config state

## Import Rules

### Import Config

Workflow:

1. The tray copies the selected file into `tmp\imports\`
2. The service validates the file
3. Validation includes JSON parsing and, when available, `sing-box check`
4. On success, the service atomically replaces `configs\config.json`
5. On failure, the active config remains unchanged

### Import Core

Workflow:

1. The tray copies the selected zip into `tmp\imports\`
2. The service extracts it into a temporary directory
3. The service checks whether `sing-box.exe` exists
4. The service runs `sing-box.exe version` from the extracted content
5. On success, the service atomically replaces the whole `core\` directory
6. On failure, the active core remains unchanged

Validation is based on extracted content and executable behavior, not the original zip filename.

## Logging

Log files:

- `C:\ProgramData\SingTray\logs\app.log`
- `C:\ProgramData\SingTray\logs\singbox.log`

Current logging behavior:

- `app.log` is recreated on each service start
- `singbox.log` is recreated on each service start
- normal high-frequency `get_status` polling is not logged by default
- logs focus on important events, state changes, and real errors

## Project Structure

```text
SingTray.sln
  SingTray.Shared/
    Shared contracts, DTOs, enums, and path conventions
  SingTray.Service/
    Windows Service, pipe server, import logic, sing-box manager
  SingTray.Client/
    WinForms tray app, pipe client, poller, menu logic
  Installer/
    Inno Setup script and publish helper
```

Key files:

- `SingTray.Shared/AppPaths.cs`
- `SingTray.Shared/PipeContracts.cs`
- `SingTray.Service/Services/SingBoxManager.cs`
- `SingTray.Service/PipeServer.cs`
- `SingTray.Client/TrayApplicationContext.cs`
- `Installer/setup.iss`
- `Installer/build-release.ps1`

## Build

Build the full solution:

```powershell
dotnet build SingTray.sln
```

Run the tray client in development:

```powershell
dotnet run --project .\SingTray.Client\SingTray.Client.csproj
```

Run the service project in development:

```powershell
dotnet run --project .\SingTray.Service\SingTray.Service.csproj
```

## Build The Installer

Use `build-release.ps1` as the single entry point:

```powershell
.\Installer\build-release.ps1 -Version v0.1.0 -Mode self-contained
.\Installer\build-release.ps1 -Version v0.1.0 -Mode framework
```

Intermediate directories:

- `Installer\artifacts\self-contained\client\`
- `Installer\artifacts\self-contained\service\`
- `Installer\staging\self-contained\client\`
- `Installer\staging\self-contained\service\`
- `Installer\artifacts\framework\client\`
- `Installer\artifacts\framework\service\`
- `Installer\staging\framework\client\`
- `Installer\staging\framework\service\`

Output:

- `Installer\output\self-contained\`
- `Installer\output\framework\`

## Development Notes

- Keep process control inside `SingTray.Service`
- Keep the tray as a UI and IPC client only
- Keep pipe names and contracts inside `SingTray.Shared`
- Do not move runtime authority into the client
- Do not replace Named Pipe with localhost HTTP

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
