# ShellX 架构设计

## 1. 产品范围

使用 C#、.NET 10、Avalonia UI 开发桌面 SSH 工具。同一套代码交付 Windows、macOS 与 Linux 三大主流桌面系统：核心层与界面层不依赖任何平台专有 API，平台差异集中在凭据存储、锁屏检测与安装包形态三处，并以适配器方式隔离。Windows 为完整验收平台，macOS 与 Linux 提供原生安装包并由 CI 持续构建。

- 左侧：连接分组、连接搜索、新增、编辑、删除和连接状态。连接管理器是应用启动后的第一个窗口，工具栏最左侧固定显示 ShellX 标识与版本号，窗口标题统一为 `ShellX · 连接管理器`，与工作区窗口的空状态品牌页保持一致。主工作区顶栏左侧同样显示 ShellX 标识，紧随其后显示当前版本号（11px 弱化色），再用分隔点引出左侧分区名 `服务器状态`，品牌为强调色、分区名为弱化色，两者层级不混淆。
- 右侧：多标签工作区；每个标签为 SSH 终端并支持拖动调整面板比例。文件管理器通过 `Window.Show()` 作为非模态独立顶层窗口打开，不设置 Owner、不调用 `ShowDialog`，因此不会禁用主窗口；文件管理器激活时还会把自身设置为 `DialogService` 的窗口上下文，使权限、确认和文件选择弹窗归属于文件管理器而不是终端主窗口；顶部按连接显示页签；同一连接可通过 ＋ 创建多个编号页签，每个 `FileManagerTabViewModel` 持有独立 `FileBrowserViewModel`，挂接到对应连接的同一 `IRemoteFileSystem` 会话。
- 风格：参考 VS Code 的暗色 / 亮色配色、活动栏、侧边栏、标签栏和底部状态栏。颜色、间距和控件样式集中管理。
- 认证：账号密码、私钥文件以及带口令的私钥。
- 隧道：本地端口转发、远程端口转发，支持保存规则、手动启停和错误状态。
- 文件：采用 SFTP，提供地址栏、前进/后退/上级目录、列表排序、隐藏文件开关、刷新、多选和右键菜单。
- 日常文件操作：新建文件/目录、文本编辑保存、重命名、删除、上传、下载、文件/目录属性及 Unix 权限修改。
- 文件传输：统一列表显示队列、方向、结果、字节/百分比进度、总体进度和取消操作；面板支持行方向 GridSplitter 拖动，收起后保留文件管理器右下角的传输托盘图标。

第一版围绕上述功能实现；完整资源管理器的系统集成、目录同步和复杂自动化不作为首版验收要求。

## 2. 技术方案

| 用途 | 方案 |
| --- | --- |
| 桌面 UI | Avalonia UI，原生 AXAML 界面 |
| 运行时 | .NET 10 |
| MVVM | CommunityToolkit.Mvvm |
| 依赖注入 | Microsoft.Extensions.DependencyInjection |
| SSH、SFTP、端口转发 | SSH.NET，通过基础设施层适配 |
| 日志 | Microsoft.Extensions.Logging 抽象，Serilog 滚动文件实现 |
| 本地配置 | System.Text.Json，版本化结构和原子替换写入 |
| Windows 凭据保护 | 当前 Windows 用户范围的 DPAPI；通过独立接口访问 |
| 测试 | xUnit；真实 SSH 集成测试使用显式提供的测试环境 |
| 跨平台发布 | Native AOT 单文件；Windows 用 Inno Setup，macOS 用 DMG + `.app`，Linux 用 AppImage |

Avalonia 采用经过兼容性验证的稳定版本，不单独升级与终端控件存在版本耦合的包。

终端渲染与 SSH 传输分别处理。使用 `Iciclecreek.Avalonia.Terminal 4.1.0` 配合 `Avalonia 12.1.4`，通过 `TerminalHost` 和外部 `IPtyConnection` 接口适配 SSH 输入、输出、终端尺寸和剪贴板。已用本机服务验证 ANSI、UTF-8 中文数据、程序化输入、窗口缩放和标签保留；实际 Linux 上 vim/top 与人工中文输入法体验仍需进一步验收。控件禁止自动启动本地进程和远端窗口操作。

## 3. 解决方案分层

采用三个运行项目，测试项目按测试对象拆分。

