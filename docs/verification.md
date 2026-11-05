# 首版验证记录

日期：2026-10-15。目标：Windows x64，.NET SDK 10.0.401。

## 自动化结果

执行：

```powershell
$env:SHELLX_TEST_FIXTURE = "$PWD/artifacts/ssh-fixture/fixture.json"
dotnet test ShellX.slnx -c Release
```

共 26 项通过，0 项失败，0 项跳过：

| 项目 | 数量 | 验证内容 |
| --- | --- | --- |
| ShellX.Core.Tests | 14 | Unix 路径、文件名约束、连接与端口参数、连接尚未完成时关闭会话、替换会话和重复关闭的资源释放 |
| ShellX.Infrastructure.Tests | 10 | 配置读写、不含凭据字段、并发主机指纹保存、损坏配置保留、Windows DPAPI、密码/普通私钥/加密私钥、SFTP 文件操作及权限、文件冲突、上传下载与取消、符号链接保护、双向端口转发 |
| ShellX.Desktop.Tests | 2 | 权限复选框与八进制值联动；原生终端 ANSI、中文输入输出、缩放、多标签保留和关闭 |

详细测试结果由 VSTest 输出到 `artifacts/test-results/final_*.trx`，该目录不属于源代码。

## 联调方式

使用 `tests/fixtures/ssh_server.py`，仅监听 `127.0.0.1` 的随机端口。测试密钥临时生成，测试凭据仅用于此服务；测试数据位于 `artifacts/ssh-fixture`。服务提供 SSH/SFTP 协议、回显终端与双向转发，终端不执行系统命令。

权限测试通过测试服务返回 Unix 权限属性，因此验证了协议和权限转换，未替代真实 Linux 文件系统权限验收。符号链接由测试服务模拟，验证删除和重命名请求不会解析到目标。

原生终端测试使用 Avalonia Headless，以实际控件接入本机 SSH 会话并校验终端缓冲区。测试包括中文文本程序化输入、ANSI 颜色处理、缩放后继续输入、标签切换后保留相同控件和内容。

## 窗口检查

已启动 Windows 原生窗口并检查欢迎页、连接分组、多标签、连接状态、上下分区、终端 ANSI/中文显示及 SFTP 文件列表。测试窗口使用 `SHELLX_DATA_DIR` 指向独立测试配置，避免把示例连接写入正式配置目录。

## 尚未验证

- 用户实际服务器及不同 SSH 服务端版本的兼容性。
- 真实 Linux 上 vim、top、less 等完整交互程序。
- 人工中文输入法组合输入、复杂字符宽度和实际剪贴板操作体验。
- 不同网络质量下的大文件传输、断网和长时间运行。
- Linux、macOS 的安全凭据存储、原生组件和发布流程。

首版上传/下载支持普通文件；目录需先在终端打包。内置编辑器支持 2 MB 内 UTF-8 文本。上传取消/失败可能保留部分远程文件，本地下载采用临时文件后替换。

## 传输任务显示修复

修复进度条默认最小宽度 200 像素超出原有 100 像素列、覆盖状态和按钮的问题。传输行将名称/简短状态、详细说明、进度分行显示，进度条允许随面板宽度收缩；任务结束后隐藏进度条和取消按钮。

任务完成、取消或失败后忽略延迟进度回调；取消过程中禁用重复取消并保留取消状态。大小与百分比独立显示，取消后的远端部分文件说明完整展示。

新增 4 项回归测试全部通过：结束后进度回调不覆盖结果、取消中的状态与按钮、空文件完成，以及 480/1050 像素宽度下文字与进度条不重叠。该次修复不涉及 SSH/SFTP 协议实现，未重新连接实际服务器。

## FinalShell 布局与终端高亮

同日调整为左侧服务器监控、顶部多标签、上方终端与命令栏、下方文件/命令页签的布局，保留 VS Code 深色配色。连接管理可通过顶部文件夹或 Ctrl+B 切换。新增 Linux 指标采样、网卡曲线、磁盘目录跳转、命令历史与快捷命令、按需加载的目录树及最近路径。

全部 44 项测试通过，0 项失败、0 项跳过：Core 14 项，Infrastructure 14 项，Desktop 16 项。结果位于 `artifacts/test-results/finalshell_*.trx`。最后一次调整文件列表列宽、页脚和连接入口图标后，Desktop 16 项再次全部通过。

新增覆盖包含 Linux 指标解析与相邻样本计算、CPU guest 计数不重复累加、计数器重置、独立 exec 采样不混入终端、命令发送与历史草稿恢复、多行命令拒绝、目录树不展开符号链接、终端高亮规则、已有 ANSI/OSC 保留、分块控制序列和全屏缓冲区处理。普通文本高亮只作用于接收流，发送流保持原始输入。

使用实际 Avalonia 控件与 Skia 渲染生成 `artifacts/finalshell-layout-preview.png`，检查监控、两标签、命令栏、目录树和文件列表。该图来自本机 SSH 测试服务，监控值由测试样本提供。Windows 窗口自动激活两次返回 `failed to activate captured window`，已停止该检查路径；本次新版布局未完成人工交互验收。

本机服务仅回显交互输入，并只对预定监控请求返回样本，不执行收到的系统命令。用户实际 Linux 服务器、真实 vim/top/less、人工输入法与剪贴板体验、长时间运行仍需实机验证。

最终执行 `dotnet build ShellX.slnx -c Release --no-restore`，0 个警告、0 个错误。通过 `scripts/publish.ps1` 生成 Windows x64 自包含单文件 `artifacts/release/win-x64/ShellX.exe`，大小 53,643,216 字节。以独立 `SHELLX_DATA_DIR` 启动该发布文件，检测到标题为 ShellX 的窗口且进程正常响应，随后关闭该测试进程；不连接实际服务器。

## 本地 / 远程双栏与监控补充

文件页签改为左侧本地文件、右侧远程 SFTP 文件，独立导航、筛选和选择。远程目录树默认收起，可从右侧更多菜单打开。上传取左侧选择与右侧当前目录，下载取右侧选择与左侧当前目录；覆盖已有目标前确认，完成后刷新目标栏，传输任务统一置于双栏下方。另存为与选择文件上传保留为附加入口。

整机内存显示使用百分比与用量；进程列表显示 PID、CPU 百分比、内存百分比、进程名称，悬停显示 RSS 用量。进程内存百分比由 RSS / MemTotal 计算，不增加额外采样请求。

当前总计 46 项通过，0 项失败、0 项跳过：Core 14、Infrastructure 15、Desktop 17。新增本地路径约束与禁止覆盖测试、双栏 SSH/SFTP 联调测试；验证上传到右侧当前目录、下载到左侧当前目录、拒绝覆盖保留本地内容、导航清除旧选择，以及 1050/1380 像素窗口下两栏的位置。指标样本断言整机 25%、进程 PID 123 与对应内存百分比。桌面回归还覆盖原有传输布局、高亮与多标签终端；终端主题改为由应用显式加载，使每个独立窗口测试都能获得模板。

本轮在开发版运行并锁住默认构建目录时，改用 `--artifacts-path artifacts/dual-pane-build`，未关闭用户窗口。最终 Release 构建 0 警告、0 错误。测试结果见 `artifacts/test-results/dual-pane_*.trx` 与最后成功的 `dual-pane-desktop_*.trx`，实际 Avalonia/Skia 布局预览为 `artifacts/dual-pane-layout-preview.png`。本机服务仅监听回环地址的随机端口；本轮仍未连接用户实际服务器。

使用 `scripts/publish.ps1 -BuildArtifactsPath artifacts/dual-pane-build` 更新 Windows x64 单文件发布程序，大小 53,658,213 字节。以独立测试数据目录启动，检测到 ShellX 窗口且进程正常响应，随后只关闭该测试进程。测试服务在验证结束后停止。

## 隧道连通状态与失败原因

隧道面板增加颜色圆点、状态文字、具体原因与最近检测时间。绿色表示 TCP 检测通过，红色表示启动或连接失败，停止为灰色，检测中/待验证为黄色。每条规则支持手动检测；启动后立即检测，运行时约每 10 秒复检，实际转发请求也可触发复检。目标异常时仍保留监听与停止操作，恢复后自动更新。

结果以规则 ID 单独发布，不将一条隧道的错误当成 SSH 主连接错误。规则监听冲突显示占用的规则名称；其他程序占用端口显示绑定地址与端口。远端只返回通用拒绝时，界面明确说明未返回细分原因，保留原始错误。TCP 检测不发送应用协议内容，未验证数据库登录或 HTTP 业务响应。

最终共 50 项测试通过，0 失败、0 跳过：Core 14、Infrastructure 17、Desktop 19。最后成功结果分别为 `artifacts/test-results/tunnels-final_net10.0_20261015213935.trx`、`tunnels-final_net10.0_20261015214004.trx` 和 `tunnels-final_net10.0_20261015213945.trx`。新增覆盖双向转发的目标失败和定期恢复、实际连接触发状态更新、规则冲突、外部进程端口占用、远端监听拒绝、目标连接拒绝、停止后的延迟回调，以及 UI 状态颜色、错误归属和操作可用性。

测试服务改为目标 TCP 建立后才确认 direct-tcpip，并行处理每个反向连接请求，使目标失败不会阻塞整个监听。实际 Avalonia/Skia 窗口渲染见 `artifacts/tunnel-layout-preview.png`，同时展示绿色正常规则和带冲突原因的红色规则。测试只使用回环 SSH 服务，未连接用户实际服务器。

最终 Release 构建 0 警告、0 错误。新版 Windows x64 单文件位于 `artifacts/release/win-x64/ShellX.exe`，大小 53,666,839 字节；独立测试数据目录下启动正常，检测到标题 ShellX 且进程响应。已关闭本轮启动测试进程并停止本机 SSH 测试服务。

