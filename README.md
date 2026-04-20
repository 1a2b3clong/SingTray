# SingTray

Language:

- [English](README.md)
- [中文](README_CN.md)

SingTray is a Windows desktop controller for `sing-box` built with a clear split architecture:

- `SingTray.Service`: a real Windows Service and the only source of truth
- `SingTray.Client`: a normal-user tray GUI for daily interaction
- `SingTray.Shared`: shared contracts, models, constants, and path conventions

It is designed to be a maintainable desktop application, not a temporary one-process helper tool.

## Features

- Real Windows Service managed by SCM
- WinForms tray client with single-instance behavior
- Named Pipe IPC between tray and service
- Core and config import handled only by the service
- Separate install directory and data directory
- Inno Setup based installer
- Start Menu integration and Windows Search discovery
- Service auto-start on boot
- Tray auto-start on user login
- Runtime state persisted in `state.json`
- Separate `app.log` and `singbox.log`

## How To Use

After installation:

1. Start `SingTray` from the Start Menu, or let it start automatically after login.
2. Right-click the tray icon to open the menu.
3. Use the top status item to control runtime state:
   - `Running` -> Stop
   - `Stopped` -> Start
   - `Error` -> Start or Restart
   - `Starting / Stopping` -> disabled
4. Use `Import Config` to import a JSON config file.
5. Use `Import Core` to import a `sing-box` zip package.
6. Use `Open Data Folder` to inspect logs and state files.

Fixed behavior:

- The service starts automatically, but `sing-box` does not auto-start by default.
- Importing core or config does not auto-start or auto-restart `sing-box`.
- If `sing-box` is running, import is rejected with:
  `Please stop sing-box first.`

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