```text
ShellX/
├─ ShellX.slnx
├─ Directory.Build.props
├─ Directory.Packages.props
├─ src/
│  ├─ ShellX.Core/
│  │  ├─ Connections/
│  │  │  ├─ Models/
│  │  │  ├─ IConnectionRepository.cs
│  │  │  └─ ICredentialStore.cs
│  │  ├─ Sessions/
│  │  │  ├─ Models/
│  │  │  ├─ IRemoteSession.cs
│  │  │  ├─ IRemoteSessionFactory.cs
│  │  │  ├─ ITerminalChannel.cs
│  │  │  └─ SessionManager.cs
│  │  ├─ Files/
│  │  │  ├─ Models/
│  │  │  ├─ IRemoteFileSystem.cs
│  │  │  └─ FileOperationService.cs
│  │  ├─ Transfers/
│  │  │  ├─ Models/
│  │  │  └─ TransferQueue.cs
│  │  ├─ Tunnels/
│  │  │  ├─ Models/
│  │  │  └─ ITunnelService.cs
│  │  ├─ Security/
│  │  │  ├─ IHostKeyStore.cs
│  │  │  └─ HostKeyVerificationResult.cs
│  │  └─ Settings/
│  │     ├─ AppSettings.cs
│  │     └─ ISettingsStore.cs
│  ├─ ShellX.Infrastructure/
│  │  ├─ Ssh/
│  │  │  ├─ SshSessionFactory.cs
│  │  │  ├─ SshRemoteSession.cs
│  │  │  ├─ SshTerminalChannel.cs
│  │  │  ├─ SftpFileSystem.cs
│  │  │  └─ SshTunnelService.cs
│  │  ├─ Persistence/
│  │  │  ├─ JsonConnectionRepository.cs
│  │  │  ├─ JsonSettingsStore.cs
│  │  │  └─ JsonHostKeyStore.cs
│  │  ├─ Security/
│  │  │  └─ WindowsCredentialStore.cs
│  │  └─ DependencyInjection.cs
│  └─ ShellX.Desktop/
│     ├─ App.axaml
│     ├─ App.axaml.cs
│     ├─ Program.cs
│     ├─ Composition/
│     │  └─ ServiceRegistration.cs
│     ├─ Features/
│     │  ├─ Shell/
│     │  │  ├─ MainWindow.axaml
│     │  │  ├─ MainWindow.axaml.cs
│     │  │  └─ MainWindowViewModel.cs
│     │  ├─ Connections/
│     │  ├─ Workspace/
│     │  ├─ Terminal/
│     │  ├─ FileBrowser/
│     │  ├─ FileEditor/
│     │  ├─ Transfers/
│     │  ├─ Tunnels/
│     │  └─ Settings/
│     ├─ Controls/
│     │  └─ TerminalHost/
│     ├─ Services/
│     │  ├─ Dialogs/
│     │  ├─ Clipboard/
│     │  └─ LocalFilePicker/
│     ├─ Converters/
│     ├─ Behaviors/
│     ├─ Styles/
│     │  ├─ Colors.axaml
│     │  ├─ Typography.axaml
│     │  └─ Controls.axaml
│     └─ Assets/
├─ tests/
│  ├─ ShellX.Core.Tests/
│  └─ ShellX.Infrastructure.Tests/
├─ scripts/
└─ docs/
   └─ architecture.md
```

每个 `Features` 子目录就近放置对应的 View、ViewModel 和仅供该模块使用的界面模型，避免不同功能的实现散落在全局目录中。

### 项目职责与依赖

| 项目 | 职责 | 禁止出现的依赖或行为 |
| --- | --- | --- |
| ShellX.Core | 业务模型、接口、会话协调、文件操作规则、传输队列和应用配置模型 | 不引用 Avalonia、SSH.NET；不持有 Window、Dispatcher 或控件 |
| ShellX.Infrastructure | SSH.NET 适配、SFTP、隧道实现、配置读写、凭据保护 | 不操作窗口，不弹对话框，不依赖 ViewModel |
| ShellX.Desktop | 界面、命令、界面状态、交互服务、终端渲染、依赖组装 | ViewModel 不直接使用 SshClient/SftpClient，不自行读写配置文件 |

编译依赖方向：

```text
ShellX.Desktop ──────────→ ShellX.Core
       │
       └───────────────→ ShellX.Infrastructure ──→ ShellX.Core
                         仅在应用组装入口使用
```

基础设施实现 Core 中定义的接口。Desktop 在启动入口完成服务注册，功能代码通过构造函数依赖 Core 的接口。

## 4. MVVM 与界面拆分

```text
MainWindowViewModel
├─ ConnectionExplorerViewModel      连接树、分组、搜索
├─ WorkspaceTabs                    已打开标签集合
│  └─ WorkspaceTabViewModel          单个标签和会话状态
│     ├─ TerminalViewModel           终端状态与终端命令
│     ├─ FileBrowserViewModel        目录、选中项、导航历史
│     ├─ TunnelListViewModel         隧道规则与运行状态
│     └─ FileEditorViewModel[]       打开的文本文件和未保存状态
└─ TransferQueueViewModel            全局传输列表，按标签标识来源
```

- `MainWindowViewModel` 只管理应用外壳、标签选择和全局状态。
- 每个功能有独立的 View 和 ViewModel；命令使用 CommunityToolkit.Mvvm。
- 代码后置用于焦点、拖动、尺寸变化、文件拖放和终端控件接线。业务操作通过 ViewModel 命令进入 Core。
- 确认、文件选择和剪贴板通过界面服务封装，不把 Window 传入 Core。
- UI 集合更新回到 UI 线程；连接、目录读取和传输不阻塞 UI 线程。
- 连接失败、权限不足、磁盘错误等转换成可操作的中文提示，原始异常写日志。