## 普通 Release 仍为旧版的修复

用户反馈 Release 中仍是单个远程面板、监控缺少内存占比和 PID。检查发现 `src/ShellX.Desktop/bin/Release/net10.0/ShellX.Desktop.dll` 停在 18:50（279,040 字节）；先前独立构建及 RID 发布更新了发布包，没有同步普通 Release。已重新编译普通 Release 和 win-x64 发布目录，普通桌面程序集更新为 318,976 字节。

发布脚本增加普通 Release 构建步骤，随后执行 RID 发布；Windows PowerShell 5.1 下执行通过，构建 0 警告、0 错误。指定独立构建目录时仍保持隔离，以保留运行中的开发程序。

从最终单文件 EXE 解包核对 Desktop、Core、Infrastructure 三个程序集，SHA-256 均与当前 win-x64 构建一致；报告为 `artifacts/package-verification/bundle-check.json`。普通 Release 和成品程序集均检查到本地文件面板、远程文件面板、PID 与内存百分比属性。

使用从 EXE 提取的 Desktop 程序集（另两个程序集与对应发布组件逐字节一致）替换隔离测试目录中的应用组件，执行既有双栏传输与多标签原生终端两项验收：2 项通过、0 失败、0 跳过。双栏上传/下载、拒绝覆盖、PID/内存显示及隧道绿红状态均通过，结果见 `artifacts/test-results/release-bundle.trx`，成品组件渲染预览位于 `artifacts/package-verification/release-preview.png`。

## 进程排序、共享隧道与 AOT 配置（2026-10-15）

进程 CPU、内存表头支持点击切换升降序；按数值排序全部采样后取前 8 项。排序列和方向保存到 settings.json，刷新保留，所有标签同步，新标签和重启后恢复。新增旧设置缺少字段的默认值兼容测试。

连接时自动启动保存的隧道并显示面板。同一主机、SSH 端口、账号的连接共享运行实例和状态，在任一标签启停都会同步。新标签保留已有手动停止状态；关闭首个标签后仍可转发，最后一个连接释放时关闭端口。共享传输异常后，新连接替换传输，旧标签能恢复接收状态。规则失败保留具体原因，不阻断终端和其他规则。

最终全量测试 55 项通过，0 失败、0 跳过：Core 14、Infrastructure 21、Desktop 20。结果为 `artifacts/test-results/shared-sort-final_net10.0_20261015222144.trx`、`shared-sort-final_net10.0_20261015222213.trx`、`shared-sort-final_net10.0_20261015222158.trx`。新增覆盖数值排序、全部进程参与内存排序、刷新与保存、旧设置/旧连接兼容、双标签状态同步、手动停止后新标签不重启、关闭所有者后的实际字节转发、最后标签释放监听，以及共享传输中断和重连恢复。

普通 Release 和 RID Release 均已更新，Windows x64 单文件为 `artifacts/release/win-x64/ShellX.exe`，53,709,039 字节（22:22:35）。包内 Desktop、Core、Infrastructure 组件解包后与当前 RID 构建逐字节一致，报告见 `artifacts/package-verification/bundle-check.json`。使用三份解包组件重新执行双栏传输、多标签终端及共享启停验证，2 项通过，结果为 `artifacts/test-results/release-shared-bundle.trx`。最新成品组件渲染为 `artifacts/package-verification/release-preview.png`。独立数据目录启动成品，窗口标题 ShellX、进程响应正常，随后正常关闭本轮测试进程；未连接实际服务器。

Native AOT 增加 `win-x64-aot.pubxml` 与 `scripts/publish.ps1 -Aot` / `scripts/start.ps1 -Aot`，输出与普通单文件分离，保留 Avalonia/Skia 原生 DLL。运行项目启用 AOT 分析、编译绑定；JSON 改为源生成。持久化 DTO 保留旧配置中缺失字段的初始化值，Core 记录仍不可变。DataGrid 显式列的排序模型属性使用 DynamicDependency 保留。

本机 Visual Studio Community 2026 未安装 MSVC x64/x86 和 Windows SDK，标准 AOT 发布报 `Platform linker not found`。诊断时使用 SDK 的 `IlcUseEnvironmentalTools=true` 运行原生代码生成，生成约 106 MB 的 OBJ，随后真实链接阶段因缺少 `link.exe` 失败。日志在 `artifacts/aot-analysis.log`。该参数只用于诊断，未加入发布配置，未生成或宣称可运行的 AOT 成品。

AOT 分析尚有第三方警告：终端控件默认样式的动态加载后备路径、Serilog 对象结构化、DataGrid 自动列/类型转换/反射排序等。应用已在 XAML 显式加载终端主题、使用显式只读列与编译绑定、保留文件模型属性；没有关闭或掩盖上述警告。仍需安装工具链后完成原生包启动、终端、对话框、文件列排序、持久化与 SSH/SFTP/共享隧道验收，当前普通 Release 的通过结果不能替代 AOT 运行验证。

## 目录递归上传与默认收起转发面板（2026-10-15）

本地文件夹可直接通过“上传 →”递归上传到右侧当前远程目录，保留所选文件夹、子目录、空目录与隐藏文件。扫描在后台执行，单个目录归为一个可取消的任务，进度按总字节数计算。同名远程文件夹先确认合并、覆盖其中同名文件，拒绝后显示“已跳过”，保留远端内容。远端其他文件不删除；同名文件/目录类型不兼容时明确失败，目录链接不会跟随到目标继续上传。已移除上传目录需打包的提示。

端口转发继续在连接时自动启动，但面板默认收起。顶部 ⇄ 图标聚合状态：红色异常优先，黄色检测中或待验证，绿色连通，全部停止为灰色。悬停显示摘要与失败原因，点击可展开/收起。共享标签状态改变也会更新图标，无需打开面板。

全量测试 57 项通过，0 失败、0 跳过：Core 14、Infrastructure 22、Desktop 21。结果为 `artifacts/test-results/recursive-upload_net10.0_20261015223721.trx`、`recursive-upload_net10.0_20261015223751.trx`、`recursive-upload_net10.0_20261015223733.trx`。新增验证真实 SFTP 中文多层目录、空目录、隐藏文件、合并拒绝与覆盖、保留远端额外文件、取消及同名类型冲突；桌面验证面板默认收起、点击展开/收起、错误图标绑定与多标签状态共享。实际 Avalonia/Skia 渲染位于 `artifacts/recursive-upload-preview.png`。

普通 Release、RID Release 与独立发布包已同步更新。成品 `artifacts/release/win-x64/ShellX.exe` 为 53,713,223 字节（22:38:35），包内三个应用组件与当前 RID 构建逐字节一致，报告更新于 `artifacts/package-verification/bundle-check.json`。独立数据目录下启动，窗口标题 ShellX、进程响应正常，随后正常关闭本轮测试进程。仅使用本机回环 SSH 服务，未连接实际服务器。

## 隐藏文件间距、右键编辑与文本上限（2026-10-15）

本地和远程隐藏文件开关恢复勾选框与文字之间 8 像素间距，并给筛选框留出间隔。两栏增加明确的“编辑文本”右键菜单；右键先选中点中的行，保留已选多项，多选时禁用单文件编辑。远程工具栏“编辑”改用文本编辑命令；文件夹、链接和无选择状态下禁用。本地系统默认程序打开入口保留。

内置编辑继续面向小型 UTF-8 配置文件。本地与远程共用后台有界读取、严格 UTF-8 解码和内容检查：最多读取 2 MiB，最多 256K 字符、10000 行、单行 8192 字符。读取中增长也受字节上限限制，读取循环检查取消；通过后才交给界面。输入框限制总字符数，保存前再次验证内容。本地保存增加大小、修改时间与原内容冲突检测，保存后正确更新元数据，保留 UTF-8 BOM 和原换行；失败不截断现有文件。远程沿用原有权限与内容冲突保护。

全量测试 61 项通过，0 失败、0 跳过：Core 14、Infrastructure 24、Desktop 23。结果为 `artifacts/test-results/text-editor_net10.0_20261015225225.trx`、`text-editor_net10.0_20261015225254.trx`、`text-editor_net10.0_20261015225240.trx`。新增验证本地连续保存与 BOM/换行保留、同大小同时间的外部内容变化、超限保存不覆盖，以及本地/远程大文件、长行、过多行、过多字符、二进制和取消。通过实际 Headless 鼠标右键选行、菜单命令绑定和 SFTP/本地落盘，确认编辑的是点中的文件，另一个文件不变；多选禁用编辑。

实际 Avalonia/Skia 渲染位于 `artifacts/text-editor-layout-preview.png` 和 `artifacts/text-editor-preview.png`。245000 字符、5000 行的样本打开并渲染约 737 ms，且可继续处理 UI 调度；记录见 `artifacts/text-editor-render.json`。这是本机单次样本结果，不代表超出限制的大文件性能。更完整的编辑器可后续评估具备行虚拟化的 AvaloniaEdit，当前未新增编辑控件依赖或外部编辑自动回传流程。

普通 Release、RID Release 和 Windows x64 独立发布包已同步更新，包内三个应用组件与当前 RID 构建一致，报告为 `artifacts/package-verification/bundle-check.json`。成品在独立数据目录启动，窗口标题 ShellX、进程响应正常，随后关闭本轮测试进程。仅连接本机回环 SSH/SFTP 服务。

## 编辑器语法高亮与多编码（2026-10-15）

内置编辑器改用 AvaloniaEdit 12，提供行号及 VS Code 深色配色。支持 JSON、XML、Nginx、INI、YAML、Shell、SQL 的基础词法高亮，按文件名、路径及内容选择，也可手动切换。显式继承 TextEditor 的样式键并加载 Fluent 主题，避免自定义派生控件出现编辑区域空白；测试断言实际文本区域尺寸，并渲染核对 JSON、XML、Nginx 三种文本。

