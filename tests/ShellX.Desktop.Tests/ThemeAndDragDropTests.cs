using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Iciclecreek.Terminal;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Core.Connections;
using ShellX.Core.Files;
using ShellX.Core.Settings;
using ShellX.Core.Tunnels;
using ShellX.Desktop.Controls;
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Desktop.Features.FileEditor;
using ShellX.Desktop.Features.Settings;
using ShellX.Desktop.Services;
using ShellX.Desktop.Styles;
using ShellX.Infrastructure.Files;
using ShellX.Infrastructure.Security;
using ShellX.Infrastructure.Ssh;

namespace ShellX.Desktop.Tests;

public sealed class ThemeAndDragDropTests
{
    [AvaloniaFact]
    public async Task ThemeChangesExistingFilePanesTerminalAndEditorAndSettingsRetainFonts()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-theme-" + Guid.NewGuid().ToString("N"))).FullName;
        using var browser = new FileBrowserViewModel(new Dialogs(), NullLogger<FileBrowserViewModel>.Instance, new LocalFileSystem(root));
        await File.WriteAllTextAsync(Path.Combine(root, "config.json"), "{\"enabled\": true}");
        var files = new FileBrowserView { DataContext = browser };
        var editor = new CodeEditor { Text = "{\n  \"server\": \"localhost\",\n  \"port\": 22,\n  \"enabled\": true\n}", SyntaxHighlighting = EditorSyntaxes.All.Single(s => s.Name == "JSON").Highlighting };
        var terminal = new TerminalHost();
        var panel = new Grid { RowDefinitions = new("150,*,180") };
        panel.Children.Add(terminal); Grid.SetRow(files, 1); panel.Children.Add(files); Grid.SetRow(editor, 2); panel.Children.Add(editor);
        var window = new Window { Width = 1180, Height = 780, Content = panel }; window.Show();
        try
        {
            await browser.Local.InitializeAsync();
            foreach (var theme in ThemeCatalog.All.Append(AppTheme.Dark))
            {
                ThemeService.Apply(theme); await Task.Delay(60);
                var palette = ThemeCatalog.Of(theme);
                var background = palette.Ui[ThemeCatalog.AppBackground];
                Assert.Equal(Color.Parse(background), ((ISolidColorBrush)window.Background!).Color);
                Assert.Equal(Color.Parse(background), ((ISolidColorBrush)editor.Background!).Color);
                Assert.Equal(Color.Parse(background), ((ISolidColorBrush)terminal.GetVisualDescendants().OfType<TerminalControl>().Single().Background!).Color);
                // 文件面板现在是两棵树，树列表的背景要跟着主题走。
                var treeBackground = Color.Parse(palette.Ui[ThemeCatalog.TreeBackground]);
                var panes = files.GetVisualDescendants().OfType<RemoteFileBrowserView>()
                    .Cast<Control>()
                    .Concat(files.GetVisualDescendants().OfType<LocalFileBrowserView>()).ToArray();
                Assert.Equal(2, panes.Length);
                Assert.All(panes, pane => Assert.Equal(treeBackground,
                    ((ISolidColorBrush)pane.FindControl<ScrollViewer>("TreeList")!.Background!).Color));
                var syntax = EditorSyntaxes.All.Single(s => s.Name == "JSON").Highlighting!.GetNamedColor("Property").Foreground.GetColor(null!);
                Assert.Equal(Color.Parse(palette.Syntax.Property), syntax);
                if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
                {
                    using var bitmap = new RenderTargetBitmap(new PixelSize(1180, 780), new Vector(96, 96));
                    bitmap.Render(window); bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, $"theme-{theme.ToString().ToLowerInvariant()}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            AppSettings? saved = null;
            var settings = new SettingsViewModel(new() { TerminalFontSize = 17, EditorFontSize = 19 }, new LockPasswordHasher(), ["Consolas"]) { ThemeIndex = 1 };
            settings.Saved += result => saved = result;
            await settings.SaveCommand.ExecuteAsync(null);
            Assert.NotNull(saved); Assert.Equal(AppTheme.Light, saved.Theme); Assert.Equal(17, saved.TerminalFontSize); Assert.Equal(19, saved.EditorFontSize);
        }
        finally { ThemeService.Apply(AppTheme.Dark); window.Close(); Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task PopularThemesDriveTerminalAnsiColorsAndEditorSyntax()
    {
        var editor = new CodeEditor { Text = "{\"a\": 1}", SyntaxHighlighting = EditorSyntaxes.All.Single(s => s.Name == "JSON").Highlighting };
        var terminal = new TerminalHost();
        var grid = new Grid { RowDefinitions = new("*,*") };
        Grid.SetRow(editor, 0); Grid.SetRow(terminal, 1);
        grid.Children.Add(editor); grid.Children.Add(terminal);
        var window = new Window { Width = 900, Height = 600, Content = grid }; window.Show();
        try
        {
            foreach (var theme in ThemeCatalog.All.Append(AppTheme.Dark))
            {
                ThemeService.Apply(theme); await Task.Delay(80);
                var palette = ThemeCatalog.Of(theme);
                var control = terminal.GetVisualDescendants().OfType<TerminalControl>().Single();
                // 终端的 ANSI 调色板要跟着主题走，不然界面换了色而终端还是老样子。
                Assert.Equal(palette.Terminal.Green, control.Options!.Theme!.Green);
                Assert.Equal(Color.Parse(palette.Terminal.Background), ((ISolidColorBrush)control.Background!).Color);
                Assert.Equal(Color.Parse(palette.Ui[ThemeCatalog.AppBackground]), ((ISolidColorBrush)window.Background!).Color);
                Assert.Equal(Color.Parse(palette.Syntax.Keyword), EditorSyntaxes.All.Single(s => s.Name == "JSON").Highlighting!.GetNamedColor("Keyword").Foreground.GetColor(null!));
            }
        }
        finally { ThemeService.Apply(AppTheme.Dark); window.Close(); }
    }
    [AvaloniaFact]
    public async Task ConnectionTabsFollowThemeColors()
    {
        var tab = new ShellTabItemView { Title = "服务器", IsActive = true };
        var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Vertical };
        panel.Children.Add(tab);
        var window = new Window { Width = 320, Height = 90, Content = panel };
        window.Show();
        try
        {
            var background = tab.FindControl<ConnectionAccentBorder>("TabBackground")!;
            var indicator = tab.FindControl<ConnectionAccentBorder>("AccentLine")!;
            foreach (var theme in ThemeCatalog.All)
            {
                ThemeService.Apply(theme);
                await Task.Delay(60);
                var palette = ThemeCatalog.Of(theme);
                // 激活页签用主题的选中色，未激活页签跟标题栏同色。
                Assert.Equal(Color.Parse(palette.Ui[ThemeCatalog.SelectionBackground]), ((ISolidColorBrush)background.Background!).Color);
                Assert.Equal(Color.Parse(palette.Ui[ThemeCatalog.AccentForeground]), ((ISolidColorBrush)indicator.Background!).Color);
                tab.IsActive = false;
                await Task.Delay(30);
                Assert.Equal(Color.Parse(palette.Ui[ThemeCatalog.HeaderBackground]), ((ISolidColorBrush)background.Background!).Color);
                tab.IsActive = true;
                if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
                {
                    using var bitmap = new RenderTargetBitmap(new PixelSize(320, 90), new Vector(96, 96));
                    bitmap.Render(window);
                    bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, $"tab-{theme.ToString().ToLowerInvariant()}.png"), PngBitmapEncoderOptions.Default);
                }
            }
        }
        finally { ThemeService.Apply(AppTheme.Dark); window.Close(); }
    }

