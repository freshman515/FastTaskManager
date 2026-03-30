# FastTaskManager

一款轻量、快速的 Windows 任务管理器，支持进程管理、性能监控、服务控制、启动项管理与全局快捷键快速唤起。

## 功能

- **进程管理** — 查看、搜索、结束进程，支持按类别过滤
- **性能监控** — CPU、内存等实时性能数据
- **服务控制** — 查看和管理 Windows 服务
- **启动项管理** — 管理开机自启动程序
- **快速启动窗口** — 全局快捷键唤起，快速操作
- **系统托盘** — 最小化到托盘，后台常驻
- **主题切换** — 支持明暗主题

## 安装

前往 [Releases](../../releases) 页面下载最新版 `.msi` 安装包，双击运行按向导完成安装。

> 无需额外安装 .NET 运行时，开箱即用。

## 开发

**环境要求**

- Windows 10/11
- .NET 8 SDK

**本地运行**

```bash
git clone https://github.com/freshman515/FastTaskManager.git
cd FastTaskManager
dotnet run --project FastTaskManager.App
```

**本地打包 MSI**

```powershell
.\build-installer.ps1 -Version 1.0.0
```

**发布新版本**

```bash
git tag v1.x.x
git push origin v1.x.x
```

推送 tag 后 GitHub Actions 自动构建并发布 Release。

## 技术栈

- WPF / .NET 8
- CommunityToolkit.Mvvm
- Hardcodet.NotifyIcon.Wpf
- WiX Toolset v5（MSI 打包）