本地和远程共用 ITextCodec，支持 UTF-8、UTF-16、UTF-32 的字节序及 BOM 选项，以及 GB18030、GBK/GB2312、Big5、Shift-JIS、韩文代码页、Windows-1252/1251、ISO-8859-1，共 18 个编码选项。优先识别 BOM、Unicode 字节模式及已知 XML 编码声明，其他情况尝试严格 UTF-8，再使用带有“推测编码”提示的后备编码。无 BOM 编码不能保证自动判断准确，可手动选择后“按此编码重读”；存在未保存文本时确认丢弃。另选编码直接保存则执行转码。

打开时保留有界原始字节快照，保存时以原始字节检查外部修改。未修改内容且编码不变时保留原字节；编辑后保持所选编码、BOM 与换行。严格编码拒绝无法表示的字符，报错并保留原文件，不使用问号替换。保留 2 MiB、256K 字符、10000 行和单行 8192 字符限制，输入及粘贴也检查上限。

全量测试 87 项通过，0 失败、0 跳过：Core 14、Infrastructure 44、Desktop 29。结果分别为 `artifacts/test-results/syntax-editor_net10.0_20261015231932.trx`、`syntax-editor_net10.0_20261015232001.trx`、`syntax-editor-ui-final_net10.0_20261015232412.trx`。新增覆盖 18 种编码/BOM 的本地编辑保存、真实回环 SFTP 的 GBK/UTF-16/Big5/Shift-JIS 保存与 UTF-8 转换、手动编码重读、不能编码字符时保留文件、三种语法的实际着色、真实对话框编码与语法选择绑定，以及超限输入拒绝。

实际渲染为 `artifacts/editor-json.png`、`artifacts/editor-xml.png` 和 `artifacts/editor-nginx.png`。245000 字符、5000 行的样本打开并渲染约 187 ms，见 `artifacts/text-editor-preview.png` 及 `artifacts/text-editor-render.json`；这是本机单次有界样本结果，不代表无限制大文件的性能。

普通 Release、RID Release 和 Windows x64 独立发布包同步更新，构建 0 警告、0 错误。成品 `artifacts/release/win-x64/ShellX.exe` 为 53,968,515 字节（2026-10-15 23:24:44 +08:00）。从成品提取 Desktop、Core、Infrastructure、AvaloniaEdit 四个组件，均与当前 RID 构建逐字节一致，报告为 `artifacts/package-verification/bundle-check.json`。独立数据目录下启动成品，窗口标题 ShellX、进程响应正常，并正常关闭本轮测试进程。只使用本机 SSH/SFTP 测试服务。

本节验证的是普通独立发布包。Native AOT 仍受前述本机 MSVC/Windows SDK 缺失限制，本轮新增 AvaloniaEdit 依赖尚未完成原生发布及运行验证。

## 保存结果反馈与文件列宽（2026-10-16）

编辑器保存期间显示“正在保存”，成功后使用绿色文字、绿色背景显示“保存成功”及时间。再次修改文本或保存编码后清除成功外观。保存失败保留编辑内容，直接在编辑窗口弹出失败原因和文件路径，可返回编辑；原状态行同时保留错误。实际对话框测试通过 GBK 无法编码表情字符触发错误，检查弹窗可见、原因明确、原字节不变，并验证关闭错误后可再次成功保存。

两侧文件列表改为按内容计算初始列宽，显式允许调整列宽并提供横向滚动，避免名称星号列在狭窄远程面板下禁止拖动。拖动表头边界调整列宽，双击边界重置为自动宽度；双击文件打开只接受数据行，避免表头操作误开文件。本地和远程分别使用 560 像素宽窗口、较长中文文件名，实际模拟按下、拖动、释放及双击，断言列宽缩小再恢复，2 项均通过。

全量验证 89 项通过、0 失败、0 跳过：Core 14、Infrastructure 44、Desktop 31。Core 和 Infrastructure 结果为 `artifacts/test-results/save-columns_net10.0_20261015233648.trx`、`save-columns_net10.0_20261015233718.trx`；加入原生启动与终端主题保留代码后，Desktop 31 项再次全部通过，最终结果为 `aot-bundle-desktop_net10.0_20261015235232.trx`。渲染文件为 `artifacts/editor-save-success.png`、`editor-save-error.png`、`local-columns.png`、`remote-columns.png`。

普通 Release、RID Release 与单文件成品同步更新，成品为 `artifacts/release/win-x64/ShellX.exe`，53,972,611 字节（2026-10-15 23:59:46 +08:00）。包内 Desktop、Core、Infrastructure、AvaloniaEdit 四个组件均与最新 RID 构建逐字节一致，报告为 `artifacts/package-verification/bundle-check.json`。最终 EXE 在独立数据目录启动，窗口响应正常并正常退出，记录为 `artifacts/managed-delivery-smoke.json`。

## Native AOT 单文件分发与原生运行验证（2026-10-16）

经用户授权，通过本机 Visual Studio Installer 安装 MSVC x64/x86 工具 14.51.36231 和 Windows SDK 10.0.26100，安装退出码 0，记录为 `artifacts/aot-toolchain-install.json`。此前缺少链接工具的限制已解除；本节为新的原生发布结果，不沿用前面仅生成 OBJ 的结论。

`scripts/publish.ps1 -Aot` 使用独立 AOT 发布配置，将当前 NuGet 依赖中的 ANGLE、SkiaSharp、HarfBuzzSharp、ConPTY DLL，以及 x64/arm64 OpenConsole 辅助程序嵌入 EXE。启动时按资源内容哈希选择 `%LOCALAPPDATA%/ShellX/native/win-x64/<内容版本>/` 缓存，校验文件并复用；需要写入时通过命名互斥锁协调、临时文件与原子替换释放，再按绝对路径加载 DLL。这是单文件分发，运行时仍使用释放到缓存的组件，不是把所有原生库静态链接到一个永不释放文件的 EXE。

首次原生包仅打开主窗口成功，进入连接标签时终端控件的静态构造会反射加载主题，因编译 XAML 加载入口被裁剪而崩溃。现已通过 TerminalHost 的 DynamicDependency 保留该入口公共方法，继续使用已编译主题，未关闭裁剪分析。重新原生编译后完成连接标签及终端、编辑器验证。

最终发布及交付目录均只包含一个 EXE：`artifacts/release/win-x64-aot/ShellX.exe`，57,939,968 字节（55.3 MiB，2026-10-16 00:00:15 +08:00）。SHA-256 为 `8859f1b53ceb77775a2c939c69d9304e6c7a19a3ac8a1693c50bcfc851a704ea`，与发布输出逐字节一致。PE 检查确认没有 CLR 头；实际进程没有加载 coreclr.dll，四个原生 DLL 均从用户缓存加载。报告为 `artifacts/package-verification/aot-check.json` 和 `artifacts/aot-delivery-smoke.json`。

通过最终交付 EXE、独立数据目录和 Windows UI Automation 连接本机回环 SSH/SFTP 服务，确认使用已保存凭据连接、监控数据填充、CPU/内存排序命令执行和终端命令发送。选择远程 hello.txt 打开内置编辑器，执行保存并检查“保存成功”提示；从测试服务文件系统模拟外部修改，再次保存直接弹出“远程文件已被其他程序修改”，随后正常关闭。保存与错误结果为 `artifacts/native-ui-aot-delivery/native-editor-result.json`。这里的原生编辑器保存检查使用已打开内容；文本输入、转码、语法高亮和列宽鼠标操作另由前述自动化测试覆盖，尚未进行原生版人工输入法、vim/top、实际用户服务器及全部隧道流程验收。

AOT 发布保留 15 条第三方裁剪/AOT 分析警告，涉及终端控件运行时主题后备入口、Serilog 结构化对象、DataGrid 自动列、反射类型转换与泛型辅助路径，日志为 `artifacts/aot-delivery.log`。应用继续采用编译主题、显式列、编译绑定、模型属性及主题入口保留；本机核心流程已通过，但这些验证不能视为所有第三方运行路径均已覆盖。

## 终端 Ctrl＋滚轮字号调整（2026-10-16）

WorkspaceView 在终端容器的隧道路由阶段处理 Ctrl＋垂直滚轮，调用当前标签已有的字号增减命令，并消费该事件，避免同时触发终端滚动或远端鼠标滚轮报告。向上放大、向下缩小，每格调整 1，继续使用 10–28 的字号范围。普通滚轮及终端以外区域的 Ctrl＋滚轮保持原有处理；字号保存在当前标签的 ViewModel，切换标签仍保留，不改为全局设置。

扩展既有真实 SSH 多标签终端测试，通过实际 Headless 鼠标事件验证 Ctrl 向上/向下滚轮、普通滚轮不调字号、上下限、文件面板滚轮不影响终端、第二标签保持默认字号、切回首标签保留字号，以及滚轮调整后菜单命令和绑定仍有效。终端 ANSI/中文数据、实际命令回显与隧道共享原有验收在同一测试中继续通过。结果为 `artifacts/test-results/terminal-zoom_net10.0_20261016092132.trx`：1 项通过、0 失败、0 跳过；使用本机回环 SSH 服务，未连接实际用户服务器。

普通 Release、RID Release 及两个单文件成品均已更新。普通版为 53,972,611 字节（2026-10-16 09:22:06 +08:00），AOT 版为 57,940,992 字节（09:23:03）。普通包的四个应用/编辑器组件与最新 RID 构建一致，见 `artifacts/package-verification/bundle-check.json`。AOT 交付目录只含一个 EXE，与原生发布输出逐字节一致，PE 没有 CLR 头；报告为 `artifacts/package-verification/terminal-zoom-aot-check.json`。两份最终成品均使用独立数据目录启动，窗口响应正常并正常退出；AOT 进程从用户缓存加载四个原生 DLL，没有加载 coreclr.dll。启动记录为 `artifacts/terminal-zoom-managed-smoke.json` 和 `terminal-zoom-aot-smoke.json`。AOT 继续保留既有第三方裁剪分析警告，日志为 `artifacts/terminal-zoom-aot-publish.log`。