## 5. 会话与资源生命周期

连接配置是可持久化的数据；会话是运行时资源。两者使用不同模型，同一连接配置可以打开多个相互独立的标签。

每个标签拥有一个逻辑 `IRemoteSession`：

```text
WorkspaceTab
└─ RemoteSession
   ├─ SSH 连接：ShellStream + 本地/远程端口转发
   ├─ SFTP 连接：目录与文件操作
   ├─ 本标签的传输任务
   └─ 本标签的取消令牌和事件订阅
```

SSH.NET 的 SSH 与 SFTP 客户端按独立连接管理，不假设它们共享同一个底层 TCP 会话。SSH 成功而 SFTP 失败时，终端仍然可用，文件面板显示独立错误并允许重试。

会话状态包括：未连接、连接中、已连接、重连中、已断开、失败。异步完成结果带会话身份校验，避免旧连接的回调覆盖新连接状态。

关闭标签时检查正在运行的传输，随后取消任务、关闭终端、释放该标签的 SFTP、监控和事件订阅，并释放隧道共享租约。同一地址还有连接时保留隧道传输连接，最后一个租约释放时停止隧道并关闭该 SSH 连接。应用退出时统一释放全部会话；不只删除界面标签。

重连创建新的会话资源，不自动重放终端命令。文件写入和传输失败不盲目重试，避免重复或覆盖操作。

## 6. 文件管理规则

- 使用 Unix 远程路径工具处理服务器路径，禁止用 Windows 的 `Path.Combine` 拼接远程路径。
- 文件列表包含名称、大小、类型、修改时间、权限和所有者/组；按权限展示可用操作。
- 上传下载使用流式读写，提供进度和取消；覆盖已有文件前明确确认。
- 重命名、删除、创建、编辑、改权限通过 SFTP 执行，不拼接 shell 命令。
- 文本编辑采用独立编辑面板，首版支持 UTF-8，检测二进制文件并设置大小上限；保存前检查远程文件是否已变化。
- 权限对话框提供所有者/组/其他的 rwx 复选框和八进制数值，保留并明确处理特殊权限位。
- 首版权限修改只作用于所选文件或目录；递归权限修改需单独明确范围。
- 递归下载/删除明确符号链接边界，默认不递归跟随符号链接，避免循环和跨目录操作；本地删除与远程删除同样只删除链接本身，目录逐项递归并在取消时停止。
- 删除远程文件不可依赖 Windows 回收站语义，对实际路径和操作范围做明确确认。
- SSH.NET 客户端访问和传输并发由会话服务协调，首版限制并发，避免同一个 SFTP 客户端被任意线程同时调用。

## 7. 隧道规则

- 本地转发：本地监听地址/端口 → 远端目标地址/端口。
- 远程转发：服务器监听地址/端口 → 本机可访问的目标地址/端口。
- 明确区分监听地址与目标地址，校验端口和规则冲突。
- 本地监听默认绑定 `127.0.0.1`；其他监听地址由用户显式指定。
- 远程监听能否绑定指定地址取决于 SSH 服务端策略，失败时显示服务端错误。
- 保存的隧道定义与运行中的隧道实例分开；Infrastructure 的 `SharedTunnelRegistry` 按规范化主机地址、SSH 端口、账号管理共享组，规则 ID 对应一份运行状态。不同端口或账号互不共享。
- 每个会话持有共享组租约；共享组统一管理 SSH 客户端与私钥的释放，首个会话的 SSH 连接独立保留为隧道传输，关闭首个标签仅关闭其 shell/SFTP。最后一个租约释放时才释放隧道和传输连接。传输异常后，在后台任务中选择仍连通标签的 SSH 替换传输；没有候选时由后续重连替换。共享组分别记录已初始化规则与仍需运行的规则，替换后恢复所有运行规则，并保留手动停止记录，已有标签继续接收新状态。
- 面板顶部提供**全部启动**与**全部停止**：分别拉起本连接所有未运行规则、停止所有正在运行或正在启动 / 检测的规则；单条失败不中断其余规则，处理后按隧道服务返回的最新状态刷新该行。
- 新连接自动启动未初始化的已保存规则，面板默认收起；工具栏图标聚合全部规则状态，异常优先显示红色，检测中/待验证为黄色，已连通为绿色，全部停止为灰色，悬停显示摘要和异常原因。已有共享组保留手动停止和失败状态，避免新标签撤销用户的停止操作或重复监听；任一规则失败不阻断 SSH 终端或其他规则。停止、检测、启动和替换传输串行处理。
- Core 的 `TunnelStatus` 按规则 ID 区分停止、启动、检测、TCP 可达、失败和待验证，独立保留监听是否运行、错误原因与检测时间。`ITunnelService.StatusChanged` 将后台结果关联到具体规则，界面不把隧道错误写成整个 SSH 会话的连接状态。
- Infrastructure 的 `TunnelTcpProbe` 用临时回环 SOCKS CONNECT 经同一 SSH 连接确认远端 TCP 目标打开成功，无需远端命令或安装程序；远程转发还检查本机目标和远端入口，并等待实际反向转发请求。探测完成后关闭临时端口，不发送应用层数据。
- 隧道服务仅在启动和手动检测时主动连接目标，后台每 10 秒检查 SSH 与监听器状态；实际请求到达时不再额外探测，避免不同监听端口指向同一目标时叠加空连接。目标恢复后的验证由手动检测触发，TCP 可达状态表示最近一次检测结果。来自实际转发的错误同样进入该规则，并在状态或原因变化时记录到应用日志；规则停止后的回调不能恢复旧状态，检测完成时不能覆盖并发收到的错误。最后一个共享连接断开时取消检测并释放端口。传输恢复会重新建立监听，已断开的业务连接由客户端重建。

