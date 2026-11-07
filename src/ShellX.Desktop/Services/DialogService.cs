using System.Linq.Expressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;
using ShellX.Core.Sessions;
using ShellX.Core.Security;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using ShellX.Desktop.Features.Connections;
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Desktop.Features.FileEditor;
using ShellX.Desktop.Features.Tunnels;
using ShellX.Desktop.Features.Security;
using ShellX.Desktop.Features.Settings;
using ShellX.Desktop.Features.Shell;
using ShellX.Infrastructure.Security;
using ShellX.Desktop.Controls;

namespace ShellX.Desktop.Services;

public sealed class DialogService(ITextCodec codec, AppLockService? appLock = null, AppLockCoordinator? lockCoordinator = null,
    IConnectionTester? connectionTester = null, IHostKeyStore? knownHosts = null, UpdateManager? updates = null) : IDialogService
{
    public Window Owner { get; set; } = null!;
    private Window DialogOwner
    {
        get
        {
            var current = Owner;
            while (current.OwnedWindows.LastOrDefault(w => w.IsVisible && w.IsEnabled) is { } child) current = child;
            return current;
        }
    }
    private Window Window(string title, double width = 510, double height = 600)
    {
        var window = new Window { Title = title, Width = width, Height = height, MinWidth = 400, MinHeight = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Icon = Owner?.Icon };
        window.PropertyChanged += (_, change) =>
        { if (change.Property == ContentControl.ContentProperty && window.Content is Control) lockCoordinator?.Protect(window); };
        return window;
    }
    private static StackPanel Stack() => new() { Spacing = 12, Margin = new Thickness(24) };
    private static TextBlock Text(string text, double size = 13) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
    private static TextBox Input<T>(Expression<Func<T, string>> property, bool password = false)
    {
        TextBox input = password ? new SecretTextBox() : new TextBox();
        input.MinHeight = 32; input.HorizontalAlignment = HorizontalAlignment.Stretch;
        input.Bind(TextBox.TextProperty, CompiledBinding.Create(property, mode: BindingMode.TwoWay));
        return input;
    }
    private static Control Field(string label, Control input)
    { var panel = new StackPanel { Spacing = 6 }; panel.Children.Add(Text(label)); panel.Children.Add(input); return panel; }
    private static ComboBox Choice<T>(Expression<Func<T, int>> property, params string[] options)
    {
        var combo = new ComboBox { ItemsSource = options, HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.Bind(ComboBox.SelectedIndexProperty, CompiledBinding.Create(property, mode: BindingMode.TwoWay)); return combo;
    }
    private static Button Button(string text, Action click, bool primary = false)
    {
        var button = new Button { Content = text, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primary) button.Classes.Add("primary");
        button.Click += (_, _) => click(); return button;
    }
    private static DockPanel Layout(Window window, Control content, params Control[] buttons)
    {
        var root = new DockPanel();
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(24, 12) };
        foreach (var button in buttons) footer.Children.Add(button);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = content }); window.Content = root;
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) window.Close(); };
        return root;
    }

    public Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret)
        => EditConnectionAsync(profile, secret, [], ConnectionFolders.Default);
    public async Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret, IReadOnlyList<string> folders, string defaultFolder)
    {
        var vm = new ConnectionEditorViewModel(profile, secret, connectionTester == null ? null : TestConnectionAsync);
        vm.Group = ConnectionFolders.Normalize(profile == null ? defaultFolder : vm.Group);
        var window = Window(profile == null ? "新建 SSH 连接" : "编辑 SSH 连接", 540, 750); window.DataContext = vm;
        var body = Stack();
        body.Children.Add(Text("连接信息", 20));
        body.Children.Add(Field("名称", Input((ConnectionEditorViewModel x) => x.Name)));
        var folderChoice = new ComboBox { Name = "ConnectionFolderSelector", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = ConnectionFolders.Expand(folders.Append(vm.Group)) };
        folderChoice.Bind(ComboBox.SelectedItemProperty, CompiledBinding.Create((ConnectionEditorViewModel x) => x.Group, mode: BindingMode.TwoWay));
        body.Children.Add(Field("连接文件夹", folderChoice));
        var accentRow = new Grid { ColumnDefinitions = new("*,160"), ColumnSpacing = 12 };
        var accents = new ComboBox { Name = "ConnectionAccentSelector", ItemsSource = vm.AccentChoices, HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ConnectionAccentChoice>((item, _) =>
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                row.Children.Add(new ConnectionAccentBorder { AccentColor = item?.Value ?? "", Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(new TextBlock { Text = item?.Name }); return row;
            }) };
        accents.Bind(ComboBox.SelectedItemProperty, CompiledBinding.Create((ConnectionEditorViewModel x) => x.SelectedAccent, mode: BindingMode.TwoWay));
        accentRow.Children.Add(accents);
        var accentCode = Input((ConnectionEditorViewModel x) => x.AccentColor); accentCode.Name = "ConnectionAccentHex"; accentCode.PlaceholderText = "#RRGGBB"; accentCode.MaxLength = 7;
        Grid.SetColumn(accentCode, 1); accentRow.Children.Add(accentCode); body.Children.Add(Field("连接标识色", accentRow));
        var accentPreview = new ConnectionAccentBorder { Name = "ConnectionAccentPreview", Appearance = ConnectionAccentAppearance.Tab, IsActive = true, Height = 34 };
        accentPreview.Bind(ConnectionAccentBorder.AccentColorProperty, CompiledBinding.Create((ConnectionEditorViewModel x) => x.AccentColor));
        var previewGrid = new Grid { RowDefinitions = new("3,*") };
        var previewLine = new ConnectionAccentBorder(); previewLine.Bind(ConnectionAccentBorder.AccentColorProperty, CompiledBinding.Create((ConnectionEditorViewModel x) => x.AccentColor)); previewGrid.Children.Add(previewLine);
        var previewName = Text("连接标签预览", 12); previewName.Margin = new Thickness(10, 0); previewName.VerticalAlignment = VerticalAlignment.Center; Grid.SetRow(previewName, 1); previewGrid.Children.Add(previewName);
        accentPreview.Child = previewGrid; body.Children.Add(accentPreview);
        body.Children.Add(Field("主机地址", Input((ConnectionEditorViewModel x) => x.Host)));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,110"), ColumnSpacing = 12 };
        row.Children.Add(Field("用户名", Input((ConnectionEditorViewModel x) => x.Username)));
        var port = Field("端口", Input((ConnectionEditorViewModel x) => x.Port)); Grid.SetColumn(port, 1); row.Children.Add(port); body.Children.Add(row);
        body.Children.Add(Field("认证方式", Choice((ConnectionEditorViewModel x) => x.AuthenticationIndex, "账号密码", "私钥文件")));
        var password = Field("密码", Input((ConnectionEditorViewModel x) => x.Password, true)); password.Bind(Visual.IsVisibleProperty, CompiledBinding.Create((ConnectionEditorViewModel x) => x.IsPassword)); body.Children.Add(password);
        var keyRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        var keyInput = Input((ConnectionEditorViewModel x) => x.PrivateKeyPath); keyRow.Children.Add(keyInput);
        var browse = Button("选择…", () => { });
        browse.Click += async (_, _) =>
        {
            var userDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var sshDirectory = Path.Combine(userDirectory, ".ssh");
            using var startFolder = await window.StorageProvider.TryGetFolderFromPathAsync(Directory.Exists(sshDirectory) ? sshDirectory : userDirectory);
            var files = await window.StorageProvider.OpenFilePickerAsync(new()
            { Title = "选择 SSH 私钥", AllowMultiple = false, SuggestedStartLocation = startFolder });
            if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) vm.PrivateKeyPath = path;
        };
        Grid.SetColumn(browse, 1); keyRow.Children.Add(browse);
        var key = Field("私钥路径", keyRow); key.Bind(Visual.IsVisibleProperty, CompiledBinding.Create((ConnectionEditorViewModel x) => x.IsKey)); body.Children.Add(key);
        var passphrase = Field("私钥口令（可选）", Input((ConnectionEditorViewModel x) => x.Passphrase, true)); passphrase.Bind(Visual.IsVisibleProperty, CompiledBinding.Create((ConnectionEditorViewModel x) => x.IsKey)); body.Children.Add(passphrase);
        var remember = new CheckBox { Content = "记住凭据（由当前 Windows 用户加密保护）" };
        remember.Bind(CheckBox.IsCheckedProperty, CompiledBinding.Create((ConnectionEditorViewModel x) => x.RememberSecret, mode: BindingMode.TwoWay)); body.Children.Add(remember);
        var mcp = new CheckBox { Content = "允许 MCP 执行本机命令" };
        ToolTip.SetTip(mcp, "开启后，MCP 客户端可执行运行 ShellX 的电脑上的任意命令，并记录审计日志。");
        mcp.Bind(CheckBox.IsCheckedProperty, CompiledBinding.Create((ConnectionEditorViewModel x) => x.McpEnabled, mode: BindingMode.TwoWay)); body.Children.Add(mcp);
        body.Children.Add(Field("初始目录（. 表示登录目录）", Input((ConnectionEditorViewModel x) => x.InitialDirectory)));
        var error = Text(""); ThemeService.Bind(error, TextBlock.ForegroundProperty, "ErrorForeground"); error.Bind(TextBlock.TextProperty, CompiledBinding.Create((ConnectionEditorViewModel x) => x.Error)); body.Children.Add(error);
        body.Bind(InputElement.IsEnabledProperty, CompiledBinding.Create((ConnectionEditorViewModel x) => x.CanEdit));
        var testPanel = new StackPanel { Spacing = 6, Margin = new Thickness(24, 0) };
        var testing = new ProgressBar { IsIndeterminate = true, Height = 2, MinWidth = 0 };
        testing.Bind(Visual.IsVisibleProperty, CompiledBinding.Create((ConnectionEditorViewModel x) => x.IsTesting)); testPanel.Children.Add(testing);
        var feedback = Text(""); feedback.Name = "ConnectionTestStatus";
        feedback.Bind(TextBlock.TextProperty, CompiledBinding.Create((ConnectionEditorViewModel x) => x.TestStatus));
        void FeedbackColor() => ThemeService.Bind(feedback, TextBlock.ForegroundProperty, vm.TestSucceeded ? "SuccessForeground" : vm.IsTesting ? "SecondaryForeground" : "ErrorForeground");
        FeedbackColor(); vm.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(vm.TestSucceeded) or nameof(vm.IsTesting)) FeedbackColor(); };
        testPanel.Children.Add(feedback);
        var testButton = new Button { Name = "TestConnection", Content = "测试连接", Command = vm.TestConnectionCommand };
        var cancelTest = new Button { Name = "CancelConnectionTest", Content = "取消测试", Command = vm.CancelTestCommand };
        cancelTest.Bind(Visual.IsVisibleProperty, CompiledBinding.Create((ConnectionEditorViewModel x) => x.IsTesting));
        var save = Button("保存连接", () => { try { window.Close(vm.Build()); } catch (Exception ex) { vm.Error = ex.Message; } }, true);
        save.Bind(InputElement.IsEnabledProperty, CompiledBinding.Create((ConnectionEditorViewModel x) => x.CanEdit));
        var layout = Layout(window, body, testButton, cancelTest, Button("取消", () => window.Close()), save);
        DockPanel.SetDock(testPanel, Dock.Bottom); layout.Children.Insert(1, testPanel);
        window.Closed += (_, _) => vm.Close();
        return await window.ShowDialog<ConnectionEditResult?>(DialogOwner);
    }

    private Task<ConnectionTestResult> TestConnectionAsync(ConnectionEditResult value, CancellationToken cancellationToken)
        => connectionTester!.TestAsync(value.Profile, value.Secret, async challenge =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var known = knownHosts == null ? null : await knownHosts.GetAsync(challenge.Host, challenge.Port, cancellationToken);
            if (known == challenge.Fingerprint) return true;
            if (known != null) throw new InvalidOperationException("主机密钥发生变化，已拒绝连接，请核实服务器身份。");
            var trusted = await Dispatcher.UIThread.InvokeAsync(async () => await ConfirmTestHostAsync(challenge, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            if (trusted && knownHosts != null) await knownHosts.TrustAsync(challenge, cancellationToken);
            return trusted;
        }, cancellationToken);

    private async Task<bool> ConfirmTestHostAsync(HostKeyChallenge challenge, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = Window("测试连接 · 确认服务器身份", 520, 320); var body = Stack();
        body.Children.Add(Text($"主机：{challenge.Host}:{challenge.Port}\n算法：{challenge.Algorithm}\n指纹：{challenge.Fingerprint}\n\n请与服务器指纹核对。确认后将保存此主机身份。"));
        Layout(window, body, Button("取消", () => window.Close(false)), Button("确认", () => window.Close(true), true));
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => { if (window.IsVisible) window.Close(false); }));
        return await window.ShowDialog<bool>(DialogOwner);
    }

    public async Task<ConnectionProfile?> ManageConnectionsAsync(MainWindowViewModel model)
    {
        var window = Window("连接管理器", 900, 620); window.MinWidth = 720; window.MinHeight = 440; window.DataContext = model;
        var root = new Grid { RowDefinitions = new("*,Auto") };
        root.Children.Add(new ConnectionManagerView { DataContext = model });
        var close = Button("关闭", () => window.Close()); close.Margin = new Thickness(16, 10); close.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(close, 1); root.Children.Add(close); window.Content = root;
        window.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.N, KeyModifiers.Control), Command = model.NewConnectionCommand });
        window.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.N, KeyModifiers.Control | KeyModifiers.Shift), Command = model.NewFolderCommand });
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) window.Close(); };
        void Pick(ConnectionProfile profile) => window.Close(profile);
        model.ConnectionPicked += Pick;
        try { return await window.ShowDialog<ConnectionProfile?>(DialogOwner); }
        finally { model.ConnectionPicked -= Pick; }
    }

    public async Task<bool> ConfirmAsync(string title, string message, bool danger = false)
    {
        var window = Window(title, 520, 300); var body = Stack(); body.Children.Add(Text(title, 19)); body.Children.Add(Text(message));
        Layout(window, body, Button("取消", () => window.Close(false)), Button(danger ? "确认操作" : "确认", () => window.Close(true), true));
        return await window.ShowDialog<bool>(DialogOwner);
    }
    public async Task<string?> PromptAsync(string title, string label, string value = "", bool secret = false)
    {
        var window = Window(title, 480, 250); var body = Stack(); TextBox input = secret ? new SecretTextBox() : new TextBox(); input.Text = value;
        body.Children.Add(Field(label, input));
        void Submit() { if (!string.IsNullOrEmpty(input.Text) || secret) window.Close(input.Text ?? ""); }
        Layout(window, body, Button("取消", () => window.Close()), Button("确定", Submit, true));
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) Submit(); };
        window.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        return await window.ShowDialog<string?>(DialogOwner);
    }
    public async Task ShowErrorAsync(string message)
    {
        var window = Window("操作失败", 520, 280); var body = Stack(); body.Children.Add(Text(message));
        Layout(window, body, Button("关闭", () => window.Close(), true)); await window.ShowDialog(DialogOwner);
    }
    public async Task ShowInformationAsync(string title, string message)
    {
        var window = Window(title, 560, 440); var body = Stack(); body.Children.Add(Text(message));
        Layout(window, body, Button("关闭", () => window.Close(), true)); await window.ShowDialog(DialogOwner);
    }
    public async Task<IReadOnlyList<string>> PickUploadAsync()
        => (await Owner.StorageProvider.OpenFilePickerAsync(new() { Title = "选择上传的文件", AllowMultiple = true }))
            .Select(x => x.TryGetLocalPath()).OfType<string>().ToArray();
    public async Task<string?> PickDownloadFolderAsync()
    {
        var folders = await Owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择下载保存目录" });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
    public async Task<string?> PickDownloadAsync(string name)
        => (await Owner.StorageProvider.SaveFilePickerAsync(new() { Title = "下载文件", SuggestedFileName = name, ShowOverwritePrompt = true }))?.TryGetLocalPath();
    public async Task<IReadOnlyList<string>> PickXshellSessionsAsync()
    {
        var startFolder = FindXshellSessionsDirectory();
        using var location = startFolder == null
            ? null
            : await DialogOwner.StorageProvider.TryGetFolderFromPathAsync(startFolder);
        var files = await DialogOwner.StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "选择 Xshell 会话文件",
            AllowMultiple = true,
            SuggestedStartLocation = location,
            FileTypeFilter = [new FilePickerFileType("Xshell 会话（*.xsh）") { Patterns = ["*.xsh"] }]
        });
        return files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
    }

    private static string? FindXshellSessionsDirectory()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NetSarang Computer");
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateDirectories(root)
            .Select(version => Path.Combine(version, "Xshell", "Sessions"))
            .Where(Directory.Exists)
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
    public async Task<string?> PickConfigurationImportAsync()
        => (await DialogOwner.StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "导入 ShellX 配置",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("ShellX 配置（JSON）") { Patterns = ["*.json"] }]
        })).FirstOrDefault()?.TryGetLocalPath();
    public async Task<string?> PickConfigurationExportAsync()
        => (await DialogOwner.StorageProvider.SaveFilePickerAsync(new()
        {
            Title = "导出 ShellX 配置", SuggestedFileName = $"ShellX-config-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            DefaultExtension = "json", ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType("ShellX 配置（JSON）") { Patterns = ["*.json"] }]
        }))?.TryGetLocalPath();

    private Window? _editorWindow;
    private TabControl? _editorTabControl;
    private readonly Dictionary<TabItem, EditorTabState> _editorStates = [];

    private sealed class EditorTabState(TextDocument document, FileEditorViewModel vm, Action detach)
    {
        public TextDocument Document { get; } = document;
        public FileEditorViewModel Vm { get; } = vm;
        public Action Detach { get; } = detach;
        public string Name => System.IO.Path.GetFileName(Document.Path);
    }

    /// <summary>
    /// 文本编辑器改为非模态标签页：全局只保留一个编辑器窗口，再次点击“编辑文本”直接新增标签。
    /// 任务在窗口/标签打开后即完成，不再阻塞文件列表继续打开其他文件。
    /// </summary>
    public async Task EditFileAsync(TextDocument document, Func<TextDocument, string, Task<TextDocument>> save)
    {
        TextEditorLimits.ValidateContent(document.Content);
        if (_editorWindow is { } existing && existing.IsVisible && _editorTabControl is { } existingTabs)
        {
            var opened = _editorStates.FirstOrDefault(pair => Equals(pair.Value.Document.Path, document.Path));
            if (opened.Value != null) existingTabs.SelectedItem = opened.Key;
            else AddEditorTab(existing, existingTabs, document, save);
            await Task.CompletedTask;
            return;
        }

        var window = Window("编辑 · " + document.Path, 1050, 720);
        window.MinWidth = 780;
        var tabs = new TabControl();
        window.Content = tabs;
        _editorWindow = window;
        _editorTabControl = tabs;
        AddEditorTab(window, tabs, document, save);
        tabs.SelectionChanged += (_, _) => ActivateEditorTab(window, tabs);

        var approved = false;
        window.Closing += async (_, e) =>
        {
            if (approved) { DetachEditorTabs(); return; }
            var dirty = _editorStates.Values.Count(state => state.Vm.IsDirty);
            if (dirty == 0) { DetachEditorTabs(); return; }
            e.Cancel = true;
            var message = dirty == 1
                ? "关闭将放弃当前文件的未保存修改，是否继续？"
                : $"有 {dirty} 个文件尚未保存，关闭将放弃这些修改，是否继续？";
            if (await ConfirmOwnedAsync(window, "未保存的修改", message)) { approved = true; window.Close(); }
        };
        window.Closed += (_, _) => { DetachEditorTabs(); _editorWindow = null; _editorTabControl = null; };
        window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.S, KeyModifiers.Control),
            Command = new RelayCommand(() => { if (ActiveEditorTab(tabs) is { } state) _ = state.Vm.SaveCommand.ExecuteAsync(null); })
        });
        window.Show(DialogOwner); // 非模态：不阻塞调用方，窗口与标签自行管理生命周期
        await Task.CompletedTask;
    }

    private EditorTabState? ActiveEditorTab(TabControl tabs)
        => tabs.SelectedItem is TabItem selected && _editorStates.TryGetValue(selected, out var state)
            ? state
            : _editorStates.Values.LastOrDefault();

    private void ActivateEditorTab(Window window, TabControl tabs)
    {
        if (ActiveEditorTab(tabs) is not { } state) return;
        window.Title = "编辑 · " + state.Document.Path;
        window.DataContext = state.Vm;
    }

    private void DetachEditorTabs()
    {
        foreach (var state in _editorStates.Values.ToArray())
        {
            try { state.Detach(); }
            catch (Exception) { }
        }
        _editorStates.Clear();
    }

    private Control BuildEditorTabHeader(Window window, TabControl tabs, TabItem tab, FileEditorViewModel vm, TextDocument document)
    {
        var label = new TextBlock
        {
            Text = System.IO.Path.GetFileName(document.Path),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 210,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        ToolTip.SetTip(label, document.Path);
        var close = new Button { Padding = new Thickness(4, 2) };
        close.Classes.Add("flat");
        close.Content = new StrokeIcon { Symbol = "close", Width = 11, Height = 11 };
        ToolTip.SetTip(close, "关闭标签");
        close.Click += async (_, _) =>
        {
            if (!_editorStates.TryGetValue(tab, out var state)) return;
            if (state.Vm.IsDirty && !await ConfirmOwnedAsync(window, "未保存的修改", $"关闭将放弃 {state.Name} 的未保存修改，是否继续？")) return;
            _editorStates.Remove(tab);
            try { state.Detach(); }
            catch (Exception) { }
            tabs.Items.Remove(tab);
            if (tabs.Items.Count == 0) window.Close();
            else ActivateEditorTab(window, tabs);
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.Add(label);
        panel.Children.Add(close);
        return panel;
    }

    private void AddEditorTab(Window window, TabControl tabs, TextDocument document, Func<TextDocument, string, Task<TextDocument>> save)
    {
        var vm = new FileEditorViewModel(document, save, codec);
        var root = new DockPanel();
        var header = new StackPanel { Spacing = 9, Margin = new Thickness(16, 10) };
        var saveRow = new DockPanel { LastChildFill = true };
        var saveButton = new Button { Content = "保存  Ctrl+S", Command = vm.SaveCommand, Margin = new Thickness(0, 0, 16, 0) };
        saveButton.Classes.Add("primary"); DockPanel.SetDock(saveButton, Dock.Left); saveRow.Children.Add(saveButton);
        var status = Text(""); status.Name = "SaveStatus"; status.TextWrapping = TextWrapping.NoWrap; status.TextTrimming = TextTrimming.CharacterEllipsis;
        status.VerticalAlignment = VerticalAlignment.Center; status.Bind(TextBlock.TextProperty, CompiledBinding.Create((FileEditorViewModel x) => x.Status));
        var feedback = new Border { Name = "SaveFeedback", Padding = new Thickness(10, 5), Child = status };
        saveRow.Children.Add(feedback);
        void UpdateSaveFeedback()
        {
            if (vm.IsSaveSuccessful) ThemeService.Bind(feedback, Border.BackgroundProperty, "SuccessBackground");
            else feedback.Background = Brushes.Transparent;
            ThemeService.Bind(status, TextBlock.ForegroundProperty, vm.IsSaveSuccessful ? "SuccessForeground" : "ForegroundBrush");
        }
        UpdateSaveFeedback();
        vm.SaveFailed += async message =>
        {
            var error = Window("保存失败", 560, 320);
            var body = Stack(); body.Children.Add(Text("文件未能保存", 18)); body.Children.Add(Text(document.Path)); body.Children.Add(Text(message));
            Layout(error, body, Button("返回编辑", () => error.Close(), true));
            await error.ShowDialog(window);
        };
        header.Children.Add(saveRow);
        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        options.Children.Add(new TextBlock { Text = "编码", VerticalAlignment = VerticalAlignment.Center });
        var encodings = new ComboBox { Name = "EncodingSelector", ItemsSource = vm.Encodings, SelectedItem = vm.SelectedEncoding, Width = 180,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<TextEncodingInfo>((item, _) => new TextBlock { Text = item?.Name }) };
        encodings.Bind(ComboBox.SelectedItemProperty, CompiledBinding.Create((FileEditorViewModel x) => x.SelectedEncoding, mode: BindingMode.TwoWay));
        ToolTip.SetTip(encodings, "选择保存编码；如果显示乱码，选择正确编码后点击“按此编码重读”。"); options.Children.Add(encodings);
        var reload = new Button { Name = "ReloadEncoding", Content = "按此编码重读" };
        reload.Click += async (_, _) =>
        {
            if (vm.HasTextChanges && !await ConfirmOwnedAsync(window, "重新读取", "重新读取会放弃未保存的文本修改，是否继续？")) return;
            await vm.ReloadCommand.ExecuteAsync(null);
        };
        options.Children.Add(reload);
        options.Children.Add(new TextBlock { Text = "语法", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var syntaxes = new ComboBox { Name = "SyntaxSelector", ItemsSource = vm.Syntaxes, SelectedItem = vm.SelectedSyntax, Width = 115,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<EditorSyntax>((item, _) => new TextBlock { Text = item?.Name }) };
        syntaxes.Bind(ComboBox.SelectedItemProperty, CompiledBinding.Create((FileEditorViewModel x) => x.SelectedSyntax, mode: BindingMode.TwoWay));
        options.Children.Add(syntaxes); header.Children.Add(options);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new StackPanel { Spacing = 3, Margin = new Thickness(14, 6) };
        var info = new TextBlock { FontSize = 11 }; ThemeService.Bind(info, TextBlock.ForegroundProperty, "SecondaryForeground");
        info.Bind(TextBlock.TextProperty, CompiledBinding.Create((FileEditorViewModel x) => x.DocumentInfo));
        var note = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap }; ThemeService.Bind(note, TextBlock.ForegroundProperty, "MutedForeground");
        note.Bind(TextBlock.TextProperty, CompiledBinding.Create((FileEditorViewModel x) => x.EncodingNote));
        footer.Children.Add(info); footer.Children.Add(note); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var input = new CodeEditor { Text = vm.Content, SyntaxHighlighting = vm.SelectedSyntax.Highlighting };
        void ApplyEditorFont()
        {
            var settings = appLock?.Settings ?? new AppSettings();
            input.FontFamily = new FontFamily(settings.EditorFontFamily); input.FontSize = settings.EditorFontSize;
        }
        ApplyEditorFont();
        if (appLock != null) appLock.SettingsChanged += ApplyEditorFont;
        input.TextChanged += (_, _) => { if (vm.Content != input.Text) vm.Content = input.Text; };
        input.InputRejected += message => vm.Status = message;
        vm.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(vm.IsSaveSuccessful)) UpdateSaveFeedback();
            if (change.PropertyName == nameof(vm.Content) && input.Text != vm.Content) input.Text = vm.Content;
            if (change.PropertyName == nameof(vm.SelectedSyntax)) input.SyntaxHighlighting = vm.SelectedSyntax.Highlighting;
        };
        root.Children.Add(input);
        var tab = new TabItem { Content = root };
        tab.Header = BuildEditorTabHeader(window, tabs, tab, vm, document);
        void Detach() { if (appLock != null) appLock.SettingsChanged -= ApplyEditorFont; }
        _editorStates[tab] = new EditorTabState(document, vm, Detach);
        tabs.Items.Add(tab);
        tabs.SelectedItem = tab;
        ActivateEditorTab(window, tabs);
    }
    private async Task<bool> ConfirmOwnedAsync(Window owner, string title, string message)
    {
        var window = Window(title, 460, 220); var body = Stack(); body.Children.Add(Text(message));
        Layout(window, body, Button("继续编辑", () => window.Close(false)), Button("放弃修改", () => window.Close(true), true));
        return await window.ShowDialog<bool>(owner);
    }

    public async Task<int?> PermissionsAsync(RemoteEntry entry)
    {
        var vm = new PermissionsViewModel(entry.Permissions); var window = Window("属性与权限", 500, 660); window.DataContext = vm;
        var body = Stack(); body.Children.Add(Text(entry.Name, 20)); body.Children.Add(Text(entry.FullPath));
        body.Children.Add(Text($"类型：{entry.Kind}　大小：{entry.SizeText}\n修改：{entry.ModifiedText}\n所有者 UID:GID：{entry.OwnerText}"));
        body.Children.Add(Field("Unix 权限（八进制）", Input((PermissionsViewModel x) => x.Octal)));
        var bits = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), RowSpacing = 8 };
        for (var i = 0; i < vm.Bits.Length; i++)
        {
            var check = new CheckBox { Content = vm.Bits[i].Label, DataContext = vm.Bits[i] };
            check.Bind(CheckBox.IsCheckedProperty, CompiledBinding.Create((PermissionBitViewModel x) => x.IsSet, mode: BindingMode.TwoWay));
            Grid.SetColumn(check, i % 3); Grid.SetRow(check, i / 3); bits.Children.Add(check);
        }
        body.Children.Add(bits); body.Children.Add(Text("仅修改当前项目，不递归修改子目录。")); var error = Text(""); ThemeService.Bind(error, TextBlock.ForegroundProperty, "ErrorForeground"); body.Children.Add(error);
        Layout(window, body, Button("取消", () => window.Close()), Button("应用权限", () => { try { window.Close((int?)vm.Build()); } catch (Exception ex) { error.Text = ex.Message; } }, true));
        return await window.ShowDialog<int?>(DialogOwner);
    }

    public Task<TunnelDefinition?> EditTunnelAsync() => ShowTunnelEditorAsync(null);
    public Task<TunnelDefinition?> EditTunnelAsync(TunnelDefinition definition) => ShowTunnelEditorAsync(definition);
    private async Task<TunnelDefinition?> ShowTunnelEditorAsync(TunnelDefinition? definition)
    {
        var vm = new TunnelEditorViewModel(definition); var window = Window(definition == null ? "新建端口转发" : "编辑端口转发", 520, 650); window.DataContext = vm; var body = Stack();
        body.Children.Add(Text("端口转发", 20));
        body.Children.Add(Field("名称", Input((TunnelEditorViewModel x) => x.Name)));
        body.Children.Add(Field("转发方向", Choice((TunnelEditorViewModel x) => x.DirectionIndex, "本地 → 远程（本地转发）", "远程 → 本地（远程转发）")));
        body.Children.Add(Field("监听地址", Input((TunnelEditorViewModel x) => x.BindHost))); body.Children.Add(Field("监听端口", Input((TunnelEditorViewModel x) => x.BindPort)));
        body.Children.Add(Field("目标地址", Input((TunnelEditorViewModel x) => x.TargetHost))); body.Children.Add(Field("目标端口", Input((TunnelEditorViewModel x) => x.TargetPort)));
        body.Children.Add(Text("本地转发的目标由服务器访问；远程转发的目标由本机访问。远程监听受服务器 GatewayPorts 等策略限制。"));
        if (definition != null) body.Children.Add(Text("保存后，正在运行的规则会重新应用；已停止的规则保持停止。同一连接的所有标签同步更新。", 12));
        var error = Text(""); ThemeService.Bind(error, TextBlock.ForegroundProperty, "ErrorForeground"); error.Bind(TextBlock.TextProperty, CompiledBinding.Create((TunnelEditorViewModel x) => x.Error)); body.Children.Add(error);
        Layout(window, body, Button("取消", () => window.Close()), Button("保存规则", () => { try { window.Close(vm.Build()); } catch (Exception ex) { vm.Error = ex.Message; } }, true));
        return await window.ShowDialog<TunnelDefinition?>(DialogOwner);
    }

    public async Task<AppSettings?> SettingsAsync(AppSettings settings)
    {
        var fonts = FontManager.Current.SystemFonts.Select(font => font.Name).Append(settings.TerminalFontFamily).Append(settings.EditorFontFamily)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var vm = new SettingsViewModel(settings, appLock?.Passwords ?? new LockPasswordHasher(), fonts, lockCoordinator?.SupportsSystemSessionMonitoring ?? OperatingSystem.IsWindows(), updates, this);
        var window = Window("设置", 700, 620); window.MinWidth = 620; window.MinHeight = 500; window.DataContext = vm;
        var root = new Grid { RowDefinitions = new("*,Auto,Auto") };
        root.Children.Add(new SettingsView { DataContext = vm });
        var error = Text(""); error.Margin = new Thickness(26, 4); ThemeService.Bind(error, TextBlock.ForegroundProperty, "ErrorForeground");
        error.Bind(TextBlock.TextProperty, CompiledBinding.Create((SettingsViewModel x) => x.Error)); Grid.SetRow(error, 1); root.Children.Add(error);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Margin = new Thickness(24, 14) };
        footer.Children.Add(Button("取消", () => window.Close()));
        var save = new Button { Name = "SaveSettings", Content = "保存", Command = vm.SaveCommand, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center }; save.Classes.Add("primary"); footer.Children.Add(save);
        Grid.SetRow(footer, 2); root.Children.Add(footer); window.Content = root;
        root.Bind(InputElement.IsEnabledProperty, CompiledBinding.Create((SettingsViewModel x) => !x.IsSaving));
        vm.Saved += result => window.Close(result);
        window.Closing += (_, e) => { if (vm.IsSaving && vm.NewPassword.Length > 0) e.Cancel = true; };
        window.Closed += (_, _) => vm.ClearPasswords();
        return await window.ShowDialog<AppSettings?>(DialogOwner);
    }
}