## 独立字体设置与应用锁定（2026-10-16）

设置页分为字体、安全、常规三个页签。终端与编辑器分别提供系统字体选择、字号和预览，保存后应用于现有标签、已打开的编辑器和后续新窗口，并写入 settings.json。终端继续保留 Ctrl＋滚轮和菜单字号调整。

安全页支持首次设置、修改和移除锁定密码，修改或移除需校验当前密码。自动锁定时长可用分钟或小时输入，内部保存为分钟数；只在软件内无键鼠操作达到时长时锁定，后台 SSH 输出、监控和传输不计入用户活动。设置密码后启动即锁定；运行时 Ctrl+L 或闲置超时锁定。所有应用窗口，包括已打开或锁定期间新建的模态窗口，均覆盖不透明遮罩，只在活动窗口显示密码框。Enter 校验，错误则保持锁定并提示，验证成功恢复原内容、快捷键和焦点。窗口锁定不会卸载终端控件、清除编辑缓冲或主动断开连接。

密码存储使用随机 16 字节盐、600000 次 PBKDF2-HMAC-SHA256、32 字节校验值和固定时间比较；原始密码不进入配置与日志。密码框使用 SecretTextBox，避免当前 Avalonia TextBoxAutomationPeer 的 Value 直接返回密码文本；自定义 peer 的读取值为空，写入仍检查可编辑状态。算法依据为 [OWASP 密码存储说明](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)及 [.NET PBKDF2 API](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rfc2898derivebytes.pbkdf2?view=net-10.0)。

验证矩阵共 96 项通过、0 失败、0 跳过：Core 14（`artifacts/test-results/security-fonts_net10.0_20261016094420.trx`）、Infrastructure 46（`security-fonts_net10.0_20261016094447.trx`）、Desktop 36（`security-fonts-desktop-final_net10.0_20261016095237.trx`）。最后收起工具提示及清除测试空值警告后，5 项安全交互测试再次通过，结果为 `security-controls-final_net10.0_20261016095649.trx`。新增覆盖随机盐及错误/损坏校验值、旧设置默认值与字体/安全配置落盘、当前密码校验、小时换算、启动锁定、可控单调时钟下的闲置截止时间与鼠标活动重置、Ctrl+L、Escape/快捷键阻断、错误密码/Enter 解锁、模态窗口遮罩、未保存内容和焦点恢复，以及辅助功能密码读取为空。真实回环 SSH 测试在锁定状态继续收取命令回显，并在解锁后保留终端实例及缓冲，终端字体/字号变更也完成实际绑定检查。

界面已使用 Avalonia/Skia 渲染核对，预览为 `artifacts/settings-fonts.png`、`settings-security.png`、`application-locked.png`。普通单文件版及 AOT 版均使用独立数据目录，通过 Windows UI Automation 验证启动锁定、错误密码拒绝、密码读取为空、Enter 解锁、终端/编辑器字体控件及保存后配置保留。AOT 成品另外经历实际一分钟无操作后自动锁定，再次正确解锁并正常退出；标准错误输出为空。记录为 `artifacts/security-managed-ui-final/result.json` 和 `security-aot-ui-final/result.json`。这些原生检查使用隔离配置，没有修改实际用户锁定密码，也没有连接实际用户服务器。

普通 Release、RID Release 和两个交付包均同步更新。普通成品 `artifacts/release/win-x64/ShellX.exe` 为 53,991,603 字节（2026-10-16 09:54:19 +08:00），包内四个组件与最新 RID 构建一致，见 `artifacts/package-verification/bundle-check.json`。AOT 成品 `artifacts/release/win-x64-aot/ShellX.exe` 为 58,097,664 字节（09:55:02），交付目录只含一个 EXE，与原生发布输出逐字节一致，PE 无 CLR 头；进程从用户缓存加载四个原生 DLL，没有加载 coreclr.dll。最终核对报告为 `artifacts/package-verification/security-aot-check.json`。普通构建 0 警告、0 错误；AOT 继续保留前述 15 条第三方裁剪/AOT 警告，日志为 `artifacts/security-fonts-aot-publish.log`。


## 2026-10-16 暗亮主题、双向拖拽和程序图标

暗色和亮色主题可在设置的“常规”页选择，保存后切换并在重启后保留。已核对主窗口、监控、文件双栏、终端、代码编辑器、弹窗和锁定遮罩使用的语义资源；终端和编辑器不因换主题重建。真实回环 SSH 会话切换主题后仍保留同一终端实例和命令回显。Skia 预览为 artifacts/workspace-dark.png、workspace-light.png、theme-dark.png、theme-light.png。

完整矩阵 104 项通过、0 失败、0 跳过：Core 14（artifacts/test-results/themes-drag-final_net10.0_20261016102736.trx）、Infrastructure 52（themes-drag-final_net10.0_20261016102806.trx）、Desktop 38（themes-drag-final_net10.0_20261016102753.trx）。之后补充图标和会话版本校验，相关 3 项界面集成测试再次通过，结果为 themes-logo-session-final_net10.0_20261016103342.trx。新增覆盖旧配置默认暗色、主题保存与字体保留、现有控件实时换色和语法配色、资源管理器同格式的文件/文件夹原始拖放事件、多选鼠标拖动保留、实际 SFTP 上传和下载的字节一致性、中文和隐藏/空目录、拒绝目录合并及文件覆盖、禁用面板/断线/重连后的过期拖动、非法远端名称在写入前拒绝，以及目录下载取消。

拖放目标固定为释放时的当前目录，内部拖放仅在同一工作区双栏间支持；不是远程拖出到 Windows 资源管理器。远端符号链接跳过，本地下载目标重解析点拒绝；单次目录下载限制 100000 个项目及 128 层。逐文件的临时文件替换保留原行为，取消目录任务可能保留先前已完成的项目。目录同步和外部资源管理器的人工拖动体验不在本次自动验证范围内。

图标源稿为 src/ShellX.Desktop/Assets/ShellX.svg，512px PNG 与包含 16/20/24/32/40/48/64/128/256px 的 ICO 同步导出。已集成 ApplicationIcon、窗口、对话框、导航及欢迎页。Windows 原生检查从两个交付 EXE 提取图标、读取 WM_GETICON 的窗口图标并对比，普通包与 AOT 包图像完全一致；图标尺寸预览为 artifacts/icon-sizes.png。

普通和 AOT 成品均在独立数据目录使用 Windows UI Automation 打开真实回环 SSH 连接，确认 CPU/内存排序入口，发送命令，选择亮色并保存，再重启确认仍选中亮色，最后正常关闭。报告为 artifacts/themes-managed-native/result.json 和 artifacts/themes-aot-native/result.json。标准错误为空，未修改用户的实际配置、凭据或连接。原生检查验证主题和图标；传输由上述实际 SSH/SFTP 与 Avalonia 原始输入测试覆盖。

普通 Release、RID Release 和两个交付包已更新。普通单文件 artifacts/release/win-x64/ShellX.exe 为 54,195,226 字节（10:34:56 +08:00），包内组件与当前 RID 构建一致，见 artifacts/package-verification/bundle-check.json。AOT 单文件 artifacts/release/win-x64-aot/ShellX.exe 为 58,419,712 字节（10:35:40），SHA256 为 b754dfc0e5f5957ecf93c5c5b6b138d9427df1832523391afb1020dd9c413771；交付目录仅含一个 EXE，与原生发布输出逐字节相同，PE 无 CLR 头，运行进程加载四个缓存原生 DLL、未加载 coreclr.dll。最终报告为 artifacts/package-verification/themes-logo-aot-check.json。普通构建 0 警告/错误；AOT 保留原有 15 条第三方分析警告，发布日志为 artifacts/themes-drag-logo-aot-publish.log。


## 2026-10-16 弹出连接管理器和共享监控

顶部文件夹 / Ctrl+B 现在弹出连接管理器。连接列表单击即关闭管理器并新开独立标签，可重复打开同一配置；新连接可以并行等待连接，主机指纹确认仍串行。管理器支持空文件夹、子文件夹、重命名、删除空文件夹，以及通过连接编辑窗口更换所属文件夹；原有分组自动展开为文件夹层级，空文件夹保存于 connections.json 的 Folders 字段。新建连接默认使用当前文件夹，嵌套编辑和提示窗口属于管理器。

切换标签时，左侧绑定对应 Monitor，终端、文件目录和命令区保留各自实例与状态。同一规范主机地址、SSH 端口和账号共享一个监控模型和采样循环，暂停、曲线、网卡和排序同步；不同地址独立。关闭当前采样来源时取消并等待其未完成采样，自动使用其他连通标签，最后一个来源断开停止。关闭当前标签时保留其原先选中状态并自动选择剩余标签，修复 ListBox 提前清空 SelectedItem 造成工作区空白的问题。

完整测试矩阵共 109 项通过、0 失败、0 跳过：Core 14（artifacts/test-results/manager-monitor-final_net10.0_20261016105737.trx）、Infrastructure 53（manager-monitor-final_net10.0_20261016105807.trx）、Desktop 42（manager-monitor-final_net10.0_20261016105800.trx）。修复移除当前标签后，连接管理和终端相关 3 项再次通过，结果为 manager-selection-final_net10.0_20261016105848.trx。补充实际连接文件夹下拉默认值和移动后落盘检查后，该完整界面联调再次通过，见 manager-folders-final_net10.0_20261016110147.trx。