## 8. 配置、凭据与日志

运行数据放在当前用户的应用数据目录中，具体路径由统一的路径服务解析：

```text
ShellX/
├─ connections.json       连接、分组、认证方式、私钥路径、凭据引用和隧道定义
├─ settings.json          主题、终端字体、面板比例等
├─ known-hosts.json       已确认的服务器指纹
├─ credentials/           经操作系统保护的凭据
└─ logs/                  滚动日志
```

- 密码和私钥口令默认只在当前会话使用，用户选择记住后交给 `ICredentialStore`；不以明文写入连接配置。
- 私钥默认只保存路径，不额外复制私钥内容。ShellX 配置备份只有在检测到已保存登录密码或私钥口令时才要求设置备份密码，并用 PBKDF2-SHA256 + AES-256-GCM 加密凭据；导入时解密并写回操作系统凭据存储。锁定密码、私钥文件内容和本机安全设置始终不导出。Xshell 导入会用当前 Windows 账户密钥校验并解密 `.xsh` 的 `Password` / `Passphrase`，成功后立即写入操作系统凭据存储、不进入连接 JSON；设置 Xshell 主密码时在默认密钥失败后提示输入一次主密码，私钥文件缺失则明确报错。
- 首次连接显示服务器指纹供用户确认；已保存指纹变化时阻止静默连接。SSH 和 SFTP 均执行主机密钥验证。
- Windows 首版采用 DPAPI 当前用户范围保护。将来支持其他系统时替换凭据实现；安全存储不可用时退回仅本次使用，不回退为明文。
- JSON 保存采用临时文件与替换、串行写入和结构版本字段；损坏文件保留以便恢复。
- 使用 `ILogger<T>` 记录会话生命周期、操作结果和异常，通过 Serilog 输出滚动文件。
- 默认不记录终端输入输出、密码、私钥口令、私钥内容和文件正文。

## 9. 开发与验收顺序

1. 终端验证：锁定兼容的 Avalonia/终端控件版本，验证 ANSI、UTF-8 中文、粘贴、Ctrl+C、窗口缩放、vim/top 和标签切换。
2. 项目骨架：建立三个项目、依赖注入、日志、主题和基本工作区布局。
3. 连接与终端：完成配置、密码/密钥认证、主机指纹、多标签及断开/关闭流程。
4. 文件管理：目录导航、文件操作、文本编辑、权限、上传下载与任务取消。
5. 隧道与交付：本地/远程转发、启停和退出释放、发布脚本及使用说明。

测试集中在会造成数据错误或资源泄漏的行为：会话释放、路径处理、文件写入冲突、权限转换、配置原子保存和任务取消。真实环境验收覆盖密码与密钥登录、终端交互、SFTP 操作和两个方向的端口转发。构建成功不等同于真实服务器验证通过。

## 10. 当前状态

首版以 Windows 为验收平台，现已把交付面扩展到 Windows、macOS 与 Linux：同一套代码经 Native AOT 产出三个平台的单文件可执行程序，并由 CI 打包为 exe / dmg / AppImage。已建立三个运行项目和 Core、Infrastructure、Desktop 三个测试项目，并实现首版界面及 SSH/SFTP/端口转发。上面的目录是架构职责示意；当前实际文件按功能合并部分模型、接口和仓储实现，尚未需要的独立应用服务文件没有创建。

后续按用户提供的 FinalShell 截图调整布局，增加 `Core/Monitoring` 指标模型与计算、`Infrastructure/Monitoring` Linux SSH exec 采样、`Desktop/Features/Monitoring` 监控面板和曲线。命令栏、会话内历史和持久化快捷命令放在 `Desktop/Features/Commands`，快捷命令模型放在 Core。目录树按需加载，不改变 SFTP 客户端的工作目录。终端普通文本高亮在 Desktop 的读流适配中处理，不修改远端配置、不改变发送的数据；ANSI 前景色、控制字符串及全屏缓冲区保留。

进程采样读取账号可见的全部进程，由 Monitor ViewModel 按 CPU 或 RSS 数值排序后显示前 8 项，避免内存排序仅作用于 CPU 前 8 项。进程的内存列同时给出占系统总内存的比例和绝对占用（按 KiB/MiB/GiB 自适应），两行显示以适配左侧窄面板。排序列与方向写入 AppSettings，经主窗口保存并同步到所有标签，刷新和重新连接不会重置。

