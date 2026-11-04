# ShellX

> 面向 Windows 的 SSH 桌面工作台：终端、文件传输、端口转发、服务器监控与 MCP 集成在一个窗口里。

[![CI](https://github.com/coolqingcheng/shell-x/actions/workflows/ci.yml/badge.svg)](https://github.com/coolqingcheng/shell-x/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/coolqingcheng/shell-x)](https://github.com/coolqingcheng/shell-x/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/coolqingcheng/shell-x/total)](https://github.com/coolqingcheng/shell-x/releases)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D6)](https://github.com/coolqingcheng/shell-x/releases)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

![ShellX 主界面](docs/images/workspace-dark.png)

ShellX 使用 **Avalonia UI + .NET 10** 构建，界面布局参考 FinalShell：左侧是服务器状态监控，上方是多标签终端，下方是本地 / 远程双栏文件面板与命令面板，各分区均可拖动调整。

## 目录

- [特性](#特性)
- [下载与安装](#下载与安装)
- [快速开始](#快速开始)
- [开发](#开发)
- [已知限制](#已知限制)
- [文档](#文档)

## 特性

| 模块 | 说明 |
| --- | --- |
| 连接管理 | 文件夹分组与搜索、多标签、连接标识色、新建 / 导入 / 导出（不含密码与私钥）、连接前测试连接 |
| 终端 | xterm-256color、快捷命令与历史、右键复制粘贴、字体缩放、断线自动重连、标签独立重命名 |
| 文件管理 | 本地与 SFTP 双栏、按类型显示线条图标、拖拽上传下载、递归目录、Unix 权限编辑、内置文本编辑器 |
| 端口转发 | 本地 / 远程规则、同地址标签共享状态、TCP 连通检测、实时速度与流量动画、断线自动恢复 |
| 服务器监控 | CPU、内存、负载、进程列表、网络速率、磁盘挂载与容量（Linux `/proc`） |
| MCP 服务 | HTTP JSON-RPC 暴露本机命令执行，Bearer 鉴权、一键复制客户端配置、逐条审计日志与实时日志窗口 |
| 安全 | 服务器指纹校验（SHA-256）、Windows DPAPI 凭据保护、应用锁与超时自动锁定 |
| 发布 | Native AOT 单文件、Inno Setup 安装包、应用内自动更新（GitHub Releases） |

### 连接与终端

- 启动后先进入连接管理器，选中连接才会创建并显示主工作区窗口；`Ctrl+N` 新建连接，`Ctrl+W` 关闭标签。
- 新建 / 编辑连接时可以填主机、端口、账号、密码或私钥（默认打开用户 `.ssh` 目录），并点击**测试连接**确认凭据与指纹后再保存。
- 标签可单独重命名，不影响连接配置；同一连接可同时开多个标签。
- 快捷命令支持历史记录（`Alt+↑/↓`）与发送状态提示；终端支持右键复制选中内容、粘贴剪贴板内容。
- 断线后按设置的间隔自动重连；`设置 → 常规` 可关闭。

### 文件管理

- 左侧本地目录、右侧远程 SFTP 目录，双栏都支持 `F5` 刷新、`F2` 重命名、`Delete` 删除、`Alt+↑` 上级目录。
- 文件列表按扩展名显示线条图标与类型文字（图片 / 视频 / 音频 / 压缩包 / 代码 / 配置 / 表格 / 程序 / 数据库 …），工具栏提供更大尺寸的矢量导航图标。
- 上传、下载支持多选文件与目录，也可以直接从 Windows 资源管理器拖入；目录传输保留子目录、空目录和隐藏文件，合并前会确认，不跟随符号链接。
- 内置文本编辑器用于日常配置文件：读取上限 **2 MiB**、**256K** 字符、**10000** 行、单行 **8192** 字符；自动识别编码，`Ctrl+S` 保存保留原编码与 BOM，保存前比对修改时间与原始字节，避免覆盖外部改动。
- 权限对话框提供 rwx 与特殊权限位复选框及八进制数值；上传下载有任务列表、进度与取消。

### 端口转发

- 支持本地转发与远程转发；点击工具栏 `⇄` 新建或管理规则，已保存规则在连接时自动启动。
- 同一主机地址、SSH 端口和账号的标签共享隧道状态，任一标签启停都会同步；最后一个连接断开时释放监听。
- 工具栏 `⇄` 颜色反映状态：绿色连通、红色异常、黄色检测中 / 待验证、灰色停止，悬停可查看失败原因。
- 启动后立即检测目标 TCP，也可手动点击“检测”；后台每 10 秒只检查 SSH 与监听是否存活，**不会向目标反复建立空连接**。
- 隧道行有数据经过时显示流量徽标动画与实时速度（`↑ 1.5 MB/s ↓ 340 KB/s`），空闲时自动隐藏。

### 服务器监控

- 左侧面板展示运行时长、负载、CPU、内存、交换分区、进程列表、网络速率、磁盘挂载与容量。
- 依赖 Linux `/proc` 以及 `ps`、`df`、`hostname`；进程 CPU 取 `ps` 报告值，整体 CPU 由相邻 `/proc/stat` 样本计算。
- 指标无法读取时给出明确状态，终端与文件功能不受影响；点击进程表或内存表头可切换排序，排序在重启后保留。

### MCP 本地命令

连接编辑器中的“允许 MCP 执行本机命令”默认关闭。开启后 MCP 可以在运行 ShellX 的 Windows 本机执行命令（不经过 SSH），并按连接记录审计日志。

```text
POST http://127.0.0.1:8765/mcp
GET  http://127.0.0.1:8765/healthz
```

地址与 Bearer Token 在 `设置 → MCP` 中，首次启动自动生成；可一键复制通用客户端 JSON：

```json
{
  "mcpServers": {
    "shellx": {
      "type": "http",
      "url": "http://127.0.0.1:8765/mcp",
      "headers": { "Authorization": "Bearer <token>" }
    }
  }
}
```

提供 `list_mcp_connections` 与 `execute_local_command` 两个工具；每次执行、拒绝和失败都写入 `%LOCALAPPDATA%\ShellX\logs\mcp-audit-yyyyMMdd.jsonl`，主页右上角“MCP 日志”可实时逐行查看。服务默认只监听 `127.0.0.1`，对外开放前请自行加上 TLS 与访问控制。

### 安全

- 首次连接要求确认服务器指纹（SHA-256），确认后记录在 `known-hosts.json`。
- 只有勾选“记住凭据”才保存密码 / 私钥口令，使用当前 Windows 用户范围的 DPAPI 加密，无法直接复制到其他用户或电脑。
- 应用锁支持锁定密码与“锁屏超过指定时长才锁定”策略；默认日志不记录终端输入输出、凭据与文件正文。
- 服务器合法更换密钥时，请先通过可信渠道核对新指纹，再删除 `known-hosts.json` 中该主机的旧记录后重新连接。

## 下载与安装

前往 [Releases](https://github.com/coolqingcheng/shell-x/releases/latest) 下载对应产物：

| 产物 | 说明 |
| --- | --- |
| `ShellX-Setup-<版本>.exe` | Inno Setup 安装包，安装到 `%LOCALAPPDATA%\Programs\ShellX`（免管理员），含开始菜单与卸载入口 |
| `ShellX-<版本>-win-x64.zip` | 免安装便携包，解压即可运行 |

- 系统要求：**Windows x64**；Native AOT 单文件**无需安装 .NET 运行时**。
- 应用内更新：`设置 → 更新` → 检查更新 → 下载安装包 → 确认后立即更新并重启；可开启“启动时自动检查更新”。

## 快速开始

1. 下载安装包并安装（或解压便携包）。
2. 启动后在连接管理器点击 **新建连接**，填写主机、端口、账号与认证方式，用**测试连接**验证后再保存。
3. 双击连接进入工作区：左侧查看服务器状态，上方使用终端，下方管理文件与命令。
4. 需要转发端口时点击工具栏 `⇄` 添加规则，连接成功后会自动启动并检测。

常用快捷键：

| 快捷键 | 作用 |
| --- | --- |
| `Ctrl+N` / `Ctrl+W` | 新建连接 / 关闭标签 |
| `Ctrl+B` | 打开连接管理器 |
| `F5` / `F2` / `Delete` | 文件列表：刷新 / 重命名 / 删除 |
| `Alt+↑` | 文件列表：上级目录 |
| `Alt+↑` / `Alt+↓` | 命令输入框：历史 上一条 / 下一条 |

## 界面

**连接管理器** —— 文件夹分组、搜索、连接测试与配置导入导出：

![连接管理器](docs/images/connection-manager.png)

**文件管理** —— 本地与远程双栏，按类型显示图标与类型列：

![文件管理](docs/images/file-manager.png)

## 开发

### 环境要求

| 依赖 | 用途 |
| --- | --- |
| .NET SDK 10 | 构建与运行 |
| Visual Studio MSVC x64 工具 + Windows SDK | Native AOT 发布 |
| Inno Setup 6 | 构建安装包（`winget install JRSoftware.InnoSetup`） |
| Python + Paramiko | 本机 SSH 测试服务（仅集成测试需要） |

### 运行与构建

```powershell
# 开发运行
dotnet run --project src/ShellX.Desktop

# 构建 Windows x64 Native AOT 单文件（默认）
build.bat
# 产物：artifacts\release\win-x64-aot\ShellX.exe

# 直接调用底层脚本
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1 -Aot
```

AOT 产物只分发一个 EXE，首次启动会把内嵌的原生组件释放到 `%LOCALAPPDATA%\ShellX\native\win-x64\<内容版本>\` 并在后续启动复用。XAML 与代码使用编译绑定，JSON 使用源生成，以保证 AOT 兼容。

### 一键发布

```powershell
powershell -ExecutionPolicy Bypass -File scripts/release.ps1
```

流程：`VERSION` 版本号 +1 → AOT 构建并校验产物 FileVersion → 提交推送 → 推送 `v*` 标签 → GitHub Actions 自动产出安装包、免安装压缩包并发布 Release。

常用参数：`-Version 1.1.0` 指定版本、`-Message "说明"` 指定提交信息、`-NoPush` 只本地提交、`-NoTag` 不打标签；脚本会先关闭正在运行的 ShellX 再构建。版本号唯一来源是根目录 `VERSION` 文件，编译产物、安装包与窗口标题显示的版本均取自它。

### 测试

```powershell
dotnet test ShellX.slnx
```

SSH 集成测试使用仓库内的本机服务（随机回环端口、独立文件目录，不接触真实服务器）：

```powershell
python -m pip install paramiko
python tests/fixtures/ssh_server.py artifacts/ssh-fixture

# 另一个终端
$env:SHELLX_TEST_FIXTURE = "$PWD/artifacts/ssh-fixture/fixture.json"
dotnet test ShellX.slnx
```

未配置本机服务时，网络集成测试会自动跳过。测试覆盖凭据加密、配置并发与损坏恢复、会话释放、SFTP 操作与权限、双向端口转发与监听冲突、本地与远程文件删除、终端 ANSI / 中文 / 缩放 / 多标签等。

需要界面截图用于文档或排查时：

```powershell
$env:SHELLX_PREVIEW_PATH = "$PWD\artifacts\preview\.keep"
dotnet test tests/ShellX.Desktop.Tests -c Release
```

渲染结果输出到 `artifacts\preview\`。若开发版正占用默认输出目录，可用 `--artifacts-path artifacts\isolated-build` 使用独立构建目录。

### 项目结构

| 项目 | 职责 |
| --- | --- |
| `ShellX.Core` | 模型、接口、会话生命周期、路径与参数规则 |
| `ShellX.Infrastructure` | SSH.NET、SFTP、隧道、JSON 配置、Windows DPAPI |
| `ShellX.Desktop` | Avalonia、MVVM、终端控件适配、交互服务、DI 与日志组装 |

核心层不引用 SSH.NET，基础设施层不引用 Avalonia；ViewModel 只通过 Core 接口调用功能。完整设计见 [docs/architecture.md](docs/architecture.md)。

### 配置与日志

默认目录 `%LOCALAPPDATA%\ShellX`，开发与测试可用 `SHELLX_DATA_DIR` 指向独立目录。

| 文件 / 目录 | 内容 |
| --- | --- |
| `connections.json` | 连接、分组、私钥路径、隧道规则与快捷命令 |
| `settings.json` | 字体字号、锁定密码校验值、自动锁定策略、界面布局、监控排序等 |
| `known-hosts.json` | 已确认的服务器 SHA-256 指纹 |
| `credentials/` | 仅在勾选“记住凭据”时保存，由 DPAPI 保护 |
| `logs/` | Serilog 滚动日志与 MCP 审计日志 |
| `updates/` | 自动更新下载的安装包与待安装记录 |

配置损坏会明确报错并保留原文件，连接配置采用临时文件写入后替换。

## 已知限制

- 仅验证 **Windows x64**；其他桌面平台的凭据存储与发布尚未适配。
- 内置编辑器面向中小配置文件，超出字节 / 字符 / 行数 / 单行长度限制时提示改用外部编辑器。
- 单次目录下载上限为 100000 个项目、128 层；传输目录内的符号链接会跳过，下载不写入本地符号链接。
- SFTP 不可用时终端仍可使用，可通过重新连接重试 SFTP。
- 资源监控依赖 Linux `/proc` 与常见命令；非 Linux 或无权限账号会显示明确状态。
- 验证基于本机 SSH 测试服务与真实服务器样本，尚未覆盖全部发行版与输入法组合场景，详见 [docs/verification.md](docs/verification.md)。

## 文档

- [架构设计](docs/architecture.md)
- [验证记录与排查过程](docs/verification.md)

## 图标

应用图标为原创的“终端提示符 + 蓝色 X”设计，源稿见 `src/ShellX.Desktop/Assets/ShellX.svg`，并导出多尺寸 ICO / PNG。修改后执行 `python scripts/generate-icon.py` 重新生成（运行程序不依赖 Python）。

## 许可证

本项目采用 [MIT License](LICENSE) 开源。