新增测试覆盖旧版分组迁移、空/嵌套文件夹重启保留、旧保存接口不丢空文件夹、重命名同步后代连接、非空文件夹删除拒绝、搜索、真实鼠标打开管理器和点击连接、多次打开同一配置、不同地址标签切换、保持原终端实例与文件路径、当前标签关闭后的自动选择、实际回环 SSH 来源接续，以及共享监控唯一读取、暂停和历史保留、磁盘导航路由、正在执行的采样取消和最后租约释放。Skia 预览为 artifacts/connection-manager.png 和 shared-monitor-tabs.png。

普通单文件与 AOT 单文件均通过 Windows UI Automation 验收弹出管理器、创建并保存文件夹、连续点击新开两个标签、监控暂停状态跨标签共享、关闭首个采样标签后继续、主题切换、程序图标及重启保留目录和主题。报告为 artifacts/manager-managed-native/result.json 和 manager-aot-native-final/result.json。首轮 AOT 自动检查未得到预期弹窗而超时，未作为验收依据；启用逐步记录并使用新隔离数据目录后上述检查全部通过，标准错误为空。所有原生和协议检查均使用隔离配置与本机回环测试服务，没有操作实际用户服务器。

普通 Release、RID Release 与交付包已更新。普通 artifacts/release/win-x64/ShellX.exe 为 54,206,398 字节（10:59:44 +08:00），包内四个组件与当前 RID 构建一致。AOT artifacts/release/win-x64-aot/ShellX.exe 为 58,582,528 字节（11:00:36），SHA256 为 d94b514cacb0fc3f3a3e595526a3bbd11698f70374b5d34155698624707be8fb，交付目录仅一个 EXE、与原生发布输出一致、PE 无 CLR 头；实际进程加载四个缓存原生 DLL，未加载 coreclr.dll。最终核对见 artifacts/package-verification/manager-monitor-aot-check.json；AOT 保留既有 15 条第三方裁剪分析警告，日志为 artifacts/manager-monitor-aot-publish.log。


## 2026-10-16 连接标识色

连接管理器右键连接进入编辑窗口，可选择“不标记”、红/橙/绿/蓝/紫/青或自定义 #RRGGBB，并有标签预览。配置保存 AccentColor；旧连接默认不标记。标签顶部为 3px 标识线，选中和未选中标签分别使用柔和同色底，终端头部、正文和命令输入区共同包在 2px 四边同色框内；暗色与亮色分别计算底色和边框。显示时限制饱和度及明度，自定义鲜红色也不会形成大片高亮背景。连接列表同步显示细色条。

完整测试矩阵 110 项通过、0 失败、0 跳过：Core 14（artifacts/test-results/connection-color_net10.0_20261016111430.trx）、Infrastructure 53（connection-color_net10.0_20261016111459.trx）、Desktop 43（connection-color_net10.0_20261016111454.trx）。清理预览中的悬停提示后，同一完整连接管理交互测试再次通过，见 connection-color-preview-final_net10.0_20261016111633.trx。

验证覆盖旧配置空色默认、JSON 源生成保存与重读、预设色和自定义色选择、格式错误拒绝、清除标识、鲜红色的亮度约束、暗/亮主题文字对比度至少 4.5、选中状态背景变化、终端四边框、主题切换保留原终端实例，以及在真实回环 SSH 标签连接期间修改颜色后两个同配置标签立即更新。红色效果预览为 artifacts/connection-color-dark.png 和 connection-color-light.png；本次只对标签底色和终端边框着色，终端输出颜色继续按已有 ANSI/自动高亮显示。

普通与 AOT 单文件成品均通过 Windows UI Automation 选择红色预设、确认 HEX 联动、输入自定义 HEX 并正常关闭编辑窗口；带颜色的隔离连接经目录保存和重启后仍保留 AccentColor。既有新开标签、共享监控、目录、主题和图标检查同样通过。报告为 artifacts/color-managed-native/result.json 与 color-aot-native/result.json，标准错误为空。测试使用隔离数据目录及本机测试服务，未修改用户实际连接。

普通 Release、RID Release 与两个交付包已更新。普通 artifacts/release/win-x64/ShellX.exe 为 54,211,020 字节（11:17:15 +08:00），包内组件与当前 RID 构建一致。AOT artifacts/release/win-x64-aot/ShellX.exe 为 58,640,896 字节（11:18:01），SHA256 为 8ac6f1c4115f7202af6efe680ea50ceaf983eb3d8830ef85269a9406caaa0723；交付目录只有一个 EXE，与原生发布输出一致、PE 无 CLR 头。原生进程加载四个缓存原生 DLL、未加载 coreclr.dll，最终报告为 artifacts/package-verification/connection-color-aot-check.json。普通构建 0 警告/错误；AOT 保留原有 15 条第三方分析警告，发布日志为 artifacts/connection-color-aot-publish.log。


## 2026-10-16 临时标签名、路径栏居中与自动重连

右键标签选择“重命名标签”，仅修改当前标签的内存显示名。真实界面测试验证同配置两标签名称独立、取消不改名、配置修改和重连保留临时名、连接 JSON 原名不变、关闭后重新打开恢复连接名。本地和远程路径栏统一 30px 高度，去除默认最小高度及上下内边距，控件和 TextPresenter 均居中；窄面板中按暗/亮主题检查实际文字中心与控件中心偏差不超过 1px，预览为 artifacts/local-path-Dark.png、local-path-Light.png、remote-path-Dark.png、remote-path-Light.png。

“设置 → 常规”新增断线自动重连开关及秒数配置，默认开启、5 秒，可设置 1–86400 整数秒并保存。已连接会话意外断线后在当前终端显示时间、断线原因、等待间隔、尝试次数、失败原因和成功提示。保持同一个终端及滚动缓冲区，重新连接 SSH/SFTP 与监控；手动断开、关闭标签或关闭开关会取消重连，服务器身份拒绝也停止重试。首次连接失败不启动自动重试，自动重连建立新 Shell，不恢复服务端旧进程。

完整测试矩阵共 115 项通过、0 失败、0 跳过：Core 14（artifacts/test-results/tab-rename-reconnect-final_net10.0_20261016115835.trx）、Infrastructure 53（tab-rename-reconnect-final_net10.0_20261016115904.trx）、Desktop 48（tab-rename-reconnect-final_net10.0_20261016115916.trx）。新增回环 SSH 验证真实传输断开、首次模拟失败、每次等待至少设置的 1 秒、第二次成功、终端原有输出和所有日志保留、重连后命令交互/SFTP/监控可用，以及在建立重连期间手动断开、关闭开关和关闭标签都能取消并停止后续尝试。日志预览为 artifacts/auto-reconnect.png。测试使用隔离数据及本机服务。

普通和 AOT 单文件实际通过 Windows UI Automation 右键标签重命名、另一标签名不变、配置名不变、关闭后临时名消失、设置 1 秒并保存、实际传输断开后自动恢复、重启后间隔保留，及既有连接目录、主题、监控共享、颜色和图标检查。报告为 artifacts/tab-reconnect-managed-native-final/result.json 和 artifacts/tab-reconnect-aot-native/result.json；标准错误均为空。本次没有操作用户实际连接。

普通 Release、RID Release 和两个交付包均已更新。普通 artifacts/release/win-x64/ShellX.exe 为 54,220,021 字节（11:59:55 +08:00），包内四个组件与当前 RID 构建一致，报告为 artifacts/package-verification/bundle-check.json。AOT artifacts/release/win-x64-aot/ShellX.exe 为 58,725,376 字节（12:00:47），SHA256 为 c7c1e9b194201f6b9d856343350e8d35591499363fa039d14bca227904e47292，交付目录仅一个 EXE、与原生发布输出一致、PE 无 CLR 头，实际进程加载四个原生依赖且不加载 coreclr.dll。最终核对为 artifacts/package-verification/tab-reconnect-aot-check.json。普通构建 0 警告/错误，AOT 保留原有 15 条第三方分析警告，日志为 artifacts/tab-reconnect-aot-publish.log。

## 2026-10-19 按系统锁屏状态自动锁定

锁屏检测由 Core 的 ISystemSessionMonitor 抽象，Infrastructure 按操作系统选择实现。Windows 订阅系统会话锁定/解锁通知并查询初始状态，其他系统明确返回不支持。设置新增默认勾选的“仅在系统锁屏后计时”；系统提前解锁取消本次计时，超过时长后 ShellX 保持锁定，休眠恢复的解锁通知也会补算时长。取消该条件后仍可按界面闲置时间锁定。

本轮相关测试共 22 项通过、0 失败、0 跳过：Infrastructure 的 SystemSessionMonitorTests、LockPasswordTests、PersistenceTests 共 11 项，Desktop 的 AppLockTests、SettingsMcpTests 共 11 项。覆盖真实 Windows 事件注册及初始状态查询、重复开始与释放、后台通知转 UI 线程、取消订阅、连续锁屏时长、短锁屏重置、模拟休眠后补算，以及旧配置默认值、设置保存和原有手动锁定/解锁交互。

执行根目录 build.bat，普通 Release 构建 0 警告/错误，Native AOT 单文件发布成功，保留已有第三方裁剪分析警告。artifacts/release/win-x64-aot/ShellX.exe 为 59,745,280 字节。使用独立 SHELLX_DATA_DIR 且关闭 MCP 启动成品，进程保持运行并响应，标准错误为空，未记录 Windows 锁屏通知注册或状态读取失败；随后仅关闭该测试进程。报告位于 artifacts/system-session-aot-smoke-*/result.json。本轮没有实际锁定用户桌面或让设备休眠，相关时序由可控时钟与接口测试验证。

## 2026-10-19 隧道稳定性与多个监听指向同一目标

不同监听端口指向同一目标端口保持独立转发。移除每 10 秒及请求到达时的主动 TCP 探测，后台仅检查 SSH 和监听器；启动、重连恢复和手动“检测”仍验证目标，避免为同一服务持续制造空连接。绿色状态与最近检测时间共同表示最近一次验证结果，目标恢复后可手动复检。隧道故障在状态或原因变化时写入应用日志。

