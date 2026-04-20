# SingTray Installer

`setup.iss` installs the published desktop app into `C:\Program Files\SingTray` and keeps runtime data in `C:\ProgramData\SingTray`.

`build-release.ps1` is the only entry point. It publishes `SingTray.Client` and `SingTray.Service` into `Installer\artifacts\<mode>\client|service`, copies them into `Installer\staging\<mode>\client|service`, and then runs Inno Setup against those staging subdirectories.