Native AOT 适配使用 Avalonia 编译绑定和 System.Text.Json 源生成。Infrastructure 的可变持久化 DTO 与 Core 的不可变记录分离，保留旧配置中缺失字段的默认值，避免 .NET 10 源生成对 init 属性默认值的差异。DataGrid 使用显式列，保留本地/远程文件模型的公共属性以支持按列排序。终端控件还会反射调用已编译主题的 XAML 入口，因此在 TerminalHost 上显式保留该入口的公共方法，避免打开连接标签时因裁剪崩溃。

AOT 发布配置独立于普通单文件发布。Desktop 的 NativeBundle.targets 从当前 NuGet 解析结果取得 Windows x64 原生 DLL 和匹配版本的 OpenConsole 辅助程序，作为资源嵌入原生 EXE，并从发布文件列表移除这些独立组件。NativeDependencies 在 Avalonia 启动前按资源内容 SHA-256 确定缓存版本，使用命名互斥锁协调多实例、临时文件与原子替换释放组件，校验已有缓存后通过绝对路径预加载原生 DLL。缓存位于当前用户 LocalApplicationData；分发包只需一个 EXE，运行时仍需要可写缓存。第三方裁剪分析告警保留，不以构建配置代替原生运行验证。

文件页签使用本地/远程双栏：`Core/Files/LocalEntry.cs` 定义本地模型与 `ILocalFileSystem`，`Infrastructure/Files/LocalFileSystem.cs` 实现本地磁盘枚举、创建、重命名、递归删除和系统打开。Desktop 的 `LocalFileBrowserViewModel` 维护本地导航、筛选和选择；原 `FileBrowserViewModel` 保留远程操作，并协调双向传输与统一任务列表。两个 View 独立处理快捷键和选择事件；本地面板不依赖 SSH 会话。实际文件传输继续由 `IRemoteFileSystem` 完成，上传目标取右侧当前目录，下载目标取左侧当前目录。

目录上传由 `ILocalFileSystem.PlanDirectoryUploadAsync` 在后台扫描，生成包含目录、文件、总字节数和跳过链接数量的计划。Core 的 `DirectoryUploader` 编排目录创建、已有目录合并、文件上传和总进度，不引用 UI 或 SSH.NET。Desktop 负责合并确认、任务状态和取消。空目录与隐藏文件包含在计划内；本地链接不跟随，远端同名项目类型不兼容或为链接时明确失败，避免当作普通目录继续写入。

本地与远程文本读取、保存分别通过 `ILocalFileSystem`、`IRemoteFileSystem` 进入基础设施层；`ITextCodec` 在 Core 定义编码接口，Infrastructure 的 `TextCodec` 实现严格多编码解码、BOM/声明/字节特征识别和编码转换，不依赖编辑控件。`TextDocument` 保存原编码、BOM 信息、识别说明及有界原始字节快照；保存冲突检测比较原字节，手动重读不会破坏比较基准。未修改内容且编码不变时保留原字节，转换或修改时使用异常回退，不能编码的字符不替换为问号。原始字节和编码信息只随当前文档存在，不写入连接配置。

Desktop 的 `CodeEditor` 适配 AvaloniaEdit 12.0.0，显式使用 TextEditor 控件主题，提供行号、按可见行显示与输入/粘贴限制；`EditorSyntaxes` 提供随主题切换的 VS Code 风格 JSON、XML、Nginx、INI、YAML、Shell、SQL 高亮，规则为固定正则并设超时。文件名、路径与内容用于默认识别，可手动覆盖。`FileEditorViewModel` 管理文本、语法、读取/保存编码、原始字节重读和未保存状态，文件写入仍通过现有保存回调进入文件系统接口。手动重读前由 DialogService 确认放弃未保存文本，选择保存编码本身也计入未保存状态。

保存成功由 FileEditorViewModel 发布明确的成功状态，DialogService 显示绿色背景及保存时间；修改后清除成功外观。保存失败保留编辑内容，通知 DialogService 在编辑窗口内直接弹出具体原因。两个文件列表使用按内容计算的列宽，避免星号列在狭窄面板中限制拖动，支持水平滚动和拖动列边界。FileGridInteraction 共用双击边界重置自动宽度的逻辑；文件双击打开只响应数据行，避免双击表头误开文件。

设置页面的 SettingsViewModel 处理字体选择、时长单位转换和密码变更验证，持久化仍由主窗口通过 ISettingsStore 完成。AppSettings/StoredSettings 增加终端和编辑器字体、锁定校验值、自动锁定开关及分钟数，旧设置沿用默认值。Core 只定义 ILockPasswordHasher 接口；Infrastructure 使用随机盐、600000 次 PBKDF2-SHA256 和固定时间比较，Desktop 不自行实现密码算法，也不持久化原始密码。