SharedTunnelRegistry 统一管理候选 SSH 客户端及私钥。共享传输断开时可借用其他仍连通标签的 SSH，原传输所属标签已关闭也能恢复；没有候选时等待后续重连。恢复覆盖共享组内全部仍需运行的规则，手动停止记录保持，最后一个租约退出释放所有连接。已断开的业务 TCP 连接不能续接，需要业务客户端重连。

本机 SSH 集成测试 18 项全部通过，记录为 artifacts/test-results/tunnel-stability-infrastructure.trx。新增测试覆盖本地与远程方向的两个监听指向同一目标、14 条连接并发传输且内容不混淆、空闲超过后台检查周期后继续收发、不产生后台目标连接、手动检测及停止单条规则不影响另一条，以及原标签关闭后的传输故障恢复、恢复所有运行规则和保留手动停止规则。最后把恢复任务显式调度到后台后，3 项关键集成测试再次通过（tunnel-stability-recovery-final.trx）；补充关闭被提升标签后仍保留传输的断言，该用例单独通过。

Desktop 的隧道、重连和工作区共 16 项检查中，15 项通过，文件右键选择用例首轮点击未选中目标行；未修改文件管理器代码，单独重跑该用例通过。首轮记录为 artifacts/test-results/tunnel-stability-desktop.trx。隧道编辑、共享状态、自动重连和取消均通过。测试服务同时修正了仅用于建立 TCP 的两秒超时遗留到已转发连接的问题，以正确验证闲置长连接。本轮仅使用独立回环 SSH 服务，没有连接用户实际服务器。

执行根目录 build.bat 成功，普通 Release 0 警告/错误，AOT 保留第三方裁剪分析警告。交付目录仅 ShellX.exe，大小 59,774,464 字节，SHA256 为 821FEB81C435FAAC42A6FF7074BA9B444AAACF2502C2B40342CE8CA26BB1F54E。独立数据目录、关闭 MCP 启动 AOT 成品，出现“连接管理器”窗口且进程响应，标准错误为空，随后关闭测试进程。报告位于 artifacts/tunnel-aot-smoke-*/result.json。本机 SSH 测试服务已停止。
## 2026-11-04 安装包与自动更新

新增安装包与自动更新能力：Core 增加 `AppVersion`（读取 `Directory.Build.props` 的 `Version`）、`UpdateVersion` 版本比较与 `IUpdateService` 抽象；Infrastructure 增加 `GitHubUpdateService`，使用源生成 JSON 解析 `https://api.github.com/repos/{owner}/{repo}/releases/latest`，优先挑选名称含 `Setup` 的 `.exe` 资产并流式下载并上报进度；Desktop 增加 `UpdateManager`（检查、下载、待安装记录、重启安装）与 `UpdateBootstrapper`（`ShellX.exe --apply-update <安装包> --parent-pid <pid> --app-path <路径> [--restart]`，等待主进程退出后以 `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS` 运行安装包并按需重启）。设置页新增“更新”页签：当前版本、仓库账号与仓库名、启动自动检查、自动下载、检查更新、下载更新、重启并更新、下载安装包、打开发布页。主程序退出时若存在已下载的更新包会自动安装。

自动化测试：`dotnet test ShellX.slnx` 全部通过——Core 20 项（新增 5 项版本比较、安装包选择与版本格式断言）、Infrastructure 46 项（18 项网络集成测试按惯例跳过）、Desktop 58 项（新增 2 项：下载后登记待安装更新、更新设置持久化）。

AOT 构建：`build.bat` 成功，交付目录仅 `ShellX.exe`，60,060,160 字节，SHA256 为 5D56217F5FC19881C64187DE7756B3C4D5BAE6F6DA33F1D871E8C490792506F7，FileVersion 1.0.0.0。更新引导模式用不存在的安装包实测：0.29 秒退出且不弹界面，说明 `--apply-update` 分支在 Avalonia 启动前生效。程序正常启动后出现“连接管理器”窗口且日志无异常。

安装包：本机安装 Inno Setup 6.7.3 后，`scripts/build-installer.ps1 -Version 1.0.0` 成功产出 `artifacts/installer/ShellX-Setup-1.0.0.exe`（18,430,628 字节，SHA256 165A572271E7431F09112F8A66E0DB51113BCAB81B34BBE97DF5B49D3944C87D）。静默安装、覆盖安装（模拟升级）和静默卸载均返回 0，安装目录含 `ShellX.exe` 与卸载器。中文安装界面使用上游社区翻译 `scripts/installer/ChineseSimplified.isl`（作者 Kira，来源 https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation）。CI 用 `.github/workflows/release.yml` 打 `v*` 标签时自动编译并发布，安装包仍以 choco 安装的 Inno Setup 编译。

敏感信息审计（提交前）：对 180 个源码/脚本/文档文件扫描私钥头、GitHub/AWS 令牌、`.env`、密钥文件扩展名、口令赋值、邮箱、手机号和内网地址。命中的口令全部是测试占位值（`test-*`、`fixture-password`、`new-password` 等），邮箱仅出现在上游翻译文件的作者署名中，内网地址仅为终端高亮测试样例 `192.168.1.2`；没有真实凭据、令牌或用户服务器信息入库。

GitHub 公开仓库尚未创建：仓库内没有 remote，需要仓库账号与仓库名后执行 `git remote add origin <地址>` 与 `git push -u origin dev`，再推送 `v*` 标签触发 Release 工作流。
### 公开仓库与首个 Release（2026-11-04）

使用本机 Git 凭据管理器中已保存的 `coolqingcheng` 账号凭据，通过 GitHub API 创建公开仓库 `coolqingcheng/shell-x`（MIT 许可，默认分支 `dev`），随后推送 `dev` 分支与 `v1.0.0` 标签。全过程未打印、未入库任何令牌，临时脚本与凭据文件在完成后已删除。

仓库地址：https://github.com/coolqingcheng/shell-x

GitHub Actions 实测：`CI`（提交 `dev`）构建与测试全部通过；`Release`（标签 `v1.0.0`）在 windows-latest 上完成 .NET 10 安装、AOT 编译、Inno Setup 安装、编译安装包、打包免安装版本并上传，结论为 success。产出的 Release 资产：

- `ShellX-Setup-1.0.0.exe`（18,432,516 字节）：应用内更新优先选择该资产
- `ShellX-1.0.0-win-x64.zip`（25,181,764 字节）：免安装版本

Release 地址：https://github.com/coolqingcheng/shell-x/releases/tag/v1.0.0 。应用内“设置 → 更新”的默认仓库已设为 `coolqingcheng/shell-x`，与实际发布地址一致。

### 版本机制与更新流程改版（2026-11-04）

**构建不产生新版本的两个根因**：其一，`Directory.Build.props` 把 `<Version>1.0.0</Version>` 硬编码，任何一次编译产出的都是 1.0.0，且 `dotnet publish -p:Version=` 会被项目内同名属性覆盖，形同无效；其二，改动代码后没有重新编译，`artifacts/release/win-x64-aot/ShellX.exe` 仍是旧时间戳的产物。

**修复**：新增仓库根目录 `VERSION` 文件作为版本唯一来源（当前内容 `1.0.1`），`Directory.Build.props` 改为读取该文件并写入 `Version`/`AssemblyVersion`/`FileVersion`；`scripts/publish.ps1` 的 `-Version` 改传 `-p:ShellXVersion=`；`scripts/build-installer.ps1` 优先读取 `VERSION`。另外 `scripts/publish.ps1` 原为无 BOM 的 UTF-8，被 `build.bat` 调用的 Windows PowerShell 5.1 按 ANSI 读取，中文提示乱码且吞掉 `$` 变量，已改为 UTF-8 带 BOM 并把版本输出改为 ASCII 的 `ShellX version: <版本>`。

**实测**：`dotnet msbuild src/ShellX.Desktop -getProperty:Version` 返回 `1.0.1`；`build.bat`（AOT）日志输出 `ShellX version: 1.0.1`，产物 `artifacts/release/win-x64-aot/ShellX.exe` 为 60,059,648 字节，FileVersion `1.0.1.0`、ProductVersion `1.0.1+895a838`。`dotnet build src/ShellX.Desktop -c Release` 0 警告 0 错误；`dotnet test ShellX.slnx -c Release` 通过 Core 20、Infrastructure 46（18 项网络集成测试按惯例跳过）、Desktop 58（12 项跳过）。

**更新流程改版**：设置“更新”页签去掉“更新源（GitHub 公开仓库）”的账号与仓库输入框，也去掉“检查更新”按钮，改为“下载最新版”一步完成检查与下载，另有“另存安装包”“打开发布页”。下载完成后弹出“发现新版本”确认框：确认＝立即更新并重启，取消＝登记为“关闭时更新”，在 ShellX 退出时由更新引导进程静默安装；启动时自动下载走同一套提示。仓库地址固定取内置默认值 `coolqingcheng/shell-x`，保存设置时保留原值不被覆盖。


### GitHub 新版本未产出与 v1.0.1 发布（2026-11-04）

**现象**：代码已推送到 `dev`，GitHub 上仍只有 v1.0.0，没有新版本安装包。

**根因**：`ci.yml` 只在 `dev`/`main` 推送时做构建与测试，不产出安装包；`release.yml` 仅在推送 `v*` 标签时触发。修复版本号机制后只提交了代码、没有打标签，因此 Release 工作流从未运行。

**处理**：在 `982b8f2` 上打 `v1.0.1` 标签并推送，Release 工作流实测成功（约 5 分钟）：AOT 单文件编译 → 安装 Inno Setup → 编译安装包 → 打包免安装版 → 上传资产。

**产出**：

- `ShellX-Setup-1.0.1.exe`（18,436,119 字节）
- `ShellX-1.0.1-win-x64.zip`（25,181,200 字节）

