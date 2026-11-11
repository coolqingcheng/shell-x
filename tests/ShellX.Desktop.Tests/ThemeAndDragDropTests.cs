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
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light, AppTheme.OneDark, AppTheme.Dracula, AppTheme.SolarizedLight, AppTheme.Dark })
            {
                ThemeService.Apply(theme); await Task.Delay(60);
                var palette = ThemeCatalog.Of(theme);
                var background = palette.Ui[ThemeCatalog.AppBackground];
                Assert.Equal(Color.Parse(background), ((ISolidColorBrush)window.Background!).Color);
                Assert.Equal(Color.Parse(background), ((ISolidColorBrush)editor.Background!).Color);
                Assert.Equal(Color.Parse(background), ((ISolidColorBrush)terminal.GetVisualDescendants().OfType<TerminalControl>().Single().Background!).Color);
                var grids = files.GetVisualDescendants().OfType<DataGrid>().ToArray();
                Assert.Equal(2, grids.Length);
                Assert.All(grids, grid => Assert.Equal(Color.Parse(background), ((ISolidColorBrush)grid.Background!).Color));
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
            foreach (var theme in new[] { AppTheme.OneDark, AppTheme.Dracula, AppTheme.SolarizedLight, AppTheme.Dark })
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
        using var browser = new FileBrowserViewModel(dialogs, NullLogger<FileBrowserViewModel>.Instance, new LocalFileSystem(source));
        browser.Attach(session.Files, CancellationToken.None);
        var view = new FileBrowserView { DataContext = browser };
        var window = new Window { Content = view, Width = 1280, Height = 600 }; window.Show();
        try
        {
            await browser.NavigateAsync(remote); await browser.Local.NavigateAsync(source);
            var remotePane = view.FindControl<RemoteFileBrowserView>("RemotePane")!;
            var localPane = view.FindControl<LocalFileBrowserView>("LocalPane")!;
            // Windows Explorer uses the same File format for both files and directories.
            var external = new DataTransfer();
            external.Add(DataTransferItem.CreateFile((await window.StorageProvider.TryGetFolderFromPathAsync(folder))!));
            external.Add(DataTransferItem.CreateFile((await window.StorageProvider.TryGetFileFromPathAsync(Path.Combine(root, "external.txt")))!));
            Assert.Equal(DragDropEffects.Copy, Over(remotePane, external));
            Assert.True(remotePane.FindControl<Border>("DropHint")!.IsVisible);
            var externalPoint = remotePane.TranslatePoint(new Point(90, 170), window)!.Value;
            window.DragDrop(externalPoint, RawDragEventType.DragEnter, external, DragDropEffects.Copy);
            window.DragDrop(externalPoint, RawDragEventType.Drop, external, DragDropEffects.Copy);
            await Until(() => browser.Transfers.Count == 2); await browser.DrainTransfersAsync();
            Assert.Equal("已完成", browser.Transfers[0].Status);
            Assert.True(await session.Files.ExistsAsync(remote + "/文件夹/empty"));
            Assert.Equal("hidden 中文", (await session.Files.ReadTextAsync(remote + "/文件夹/.hidden")).Content);
            using (var local = FileDragDrop.LocalPayload(browser, [Path.Combine(source, "one.txt"), Path.Combine(source, "two.txt")]))
            {
                Assert.Equal(DragDropEffects.None, Over(localPane, local.Data));
                Assert.Equal(DragDropEffects.Copy, Over(remotePane, local.Data));
                var grid = localPane.FindControl<DataGrid>("LocalGrid")!;
                grid.SelectedItems.Clear();
                foreach (var entry in browser.Local.Entries.Where(e => !e.IsDirectory)) grid.SelectedItems.Add(entry);
                await Task.Delay(60);
                var row = grid.GetVisualDescendants().OfType<DataGridRow>().First(r => r.DataContext is LocalEntry { IsDirectory: false });
                var point = row.TranslatePoint(new Point(50, row.Bounds.Height / 2), window)!.Value;
                var destination = remotePane.TranslatePoint(new Point(80, 160), window)!.Value;
                window.MouseMove(point); window.MouseDown(point, MouseButton.Left);
                Assert.Equal(2, grid.SelectedItems.Count);
                window.MouseMove(point + new Vector(10, 0), RawInputModifiers.LeftMouseButton); await Task.Delay(20);
                window.MouseMove(destination, RawInputModifiers.LeftMouseButton); await Task.Delay(20); Assert.Empty(dialogs.Errors); Assert.True(remotePane.FindControl<Border>("DropHint")!.IsVisible, "原生拖放未到达远程区域"); window.MouseUp(destination, MouseButton.Left);
                await Until(() => browser.Transfers.Count == 4); await browser.DrainTransfersAsync();
            }
            await browser.NavigateAsync(remote); await browser.Local.NavigateAsync(target);
            browser.SelectedEntries.AddRange(browser.Entries);
            var selection = browser.CaptureDownloadSelection()!;
            using (var payload = FileDragDrop.RemotePayload(selection))
            {
                Assert.Equal(DragDropEffects.None, Over(remotePane, payload.Data));
                Assert.Equal(DragDropEffects.Copy, Over(localPane, payload.Data)); Drop(localPane, payload.Data);
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
                localPane.IsEnabled = false; Assert.Equal(DragDropEffects.None, Over(localPane, payload.Data)); localPane.IsEnabled = true;
                using var staleLocal = FileDragDrop.LocalPayload(browser, [Path.Combine(source, "one.txt")]);
                browser.Attach(session.Files, CancellationToken.None); // 同一个适配器重新挂载也要让旧拖动失效。
                Assert.False(selection.IsValid); Assert.Equal(DragDropEffects.None, Over(remotePane, staleLocal.Data));
                browser.Attach(null, CancellationToken.None); Assert.False(selection.IsValid);
                Assert.Equal(DragDropEffects.None, Over(localPane, payload.Data)); Assert.Equal(DragDropEffects.None, Over(remotePane, external));
            }
            Assert.Empty(dialogs.Errors);
        }
        finally { window.Close(); await session.Files.DeleteAsync(remote); Directory.Delete(root, true); }
    }

    private static DragDropEffects Over(Control pane, IDataTransfer data)
    {
        var e = new DragEventArgs(DragDrop.DragOverEvent, data, pane, new Point(80, 120), KeyModifiers.None) { DragEffects = DragDropEffects.Copy };
        pane.RaiseEvent(e); return e.DragEffects;
    }
    private static void Drop(Control pane, IDataTransfer data)
        => pane.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, pane, new Point(80, 120), KeyModifiers.None) { DragEffects = DragDropEffects.Copy });
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
