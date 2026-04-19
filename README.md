# SingTray

SingTray is a Windows desktop controller for `sing-box` with a split architecture:

- `SingTray.Service` runs as a real Windows Service and is the only source of truth.
- `SingTray.Client` is a normal-user tray app for daily interaction.
- `SingTray.Shared` contains shared contracts, models, and path conventions.

The goal is a clean, installable, maintainable Windows experience that feels closer to a real desktop product than a one-process helper tool.

## Features

- Real Windows Service managed by SCM
- WinForms tray client with single-instance behavior
- Named Pipe IPC between tray and service
- Core and config import handled only by the service
- Controlled data directory under `C:\ProgramData\SingTray`
- Installer based on Inno Setup
- Start Menu integration and Windows app discovery
- Service auto-start on boot
- Tray auto-start on user login
- Runtime state persisted in `state.json`
- Separate `app.log` and `singbox.log`

## How It Works

SingTray installs two executables:

- `SingTray.Service.exe`
- `SingTray.Client.exe`

The service is responsible for:

- managing the `sing-box` process
- validating and importing config/core files
- persisting runtime state
- exposing IPC commands over Named Pipe

The tray client is responsible for:

- showing current state
- sending `start`, `stop`, `restart`, `import_config`, `import_core`
- opening the data folder
- exiting the GUI cleanly

The tray does not directly control `sing-box`, and it does not bypass the service to modify active core/config files.

## Installation

Requirements:

- Windows 10 or Windows 11
- Administrator permission to install

Default install locations:

- Program files: `C:\Program Files\SingTray\`
- Data files: `C:\ProgramData\SingTray\`

Installed behavior:

- `SingTray.Service` is registered as an automatic Windows Service
- `SingTray.Client.exe` is added to:
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- A Start Menu shortcut named `SingTray` is created

## Daily Usage

After installation:

1. Start `SingTray` from the Start Menu, or let it auto-start after login.
2. Right-click the tray icon to open the menu.
3. Use the top menu item to control runtime state:
   - `Running` -> Stop
   - `Stopped` -> Start
   - `Error` -> Start or Restart
   - `Starting` / `Stopping` -> disabled
4. Use `Import Config` to import a JSON config file.
5. Use `Import Core` to import a `sing-box` zip package.
6. Use `Open Data Folder` to inspect logs and state files.

Important behavior:

- The service starts automatically, but `sing-box` does not auto-start by default.
- Import does not auto-start or auto-restart `sing-box`.
- If `sing-box` is running, importing core/config is rejected with:
  `Please stop sing-box first.`

## Data Layout

SingTray stores runtime data here:

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

What each file is for:

- `app.log`: service-side event log
- `singbox.log`: stdout/stderr from `sing-box`
- `state.json`: persisted runtime/core/config state

## Import Rules

### Import Config

Workflow:

1. The tray copies the selected file into `tmp\imports\`.
2. The service validates it.
3. Validation uses JSON parsing and, when possible, `sing-box check`.
4. On success, the service atomically replaces `configs\config.json`.
5. On failure, the active config remains unchanged.

### Import Core

Workflow:

1. The tray copies the selected zip into `tmp\imports\`.
2. The service extracts it into a temporary directory.
3. The service checks for `sing-box.exe`.
4. The service runs `sing-box.exe version` from the extracted content.
5. On success, the service atomically replaces the whole `core\` directory.
6. On failure, the active core remains unchanged.

Validation is based on extracted content and executable behavior, not the original zip filename.

## Logging

SingTray writes two logs:

- `C:\ProgramData\SingTray\logs\app.log`
- `C:\ProgramData\SingTray\logs\singbox.log`

Current behavior:

- `app.log` is recreated on each service start
- `singbox.log` is recreated on each service start
- normal high-frequency `get_status` polling is not logged by default
- logs focus on important events, state changes, and real errors

## Project Structure

```text
SingTray.sln
  SingTray.Shared/
    Shared contracts, DTOs, enums, paths
  SingTray.Service/
    Windows Service, pipe server, import logic, sing-box manager
  SingTray.Client/
    WinForms tray app, pipe client, poller, tray menu
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
- `Installer/publish.ps1`

## Build

Build the solution:

```powershell
dotnet build SingTray.sln
```

Run the tray client in development:

```powershell
dotnet run --project .\SingTray.Client\SingTray.Client.csproj
```

Run the service project as a normal process for development:

```powershell
dotnet run --project .\SingTray.Service\SingTray.Service.csproj
```

## Publish

The current publish helper creates separate outputs first, then merges them into a staging folder.

Prepare publish staging:

```powershell
.\Installer\publish.ps1
```

This produces:

- `Installer\artifacts\client\`
- `Installer\artifacts\service\`
- `Installer\staging\`

This avoids multi-project publish output conflicts.

Current publish settings:

- `Release`
- `win-x64`
- `self-contained = true`

## Build Installer

After running `publish.ps1`, compile the installer with Inno Setup:

```powershell
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" .\Installer\setup.iss
```

Output:

- `Installer\output\SingTray-Setup.exe`

## Notes for Development

- Keep process control in `SingTray.Service`
- Keep the tray as a UI client only
- Keep pipe names and contracts in `SingTray.Shared`
- Do not move runtime authority into the client
- Do not replace Named Pipe IPC with localhost HTTP

## License

No license file has been added yet.