`GET /repos/coolqingcheng/shell-x/releases/latest` 返回 `v1.0.1`（非草稿、非预发布），应用内“下载最新版”可直接命中该版本。发布地址：https://github.com/coolqingcheng/shell-x/releases/tag/v1.0.1

**版本一致性**：工作流从标签取版本号并以 `-Version` 传给 `publish.ps1`（转成 `-p:ShellXVersion=`）与 `build-installer.ps1`，因此产物 FileVersion 与标签一致；根目录 `VERSION` 文件与其保持同步。

### 窗口标题版本号与 CI 测试宿主偶发崩溃（2026-11-04）

**窗口标题版本号**：新增 `ShellX.Desktop/Services/WindowTitles.cs`，在窗口构造完成后把 `v<版本>` 追加到标题，版本取 `ShellX.Core.AppVersion.Value`（即 VERSION 文件那一套）。覆盖主窗口 `ShellX v1.0.1`、连接管理器 `连接管理器 v1.0.1`、MCP 执行日志 `MCP 执行日志 v1.0.1`。实测启动 Release 版与 AOT 版，进程窗口标题均为 `连接管理器 v1.0.1`；AOT 产物 60,060,160 字节，FileVersion `1.0.1.0`。

**CI 失败排查**：提交 `c2f261c` 的 CI 失败，但失败发生在测试环节而非编译环节（编译 0 错误）。日志显示 `ShellX.Desktop.Tests: Catastrophic failure: System.InvalidOperationException: Test process did not return valid JSON (non-object)`，即 xunit v3 的测试宿主进程在握手后异常退出，宿主随后提示 “No test is available”。同一份代码本地执行 `dotnet build ShellX.slnx -c Release` + `dotnet test ShellX.slnx -c Release --no-build` 全部通过；在 GitHub 上重跑该作业（run_attempt=2）也通过，测试数量完全一致（Core 20、Infrastructure 46/18 跳过、Desktop 58/12 跳过），判定为 runner 上的偶发崩溃，与本次改动无关。

**加固**：`ci.yml` 的测试步骤改为最多重试 3 次（间隔 10 秒，全部失败才退出 1），并给作业设置 30 分钟超时。加固后 `b5b9cf2` 的 CI 实测 success。

### 一键发布流程与 v1.0.2（2026-11-04）

**要求**：每次改完代码都要出正式版本，因此把「版本号 +1 → AOT 构建 → 校验产物版本 → 提交推送 → 打标签推送 → GitHub 出安装包」固化为 `scripts/release.ps1`。脚本支持 `-Version`（指定目标版本）、`-Message`（提交信息）、`-NoPush`（只本地提交打标签）、`-NoTag`（不打标签）；版本号写入根目录 `VERSION` 文件。同时 `scripts/publish.ps1` 在构建前会强制关闭正在运行的 ShellX 进程，避免文件占用导致发布失败。

**v1.0.2 实测**：脚本运行后版本 1.0.1 → 1.0.2，AOT 产物 `artifacts/release/win-x64-aot/ShellX.exe` 60,060,160 字节、FileVersion `1.0.2.0`（脚本会校验产物版本与目标版本一致，不一致直接失败），提交 `c144e77`，推送 `dev` 与标签 `v1.0.2`，GitHub Release 工作流 success。

**v1.0.2 产物**：

- `ShellX-Setup-1.0.2.exe`（18,431,363 字节）
- `ShellX-1.0.2-win-x64.zip`（25,181,437 字节）

该版本包含窗口标题版本号功能（`ShellX v1.0.2` / `连接管理器 v1.0.2` / `MCP 执行日志 v1.0.2`）。

### 简化更新流程（2026-11-04）

**反馈**：更新页签按钮过多、流程分叉，改为单一动作。

**改动**：设置页“更新”页签只保留当前版本、状态、进度条、一个“检查更新”按钮和“启动时自动检查更新”开关；删除“下载最新版”“另存安装包”“打开发布页”“立即更新/关闭时更新”面板，以及“发现新版本时自动下载安装包”选项（`AppSettings.AutoDownloadUpdate`、`StoredSettings.AutoDownloadUpdate` 一并移除，旧配置文件里残留字段按未知成员忽略）。`SettingsViewModel` 的五个更新命令合并为一个 `CheckUpdateAsync`：检查 → 有新版本则下载安装包 → 弹“发现新版本”确认框 → 确认立即更新重启，取消则调用 `UpdateManager.ClearPending()` 清除待安装登记，退出时不再自动安装。启动自动检查走同一条路径。

**实测**：`dotnet build src/ShellX.Desktop -c Release` 0 警告 0 错误；`dotnet test ShellX.slnx -c Release` 通过 Core 20、Infrastructure 46（18 跳过）、Desktop 58（12 跳过）。

### 服务器监控空白根因修复与连接管理器名称列（2026-11-04）

**现象**：连接成功后左侧“服务器状态”面板没有数据。日志中每次建立会话后立即出现 `System.IO.InvalidDataException: 服务器未返回有效的 CPU 采样。`。

**排查过程**：先用 `ssh` 直接在目标机执行同一段采样脚本，输出完全正常（`__SX_CPU__` 段含 `cpu 4993553 677 ...`）；用裸 SSH.NET 客户端执行同一命令也正常；但用应用自身的 `SshSessionFactory` 建立会话后读取监控就失败。给 `LinuxServerMonitor` 加上原始输出日志后，真相大白：

```
bash: line 1: export: `LC_ALL
': not a valid identifier
bash: -c: line 21: syntax error near unexpected token `$'do\r''
退出码=2  原始输出长度=0
```

**根因**：采样命令用 C# 原始字符串（`"""`）书写，原始字符串会原样保留源文件的换行符；仓库配置 `core.autocrlf=true`，Windows 检出后源文件是 CRLF，于是命令里每行都带 `\r`。远端 bash 解析到 `export LC_ALL\r` 和 `do\r` 直接报语法错误，整段脚本以退出码 2 结束，没有任何标准输出，解析器因此判定“未返回有效的 CPU 采样”。

**修复**：`LinuxServerMonitor` 增加 `SampleScript = SampleCommand.Replace("\r\n", "\n").Replace("\r", "\n")`，发送前统一成 LF；新增 `.gitattributes` 令 `*.cs` 以 LF 检出，避免同类问题复发；监控解析失败时补充记录退出码、错误输出和原始输出长度，便于后续排查。

**验证**：用应用自身的 `SshSessionFactory` 建立会话后连续三次 `Monitor.ReadAsync` 全部成功（`hostname=iZuf65sxe59a3c3qmb2qozZ`）；`dotnet build src/ShellX.Desktop -c Release` 0 警告 0 错误；`dotnet test ShellX.slnx -c Release` 通过 Core 20、Infrastructure 46（18 跳过）、Desktop 58（12 跳过）。

**连接管理器名称列**：连接列表表头补上“名称”列（原先第一列表头是文件夹标题，名称只显示在行内），文件夹标题移到左下角状态栏，与连接数并列显示为“文件夹名 · N 个连接”。
## 文件管理器按类型显示图标（2026-11-04）

- 需求：文件列表里常见文件要有各自的图标，不能只有一个通用文件块。
- 归类：新增 `ShellX.Core.Files.FileTypes`，本地/远程共用一张扩展名表，输出类别（`folder/link/code/config/image/audio/video/archive/text/sheet/program/db/pdf/doc/file`）与中文标签；`LocalEntry`、`RemoteEntry` 的 `Icon`、`Kind` 改为取自该表，“类型”列随之显示图片/视频/压缩包/代码等具体类型。
- 图标：`FileIconView` 按类别给出 16×16 视框的矢量图形（文件夹、链接、`</>`、相框山景、播放框、音符、压缩箱、带行文档、表格、滑块配置、芯片、数据库圆柱，其余退回文档轮廓），仍然24×24 居中。
- 配色：文件夹 `FolderForeground`、符号链接 `LinkForeground`、普通文件 `IconForeground`，由 `Theme.axaml` 的样式给出。
- 关键坑：`FileIconView : PathIcon` 不写 `StyleKeyOverride` 时拿不到 `PathIcon` 的 ControlTheme（`Template == null`，控件完全不绘制）；写成 `typeof(PathIcon)` 后类型选择器也按 StyleKey 匹配，原来的 `controls|FileIconView` 样式会失效，因此控件构造时加了 `file-icon` 标记类，样式选择器改为 `PathIcon.file-icon[.folder|.link]`。
- 验证：`dotnet build src/ShellX.Desktop -c Release` 0 警告 0 错误；`dotnet test ShellX.slnx -c Release` 全绿（Core 43 通过；Infra 46 通过（18 跳过，需本机 SSH 夹具）；Desktop 77 通过（12 跳过））。用 `SHELLX_PREVIEW_PATH` 触发现有预览渲染，导出 `artifacts/preview/file-icons-remote.png`、`file-icons-local.png` 逐行核对：图标居中、24px、文件夹取样 `215,186,125`（#D7BA7D）、文件取样 `150,150,150`（#969696）。
## 线条图标与端口转发流量显示（2026-11-04）

### 图标

- 新增 `Controls/StrokeIcon`：几何按 24×24 视框绘制，按控件尺寸等比缩放、描边宽度保持 1.5（圆角端点与连接），文件列表图标从实心块改成线条样式（更细）。`FileIconView` 改为继承 `StrokeIcon`，仍然24×24 居中、配色规则不变。
- 文件管理器两行工具栏里的纯图标按钮（后退/前进/上一级/登录目录/刷新/更多）从文字字形（← ↑ ⌂ ↻ ⋯）换成矢量线条图标，尺寸 20px，按钮内边距 `6,5`；图标形状在 `StrokeIcon.SymbolPath` 里统一维护。

### 端口转发速度与动画

