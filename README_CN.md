# SingTray

语言：

- [English](README.md)
- [中文](README_CN.md)

SingTray 是一个面向 Windows 的 `sing-box` 桌面控制器，采用明确的分层架构：

- `SingTray.Service`：真正的 Windows Service，是唯一真状态源
- `SingTray.Client`：普通用户态托盘 GUI，负责日常交互
- `SingTray.Shared`：共享协议、模型、常量和路径约定

它的目标不是一个临时小工具，而是一个结构清晰、职责分离、可安装、可维护的正式桌面程序。

## 如何使用

1. 先从 `sing-box` 官方仓库下载 Windows x64 内核压缩包：
   `https://github.com/sagernet/sing-box`
2. 从开始菜单启动 `SingTray`，或等待它在登录后自动启动。
3. 右键系统托盘图标，点击 `Import Core`，直接导入下载好的 zip 压缩包。
4. 点击 `Import Config`，导入你的 `config.json`。
5. 点击第一行状态项切换运行状态：
   - `SingTray - Stopped` -> Start
   - `SingTray - Running` -> Stop
   - `SingTray - Error` -> Start 或 Restart
   - `SingTray - Starting` / `SingTray - Stopping` -> 禁用
6. 点击 `Exit` 关闭托盘程序。客户端会记录退出前 `sing-box` 是否处于运行状态，并在下次托盘启动时尝试恢复一次之前的状态。

默认行为：

- `SingTray.Service` 会随 Windows 自动启动
- `SingTray.Client` 会在用户登录后自动启动
- 全新安装后，`sing-box` 默认不会自动启动，需手动点击 Start
- 导入 Core / Config 后不会自动启动或自动重启 `sing-box`
- 如果 `sing-box` 正在运行，导入会被拒绝，并提示：
  `Please stop sing-box first.`

## 托盘状态说明

顶部运行状态：

- `Running`：`sing-box` 当前正在运行
- `Stopped`：`sing-box` 当前已停止
- `Starting`：正在执行启动请求
- `Stopping`：正在执行停止请求
- `Error`：最近一次启动或运行失败
- `Unavailable`：托盘当前无法连接 `SingTray.Service`

导入状态提示：

- `Import Config` 正常时会显示当前配置文件名
- `Import Config` 也可能显示 `Unconfigured`、`Waiting`、`Error`
- `Import Core` 正常时会显示当前导入的 Core 版本号
- `Import Core` 也可能显示 `Missing` 或 `Error`

## 安装位置

默认安装和数据路径：

- 程序目录：`C:\Program Files\SingTray\`
- 数据目录：`C:\ProgramData\SingTray\`

安装后的行为：

- 注册 `SingTray.Service` 为自动启动服务
- 写入当前用户登录启动项：
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- 创建开始菜单快捷方式 `SingTray`

## 数据目录结构

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

文件说明：

- `app.log`：Service 自身事件日志
- `singbox.log`：`sing-box` 的 stdout/stderr
- `state.json`：持久化运行状态、Core 状态、Config 状态

## 导入规则

### Import Config

流程：

1. Tray 先把选中的文件复制到 `tmp\imports\`
2. Service 对文件执行校验
3. 校验包括 JSON 解析，以及可用时调用 `sing-box check`
4. 校验成功后原子替换 `configs\config.json`
5. 校验失败时保持正式配置不变

### Import Core

流程：

1. Tray 先把 zip 复制到 `tmp\imports\`
2. Service 解压到临时目录
3. 检查是否存在 `sing-box.exe`
4. 在临时目录中执行 `sing-box.exe version`
5. 校验成功后原子替换整个 `core\` 目录
6. 校验失败时保持正式 Core 不变

当前版本按“解压后的内容和可执行行为”做校验，不依赖原 zip 文件名。

## 日志

日志位置：

- `C:\ProgramData\SingTray\logs\app.log`
- `C:\ProgramData\SingTray\logs\singbox.log`

当前日志策略：

- `app.log` 在每次 Service 启动时覆盖重建
- `singbox.log` 在每次 Service 启动时覆盖重建
- 默认不记录高频 `get_status` 轮询
- 日志以关键事件、状态变化和真实错误为主

## 项目结构

```text
SingTray.sln
  SingTray.Shared/
    共享协议、DTO、枚举、路径约定
  SingTray.Service/
    Windows Service、Pipe Server、导入逻辑、SingBox 管理
  SingTray.Client/
    WinForms Tray、Pipe Client、轮询器、菜单逻辑
  Installer/
    Inno Setup 脚本和发布辅助脚本
```

关键文件：

- `SingTray.Shared/AppPaths.cs`
- `SingTray.Shared/PipeContracts.cs`
- `SingTray.Service/Services/SingBoxManager.cs`
- `SingTray.Service/PipeServer.cs`
- `SingTray.Client/TrayApplicationContext.cs`
- `Installer/setup.iss`
- `Installer/build-release.ps1`

## 如何编译

编译整个解决方案：

```powershell
dotnet build SingTray.sln
```

开发时运行 Tray：

```powershell
dotnet run --project .\SingTray.Client\SingTray.Client.csproj
```

开发时运行 Service：

```powershell
dotnet run --project .\SingTray.Service\SingTray.Service.csproj
```

## 如何发布

当前发布脚本会先分别输出 Client 和 Service，再合并到 staging 目录，避免多项目 publish 相互覆盖。

执行：

```powershell
.\Installer\build-release.ps1 -Version v0.1.0 -Mode self-contained
```

生成目录：

- `Installer\artifacts\client\`
- `Installer\artifacts\service\`
- `Installer\staging\`

当前发布参数：

- `Release`
- `win-x64`
- `self-contained = true`

## 如何打包安装器

统一使用 `build-release.ps1` 作为唯一入口：

```powershell
.\Installer\build-release.ps1 -Version v0.1.0 -Mode framework
```

输出文件：

- `Installer\output\self-contained\`
- `Installer\output\framework\`

## 开发说明

- 进程控制必须留在 `SingTray.Service`
- Tray 只做 UI 和 IPC 客户端
- Pipe 名称和协议统一放在 `SingTray.Shared`
- 不要把状态控制权搬到 Client
- 不要把 Named Pipe 改成 localhost HTTP

## 许可证

本项目采用 MIT License，详见 [LICENSE](LICENSE)。