Core 的 `ISystemSessionMonitor` 定义系统锁屏状态、带时间戳的通知和监听生命周期；Infrastructure 根据操作系统注入实现。Windows 使用 `WindowsSystemSessionMonitor` 订阅 `SystemEvents.SessionSwitch`，并通过 WTS 查询初始化当前用户会话的锁屏状态，退出时取消订阅。其他系统目前使用明确返回不支持及未知状态的 `UnsupportedSystemSessionMonitor`，后续可独立接入平台实现。窗口切换不影响这个单例监听器。

AppLockService 管理当前应用设置和锁定/校验状态。自动锁定默认只计算一次连续的系统锁屏时长，提前解锁清零；解锁通知会先补算时长，覆盖休眠期间没有定时检查的情况。取消“仅在系统锁屏后计时”后，沿用基于 TimeProvider 单调时间戳的界面闲置计时；SSH 输出和监控采样不参与。AppLockCoordinator 将系统通知转到 UI 线程，在每个应用窗口内容挂载、显示前添加 LockScreen，锁定时隐藏并禁用原内容、收起菜单和提示、暂存窗口快捷键，将焦点留在活动窗口的密码输入框。原控件保留在树中，不卸载终端，不释放 SSH 会话或编辑缓冲区；解锁后恢复内容、快捷键和焦点。新开的模态窗口同样受保护，Ctrl+L 和 Escape 在窗口隧道路由中处理，后台定时器每秒检查自动锁定条件。

SecretTextBox 继承标准 TextBox 主题并保持密码掩码，自定义 AutomationPeer 的 Value 读取为空，辅助功能写入仍遵守可编辑状态。终端字体通过 TerminalHost 的字符串属性显式创建 FontFamily；编辑器订阅设置变更并在对话框关闭时取消订阅，避免依赖运行时反射转换或残留事件引用。

Core 的 `TextEditorLimits` 仍定义 2 MiB、256K 字符、10000 行和单行 8192 字符上限，读取完成后通过检查才进入 Desktop 编辑框，输入/粘贴与保存时也校验。右键选行逻辑由两个文件面板共用，文本编辑命令只对单个普通文件启用。本地系统默认打开入口保留，外部编辑器自动回传尚未实现。

运行数据默认位于 `%LOCALAPPDATA%/ShellX`。上传和下载均支持普通文件和递归目录，内置文本编辑支持上述范围的 Unicode 和常见旧编码；目录同步留待后续扩展。窗口大小与拖动后的分区比例暂不持久化。代码通过本机 SSH 协议服务进行联调；实际用户服务器、vim/top 和人工输入法体验尚未验证。具体运行与验收说明见 README.md 和 verification.md。

参考：

- Avalonia MVVM：https://docs.avaloniaui.net/docs/fundamentals/the-mvvm-pattern/
- Avalonia 依赖注入：https://docs.avaloniaui.net/docs/app-development/dependency-injection
- SSH.NET：https://github.com/sshnet/SSH.NET
- 终端控件候选：https://github.com/tomlm/Iciclecreek.Avalonia.Terminal


主题由 Core 的 AppTheme 配置和 Infrastructure 的 StoredSettings 持久化；Desktop 的 ThemeService 只负责切换 Avalonia RequestedThemeVariant。Theme.axaml 使用语义资源及 Dark/Light 字典，视图使用 DynamicResource；终端、编辑器、弹窗和图表使用同一组资源。编辑器高亮画刷按 TextView 当前主题取色并重绘；普通终端文本的自动高亮适配当前主题，服务器已有 ANSI 前景色仍予保留。切换主题不会重建控件或重连 SSH。

FileDragDrop 在文件行上识别带阈值的拖动，保留多选；原生 DataTransfer 只携带短期选择令牌，来源、会话版本和文件快照保存在进程内。远端选择固定原会话，重连后失效；只在同一工作区的对侧面板接受内部传输，外部 File 格式仅在远程面板接受。目标为释放时面板的当前目录，使用 Copy 效果；锁定、断线或原令牌过期时拒绝。UI 拖放层不自行进行文件读写，继续调用 FileBrowserViewModel 的现有传输入口。

递归下载编排在 Infrastructure/Files/DirectoryDownloader，使用 IRemoteFileSystem 读取远端树并在落盘前验证本地名称、冲突和目标路径，跳过远端符号链接，拒绝本地重解析点。合并确认仍由 Desktop 提供回调，目录扫描和传输共享同一个取消令牌；总进度复用 TransferViewModel。每个文件仍由 SFTP 适配器先写临时文件、完成后原子替换，取消目录任务可能保留此前已完成的文件。

Assets/ShellX.svg 为图标源稿，脚本导出 PNG 与多尺寸 ICO；ApplicationIcon 嵌入 Windows PE 图标资源，AvaloniaResource 供窗口、任务栏和页面加载，AOT 与普通单文件版使用相同资产。