    [Fact]
    public void EveryThemeProvidesCompletePaletteAndRoundTripsThroughIndex()
    {
        var required = new[]
        {
            ThemeCatalog.AppBackground, ThemeCatalog.PanelBackground, ThemeCatalog.BorderColor, ThemeCatalog.ForegroundBrush,
            ThemeCatalog.EditorForeground, ThemeCatalog.RaisedBackground, ThemeCatalog.InputBackground, ThemeCatalog.HeaderBackground,
            ThemeCatalog.HoverBackground, ThemeCatalog.SelectionBackground, ThemeCatalog.MutedForeground, ThemeCatalog.SecondaryForeground,
            ThemeCatalog.IconForeground, ThemeCatalog.PlaceholderForeground, ThemeCatalog.FaintForeground, ThemeCatalog.AccentForeground,
            ThemeCatalog.LinkForeground, ThemeCatalog.FolderForeground, ThemeCatalog.CommandForeground, ThemeCatalog.SuccessForeground,
            ThemeCatalog.ErrorForeground, ThemeCatalog.WarningForeground, ThemeCatalog.WarningBackground, ThemeCatalog.SuccessBackground,
            ThemeCatalog.TreeBackground, ThemeCatalog.ChartBackground, ThemeCatalog.ChartGrid, ThemeCatalog.PurpleForeground,
            ThemeCatalog.OrangeForeground, ThemeCatalog.CommentForeground, ThemeCatalog.PrimaryBackground,
            ThemeCatalog.PrimaryHoverBackground, ThemeCatalog.PrimaryForeground
        };
        Assert.Equal(ThemeCatalog.All.Count, ThemeCatalog.All.Distinct().Count());
        var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var theme in ThemeCatalog.All)
        {
            var palette = ThemeCatalog.Of(theme);
            Assert.All(required, key => Assert.True(palette.Ui.ContainsKey(key), theme + " 缺少配色键 " + key));
            Assert.All(palette.Ui.Values, hex => Color.Parse(hex));
            // 终端 22 色与编辑器 7 种语法角色都要有值。
            Assert.All(new[]
            {
                palette.Terminal.Foreground, palette.Terminal.Background, palette.Terminal.Cursor, palette.Terminal.Selection,
                palette.Terminal.Black, palette.Terminal.Red, palette.Terminal.Green, palette.Terminal.Yellow, palette.Terminal.Blue,
                palette.Terminal.Magenta, palette.Terminal.Cyan, palette.Terminal.White, palette.Terminal.BrightBlack,
                palette.Terminal.BrightRed, palette.Terminal.BrightGreen, palette.Terminal.BrightYellow, palette.Terminal.BrightBlue,
                palette.Terminal.BrightMagenta, palette.Terminal.BrightCyan, palette.Terminal.BrightWhite
            }, hex => Color.Parse(hex));
            Assert.All(new[]
            {
                palette.Syntax.Comment, palette.Syntax.String, palette.Syntax.Property, palette.Syntax.Keyword,
                palette.Syntax.Number, palette.Syntax.Variable, palette.Syntax.Delimiter
            }, hex => Color.Parse(hex));
            // 终端语义着色是 24 位 RGB 的 R;G;B。
            Assert.All(new[] { palette.Highlight.Error, palette.Highlight.Warning, palette.Highlight.Success, palette.Highlight.Ip, palette.Highlight.Link },
                value => Assert.Equal(3, value.Split(';').Length));
            // 下拉框索引要能来回映射，否则设置页选错主题。
            var index = ThemeCatalog.ToIndex(theme);
            Assert.Equal(theme, ThemeCatalog.FromIndex(index));
            Assert.NotNull(ThemeCatalog.Variant(theme));
            signatures.Add(string.Join("|", required.Select(key => palette.Ui[key])));
        }
        // 每套主题的配色都不一样，避免“换了主题看起来没换”。
        Assert.Equal(ThemeCatalog.All.Count, signatures.Count);
        var settings = new SettingsViewModel(new AppSettings(), new LockPasswordHasher(), ["Consolas"]);
        Assert.Equal(ThemeCatalog.All.Count, settings.Themes.Length);
        Assert.Equal(ThemeCatalog.All.Select(ThemeCatalog.DisplayName), settings.Themes);
    }

    [AvaloniaFact]
    public async Task ExplorerAndInternalDropsTransferFilesAndFoldersWithExactBytesAndMergeConfirmation()
    {
        var fixturePath = Environment.GetEnvironmentVariable("SHELLX_TEST_FIXTURE");
        if (!File.Exists(fixturePath)) Assert.Skip("需要本机 SSH 测试服务。");
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-drop-" + Guid.NewGuid().ToString("N"))).FullName;
        var source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(root, "download")).FullName;
        var folder = Directory.CreateDirectory(Path.Combine(source, "文件夹", "子目录")).Parent!.FullName;
        Directory.CreateDirectory(Path.Combine(folder, "empty"));
        var bytes = Enumerable.Range(0, 4096).Select(i => (byte)(i % 256)).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(folder, "子目录", "binary.dat"), bytes);
        await File.WriteAllTextAsync(Path.Combine(folder, ".hidden"), "hidden 中文");
        await File.WriteAllTextAsync(Path.Combine(root, "external.txt"), "Explorer file");
        await File.WriteAllTextAsync(Path.Combine(source, "one.txt"), "first");
        await File.WriteAllTextAsync(Path.Combine(source, "two.txt"), "second");
        var profile = new ConnectionProfile { Host = "127.0.0.1", Port = config.RootElement.GetProperty("Port").GetInt32(), Username = "shellx" };
        await using var session = await new SshSessionFactory(NullLoggerFactory.Instance).ConnectAsync(profile, new("fixture-password"), _ => Task.FromResult(true), CancellationToken.None);
        var remote = "/drop-" + Guid.NewGuid().ToString("N"); await session.Files!.CreateAsync(remote, true);
        var dialogs = new Dialogs();
        var localFiles = new LocalFileSystem(source);
        using var browser = new FileBrowserViewModel(dialogs, NullLogger<FileBrowserViewModel>.Instance, localFiles);
        browser.Attach(session.Files, CancellationToken.None);
        var view = new FileBrowserView { DataContext = browser };
        var window = new Window { Content = view, Width = 1280, Height = 600 }; window.Show();
        try
        {
            await browser.NavigateAsync(remote); await browser.Local.NavigateAsync(source);
            // 远程树上的目标目录节点：拖到这里就是上传到这个目录。
            var remoteNode = new RemoteTreeNodeViewModel(
                new RemoteEntry("drop", remote, true, false, 0, DateTime.UtcNow, 493, 0, 0), session.Files!, CancellationToken.None, browser);
            // 本地树上的目标目录节点：拖到这里就是下载到这个目录。
            var localNode = new LocalTreeNodeViewModel(
                new LocalEntry("download", target, true, false, false, 0, DateTime.UtcNow), localFiles, CancellationToken.None, browser.Local);

            // 资源管理器拖进来的本地文件：上传到远程节点目录。
            var external = new DataTransfer();
            external.Add(DataTransferItem.CreateFile((await window.StorageProvider.TryGetFolderFromPathAsync(folder))!));
            external.Add(DataTransferItem.CreateFile((await window.StorageProvider.TryGetFileFromPathAsync(Path.Combine(root, "external.txt")))!));
            var externalPaths = FileDragDrop.ReadFilePaths(external)!;
            Assert.NotNull(externalPaths);
            await browser.DropLocalAsync(remoteNode, externalPaths);
            await Until(() => browser.Transfers.Count == 2); await browser.DrainTransfersAsync();
            Assert.Equal("已完成", browser.Transfers[0].Status);
            Assert.True(await session.Files.ExistsAsync(remote + "/文件夹/empty"));
            Assert.Equal("hidden 中文", (await session.Files.ReadTextAsync(remote + "/文件夹/.hidden")).Content);

            // 本地树节点拖到远程树节点：进程内载荷带的是本地路径。
            using (var local = FileDragDrop.LocalPayload(browser, [Path.Combine(source, "one.txt"), Path.Combine(source, "two.txt")]))
            {
                Assert.True(FileDragDrop.CanDrop(browser, true, local.Data));
                Assert.False(FileDragDrop.CanDrop(browser, false, local.Data));
                await browser.DropLocalAsync(remoteNode, FileDragDrop.ReadLocalPaths(local.Data)!);
            }
            await Until(() => browser.Transfers.Count == 4); await browser.DrainTransfersAsync();

            // 远程条目拖到本地树节点：下载到该目录。
            await browser.NavigateAsync(remote); await browser.Local.NavigateAsync(target);
            browser.SelectedEntries.AddRange(browser.Entries);
            var selection = browser.CaptureDownloadSelection()!;
            using (var payload = FileDragDrop.RemotePayload(selection))
            {
                Assert.False(FileDragDrop.CanDrop(browser, true, payload.Data));
                Assert.True(FileDragDrop.CanDrop(browser, false, payload.Data));
                await browser.Local.DropRemoteAsync(localNode, selection);
            }
            await Until(() => browser.Transfers.Count == 8); await browser.DrainTransfersAsync();
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(target, "文件夹", "子目录", "binary.dat")));
            Assert.True(Directory.Exists(Path.Combine(target, "文件夹", "empty")));
            Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(target, "one.txt")));
            Assert.Equal("second", await File.ReadAllTextAsync(Path.Combine(target, "two.txt")));
            Assert.Equal("Explorer file", await File.ReadAllTextAsync(Path.Combine(target, "external.txt")));
            Assert.All(browser.Transfers, t => Assert.Equal("已完成", t.Status));
            dialogs.Confirm = false;
            await File.WriteAllTextAsync(Path.Combine(target, "one.txt"), "keep");
            await File.WriteAllTextAsync(Path.Combine(target, "文件夹", ".hidden"), "keep folder");
            await browser.DownloadEntriesAsync(selection, target); await browser.DrainTransfersAsync();
            Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(target, "one.txt")));
            Assert.Equal("keep folder", await File.ReadAllTextAsync(Path.Combine(target, "文件夹", ".hidden")));
            Assert.Contains("合并本地文件夹", dialogs.Confirmations); Assert.Contains("覆盖本地文件", dialogs.Confirmations);
            using var staleLocal = FileDragDrop.LocalPayload(browser, [Path.Combine(source, "one.txt")]);
            browser.Attach(session.Files, CancellationToken.None); // 同一个适配器重新挂载也要让旧拖动失效。
            Assert.False(selection.IsValid); Assert.False(FileDragDrop.CanDrop(browser, true, staleLocal.Data));
            browser.Attach(null, CancellationToken.None); Assert.False(selection.IsValid);
            Assert.False(FileDragDrop.CanDrop(browser, false, FileDragDrop.RemotePayload(selection).Data));
            Assert.False(FileDragDrop.CanDrop(browser, true, external));
        }
        finally { window.Close(); await session.Files.DeleteAsync(remote); Directory.Delete(root, true); }
    }


    private static async Task Until(Func<bool> condition)
    { for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(20); Assert.True(condition()); }

    private sealed class Dialogs : IDialogService
    {
        public bool Confirm { get; set; } = true;
        public List<string> Confirmations { get; } = [];
        public List<string> Errors { get; } = [];
        public Task<bool> ConfirmAsync(string title, string message, bool danger = false) { Confirmations.Add(title); return Task.FromResult(Confirm); }
        public Task ShowErrorAsync(string message) { Errors.Add(message); return Task.CompletedTask; }
        public Task<ConnectionEditResult?> EditConnectionAsync(ConnectionProfile? profile, ConnectionSecret? secret) => Task.FromResult<ConnectionEditResult?>(null);
        public Task<string?> PromptAsync(string title, string label, string value = "", bool secret = false) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> PickUploadAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickDownloadAsync(string name) => Task.FromResult<string?>(null);
        public Task EditFileAsync(TextDocument document, Func<TextDocument, string, Task<TextDocument>> save) => Task.CompletedTask;
        public Task<int?> PermissionsAsync(RemoteEntry entry) => Task.FromResult<int?>(null);
        public Task<TunnelDefinition?> EditTunnelAsync() => Task.FromResult<TunnelDefinition?>(null);
        public Task<AppSettings?> SettingsAsync(AppSettings settings) => Task.FromResult<AppSettings?>(null);
    }
}