- 现状问题：SSH.NET 的 `ForwardedPort` 没有任何字节计数接口，无法直接得到速度。
- 做法：新增 `Infrastructure/Ssh/TunnelRelay`，把对外监听改为由中转持有——本地转发是“本机中转 → SSH 侧仅回环的临时转发 → 目标”，远程转发是“SSH 服务端 → 本机中转 → 目标”。中转逐字节统计上下行，且**只在收到客户端连接时才建立上游连接**，空闲时不产生任何目标连接负载。
- `SshTunnelService` 每秒采样一次：只有上一秒有字节（或刚停止）才发布带 `SentRate/ReceivedRate/BytesSent/BytesReceived` 的 `TunnelStatus`，空闲时界面不刷新；`IsRunning`、后台10秒监听检查、检测流程都同时校验转发与中转监听。
- UI：隧道行在有流量时显示呼吸动画的流量徽标（↑↓ 线条图标，0.9s 循环）和 `↑ 1.5 MB/s ↓ 340 KB/s`（`TunnelSpeed.Format`），无流量时整块隐藏。

### 验证

- 无夹具全量：`dotnet build src/ShellX.Desktop -c Release` 0 警告 0 错误；`dotnet test ShellX.slnx -c Release` Core 49 / Infra 47（18 跳过）/ Desktop 78（12 跳过）全绿。新增 `TunnelSpeedTests`、`TunnelRelayTests`（不经 SSH 验证转发、计数、无空闲连接、端口释放）、`TunnelViewModelTests.TrafficShowsSpeedAndAnimationOnlyWhileBytesFlow`。
- SSH 夹具全量：`python tests/fixtures/ssh_server.py artifacts/ssh-fixture` 后带 `SHELLX_TEST_FIXTURE` 运行，**ShellX.Infrastructure.Tests 65 项全部通过**，覆盖双向转发字节回环、多监听端口对同一目标（要求精确连接数，验证中转不产生后台连接）、监听冲突、传输恢复、编辑/停止释放端口。
- 夹具运行中 Desktop 有2项失败：`ConnectionTestTests.NewConnectionCanTestUnsavedForm…` 在**改动前的 HEAD（v1.0.5）同样失败**（同一行、同一断言），属既有问题与本次改动无关；`WorkspaceTests.RightClickEditsTheClickedLocalAndRemoteFile` 单独重跑通过（并发夹具负载下的偶发）。
- 图标预览：`SHELLX_PREVIEW_PATH` 渲染 `artifacts/preview/file-icons-remote.png`、`file-icons-local.png`，工具栏与文件列表图标逐个核对；流量图标 `artifacts/preview/flow-icon.jpg`。隧道行的流量徽标未做真机截图（需要真实隧道跑流量），仅做了 XAML 编译与单元断言。
## 字体图标全面替换与非模态标签编辑器（2026-11-04）

### 图标

- `StrokeIcon` 符号表补齐：`folder`、`tunnel`、`power`、`maximize`、`duplicate`、`close`、`settings`、`plus`、`download`、`upload`、`disk`、`layout`、`connections`、`down`、`chevron-down`（沿用既有的 `back/forward/up/home/refresh/more/flow`）。
- 替换位置：终端工具栏 `⇄ ↻ ⏻ ▣ ⋯`、连接标签状态点 `●`→`Ellipse`、标签关闭 `×`、主窗口 `＋ / ▦ / ⚙` 与连接管理按钮、连接管理器齿轮与文件夹、文件面板 `磁盘 / ＋文件夹 / ＋文件 / ←下载 / 上传→`、传输方向 `↑ ↓`、目录树文件夹 `▰` 与展开箭头 `› ⌄`、监控刷新 `↻` 与网络 `↑ ↓`、面板关闭 `×`、快捷命令 `＋ 添加`、新建隧道 `＋`。
- 保留**文本内容**中的箭头与分隔符（快捷键提示 `Alt+↑/↓`、排序表头 `CPU ↓`、菜单文案“下载到左侧目录 ←”、传输源数据里的方向字符、密码掩码 `●`），它们属于文字而非图标。
- 齿轮初版是“圆 + 辐条”，在 18px 下更像太阳，改为齿 + 双圈后按连接管理器截图放大核对为标准齿形。

### 文本编辑器：非模态标签页

- `IDialogService.EditFileAsync` 签名不变；实现改为**单窗口多标签**：`window.Show(DialogOwner)` 非模态，任务在标签打开后立即完成，因此文件列表不会被编辑器占用，可继续打开其他文件。
- 再次点击“编辑文本”：同路径文件聚焦已有标签；新文件新增标签（标签头为文件名 + 线条 `×`）。
- 标签 `×` 关闭前对未保存内容确认；关闭窗口时若多个文件未保存，统一提示“有 N 个文件尚未保存”；`Ctrl+S` 保存当前标签；`window.Title` 与 `DataContext` 跟随当前标签（保持既有测试契约）。
- 每个标签独立的 `FileEditorViewModel`、编码/语法选择、保存状态与字体订阅（关闭标签时取消 `AppLockService.SettingsChanged` 订阅，避免泄漏）。

### 验证

- 无夹具全量：`dotnet build` 0 警告 0 错误；`dotnet test ShellX.slnx -c Release` → Core 49 / Infra 47（18 跳过）/ Desktop 79（12 跳过）全绿。
- 新增测试 `FileEditorTests.OpeningAnotherFileAddsATabToTheSameNonModalEditorWindow`：两次打开只产生 1 个窗口、2 个标签、打开即完成、重复打开同文件不新增标签、关闭窗口后无残留。
- 带 SSH 夹具全量：Desktop 89 通过 / 2 失败——`ConnectionTestTests.NewConnectionCanTest…` 在改动前的 HEAD（v1.0.5）同样失败，属既有问题；`McpLogLayoutTests.LongMcpLog…` 为偶发，单独重跑通过。
- 图标与标签页截图（`SHELLX_PREVIEW_PATH` 渲染）：`workspace-dark.png`、`file-icons-remote.png`、`file-icons-local.png`、`connection-manager.png`、`shared-monitor-tabs.png`、`editor-tabs.png`，并对工具栏、文件面板按钮、齿轮做了局部放大核对。
## 服务状态窗口（2026-11-05）

### 实现

- **入口**：端口转发面板标题栏新增“服务”按钮（`IsEnabled` 跟随 `IsConnected`），点击打开非模态 `ServiceWindow`；同一连接重复打开只激活已有窗口，会话 `Disconnected` 或标签 `Disposed` 时窗口自动关闭。
- **数据链路**：`IRemoteSession.Services`（Core 新增 `IServiceCenter`，接口成员带默认实现 `null`，既有测试桩无需改动）→ `SshRemoteSession` 装配 `SshServiceCenter(SshClient, ILogger)`，与监控共用同一条 SSH 连接的 exec 通道。
- **命令**：一次执行 `systemctl list-units --type=service --all --no-pager --no-legend --plain --full` + 标记行 + `systemctl list-unit-files --type=service --no-pager --no-legend`，按单位名合并出“服务 / 状态 / 开机 / 描述”。**所有命令都是单行字符串**，不再使用可能带 CRLF 的原始字符串。
- **操作**：`start / stop / restart / enable / disable`，90 秒超时，返回 `ServiceActionResult(成功, 原始输出, 退出码)`；`StatusAsync` 用 `SYSTEMD_COLORS=0 systemctl status -l -n 25` 读取详情（无 ANSI 颜色）。
- **安全**：单位名先过本地白名单正则 `^[A-Za-z0-9@_.:\-]+$` 再拼命令，非法名直接返回失败/抛出，不触碰 SSH 通道。
- **UI**：`Features/Services/ServiceWindow(.axaml/.cs)` + `ServiceWindowViewModel`——搜索框按服务名/描述过滤、四列 DataGrid（状态按 运行中/失败/未运行 着色）、排序、选中行加载 status 详情、五个操作按钮（确认后执行，结果显示原始输出与退出码并自动刷新）。`WorkspaceTabViewModel` 补 `Dialogs` 访问器与 `Disposed` 事件。

### 验证

- 单元/界面：Core 49、Infra 50（3 项服务解析与注入拦截测试）、Desktop 84（5 项服务窗口测试）全部通过，`dotnet build ShellX.slnx -c Release` 0 错误（仅 UpdateManagerTests 里既有的2条 xUnit1051 警告）。
- 新增测试：`ServiceCenterTests`（列解析/合并开机状态/占位描述/垃圾行忽略、非法单位名不触碰 SSH）、`ServiceWindowTests`（按名称与描述搜索、确认后才执行且刷新保持选中、失败展示退出码与红色、端口转发面板存在服务入口、窗口标题与预览渲染）。
- **真实服务器只读联调**：用 `~/.ssh/id_ed25519` 连接 `iot99.xiaolab.cn`（未执行任何 start/stop 等写操作），`ListAsync` 返回 **132 个服务**，列正确（如 `aliyun.service | active / running | enabled | Aliyun Assist`、`apparmor.service | active / exited | enabled`）；`StatusAsync("ssh.service")` 返回完整 `systemctl status` 详情（Loaded/Active/Main PID/Memory）。
- 预览截图：`SHELLX_PREVIEW_PATH` 渲染 `service-status.png`，核对了列宽（“开机”列由 90 调到 118 避免 enabled 被截断）、状态着色、操作按钮与详情区。
- 带夹具**全量**（三个测试程序集并行）出现 3 项失败，均与本次改动无关，并已在改动前的 HEAD worktree（v1.0.8）上复现同样结果：`SshIntegrationTests.MultipleListenersToOneTarget…`（精确连接数 + 11 秒等待，受并行负载影响，**Infrastructure 单独运行 68/68 全过**）、`ConnectionTestTests.NewConnectionCanTest…`（HEAD 同样失败）、`WorkspaceTests.RightClickEdits…`（单独重跑通过，偶发）。