连接管理器由顶部文件夹按钮 / Ctrl+B 打开的独立模态窗口承载，窗口复用 MainWindowViewModel 的连接目录状态；选择连接关闭管理器后调用可并发的新标签连接命令，密码和主机指纹确认仍走原流程。左侧不再承载连接列表，只绑定 SelectedTab.Monitor；右侧工作区仍保留所有标签控件并切换可见性，切换不卸载终端。关闭当前标签时先保存其选中状态再移除，避免 ListBox 自动清除 SelectedItem 导致整个工作区空白。

Core 的 ConnectionCatalog 定义连接及文件夹快照，ConnectionFolders 处理路径规范、祖先展开及名称校验；ConnectionProfile.Group 继续保存文件夹路径，兼容原有平面分组。Infrastructure 的 connections.json 在保留版本 1 的基础上增加 Folders 字段，保存空目录和多层级目录；旧 SaveAsync 只更新连接并保留 Folders，目录和连接变更通过一次原子快照写入。Desktop 的连接目录编辑由目录门锁串行提交，文件夹重命名同步更新后代和连接路径；非空目录不能直接删除。连接编辑窗口提供文件夹选择，新建连接默认使用管理器当前文件夹。嵌套弹窗使用当前活动对话框作为所属窗口，继续受全局锁定保护。

Core 的 ConnectionEndpoint 统一按规范主机名、SSH 端口、账号标识共享边界，隧道与监控使用一致的规则，不进行 DNS 别名归并。界面布局属于全局偏好，MonitorPaneWidth、TerminalPaneStar、LocalPaneStar 和 RemoteTreeWidth 保存到 settings.json；各拖动条在 DragCompleted 后写入，主窗口、当前和后续标签订阅同一 LayoutChanged 事件恢复，右上角重置命令只恢复布局字段并保留主题、字体和安全设置。Desktop 的 SharedMonitorRegistry 为每个地址维护同一个 ServerMonitorViewModel 和唯一轮询循环，各标签持有租约并提供各自 IServerMonitor 来源；终端和 SFTP 不共享。采样借用一个仍连通标签的 SSH exec 通道，不新增监控专用 SSH 连接。关闭来源标签先取消并等待其正在执行的采样，再释放 SSH，之后使用其他标签来源；最后一个来源断开时停止轮询，最后一个标签关闭时移除共享组。网卡选择、曲线和排序随共享模型保留；磁盘导航按当前选中标签路由。切换连接配置地址后，重连时重新取得对应地址的监控租约。


连接标识色使用 ConnectionProfile.AccentColor 保存不透明 #RRGGBB 或空字符串。Core 校验格式，Infrastructure 的 StoredConnection 源生成 DTO 保留旧配置空值默认和颜色字段，连接编辑器 Build 显式带回颜色；现有 with 更新、文件夹移动和快捷命令保存继续保留该值。配色不进入 ConnectionEndpoint，因此不会拆分原有共享监控或隧道。

Desktop 的 ConnectionAccentChoice 提供柔和预设和自定义输入；ConnectionAccentBorder 根据控件 ActualThemeVariant、用途和标签选中状态派生颜色：Indicator 为顶部色线和列表色条，Tab 为选中/未选中标签底色，Frame 为终端四边描边。自定义颜色显示时限制饱和度和明度，底色与边框分别混入主题背景，避免鲜艳大色块。终端头部、内容和命令输入区共同置于 Frame 的 2px 边框内，最大化仍只改变原布局行高。标识色和主题变化仅重绘这些 Border，不替换 TerminalHost 或 SSH 会话。WorkspaceTabViewModel 暴露 AccentColor 并在配置更新时通知，所有同配置标签立即更新。


标签临时名称只保存在 WorkspaceTabViewModel 的内存字段中，显示名优先使用临时名；连接配置更新、切换和重连均保留临时名。新标签不继承，关闭标签不保存。路径栏统一使用 TextBox.file-path 样式，30px 高、最小高度为 0、控件和文本分别垂直居中，保持长路径左对齐和横向编辑。

自动重连配置 AutoReconnectEnabled / ReconnectIntervalSeconds 由 Core 定义、StoredSettings 源生成 DTO 持久化，旧配置默认开启、间隔 5 秒。Desktop 的标签拥有唯一的可取消重试循环，只有成功连接后的意外断线启动；复用当前会话的凭据，每次失败后按配置等待，手动断开、关闭和关闭开关取消等待及正在建立的连接。主机指纹仍走原验证，身份拒绝后停止自动重试。首次连接失败仍由用户决定是否重试。

SessionTerminalChannel 是 Desktop 的终端适配层，把 SSH 输出和本地带时间的连接日志合并到有容量限制的队列；终端控件始终连接此稳定通道，SSH 源可替换，因此重连不会替换终端缓冲区。源读取结束也报告断线；关闭源先取消读取，再释放 SSH，最后等待读取结束，避免旧源与新源混写。终端输入仅转发给当前源，日志仅进入本地显示通道，不作为命令发送。断线后取消文件传输，成功重连重新接入 SFTP、共享监控和隧道生命周期；已经终止的远程 Shell 进程不会恢复。
## 11. 安装包与自动更新

安装包由 Inno Setup 6 描述（`scripts/installer/ShellX.iss`），只打包 AOT 单文件产物，安装到当前用户的 `%LOCALAPPDATA%/Programs/ShellX`，免管理员，含开始菜单、可选桌面快捷方式和卸载项。版本号的唯一来源是仓库根目录的 `VERSION` 文件（发布时由 `scripts/release.ps1` 自动递增），`Directory.Build.props` 通过 `ShellXVersionFile`/`ShellXVersion` 读取它并写入 `Version`/`AssemblyVersion`/`FileVersion`；`scripts/build-installer.ps1` 优先读取同一文件，`scripts/publish.ps1` 以 `-p:ShellXVersion=` 覆盖，保证 EXE、安装包与发布标签三者版本一致。版本号不再硬编码，因此产物版本会随 `VERSION` 文件变化。

更新走 GitHub 公开仓库的 Releases。Core 定义 `IUpdateService`、`UpdateRelease`、`UpdateAsset` 和版本比较 `UpdateVersion`（兼容 `v1.2.3`、`1.2.3`、`1.2.3-beta`）；Infrastructure 的 `GitHubUpdateService` 用源生成 JSON 解析 `releases/latest`，优先选择名称含 `Setup` 的 `.exe` 资产，并提供带进度的流式下载。Desktop 的 `UpdateManager` 负责检查、下载、待安装记录（`%LOCALAPPDATA%/ShellX/updates/pending.json`）以及拉起更新引导进程；`UpdateBootstrapper` 在 `Program.Main` 最前面拦截 `--apply-update` 参数，等待主进程退出后静默运行安装包并按需重启，主程序退出时同样检查待安装更新，实现“关闭即更新”。设置页“更新”页签只保留一个“检查更新”按钮和“启动时自动检查更新”开关：检查到新版本就下载安装包，下载完成后弹出“发现新版本”确认框；确认框由 `UpdateManager.BuildUpdatePrompt` 统一生成，展示当前版本、新版本、GitHub Release 的 `body` 更新说明与发布链接，确认即立即更新并重启，取消则清除待安装登记、退出时不再自动安装。仓库地址固定取内置默认值 `coolqingcheng/shell-x` 并保留原配置值。

发布流程：`.github/workflows/release.yml` 在推送 `v*` 标签时编译 AOT 产物、安装包和免安装压缩包并发布 Release，`ci.yml` 负责提交与 PR 的构建测试。敏感信息不入库：连接配置、凭据、设置与日志都存放在 `%LOCALAPPDATA%/ShellX`，仓库内只有源码、脚本和文档。

### 跨平台打包

三个平台共用同一条 `dotnet publish -p:PublishAot=true` 产线，差异只在“把可执行文件装进什么容器”：

| 平台 | 容器 | 脚本 | 依赖 |
| --- | --- | --- | --- |
| Windows x64 | Inno Setup 安装程序 + 便携 zip | `scripts/build-installer.ps1` | Inno Setup 6 |
| Linux x64 | AppImage（主）+ tar.gz（免安装） | `scripts/build-appimage.sh` | `squashfs-tools`、`desktop-file-utils`、AppImageKit `appimagetool` |
| macOS arm64 / x64 | DMG（主）+ `.app` zip（免安装） | `scripts/build-dmg.sh` | `hdiutil`、`iconutil`、`sips`（macOS 自带） |

`build-appimage.sh` 把发布目录整理成标准 AppDir：`AppRun` 用 `readlink -f` 定位自身所在目录再 `exec` 可执行文件，根目录放 `shellx.desktop` 与 `shellx.png`（`.desktop` 的 `Icon=` 必须与图标文件名一致），最后交给 `appimagetool` 封装。CI 通常没有 FUSE，因此运行 `appimagetool` 时设置 `APPIMAGE_EXTRACT_AND_RUN=1` 让其自解压执行；工具下载到临时目录并在退出时清理，避免打包失败时残留在发布产物里。

`build-dmg.sh` 组装标准 `ShellX.app`：`Contents/MacOS/ShellX` 为可执行文件，`Contents/Info.plist` 写明 `CFBundleIdentifier`、`CFBundleExecutable`、`CFBundleShortVersionString` 与 `LSMinimumSystemVersion`，`Contents/Resources/ShellX.icns` 由 512×512 应用图标经 `sips` 多尺寸化后用 `iconutil` 生成。打包前做一次 ad-hoc 签名（`codesign --force --deep --sign -`）以减少“已损坏”提示，但未做 Apple 公证与开发者签名，用户首次打开仍需手动放行或清除隔离属性。DMG 用 `hdiutil create -format UDZO` 生成，卷内含 `/Applications` 符号链接以便拖拽安装；`.app` 另用 `ditto` 导出 zip 作为免安装版本。

CI 与本地使用同一套脚本：`ci.yml` 在 Ubuntu / macOS runner 上除 AOT 发布外还执行打包步骤，使打包脚本的破坏能在合并前暴露，而不是等到打标签才失败。
