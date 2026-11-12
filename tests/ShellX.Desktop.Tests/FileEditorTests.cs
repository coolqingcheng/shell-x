using Avalonia.Headless.XUnit;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaEdit.Highlighting;
using ShellX.Core.Files;
using ShellX.Desktop.Controls;
using ShellX.Desktop.Features.FileEditor;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;

namespace ShellX.Desktop.Tests;

public sealed class FileEditorTests
{
    [AvaloniaFact]
    public async Task EncodingAndSyntaxControlsUpdateTheEditorAndSaveChosenEncoding()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-editor-ui-" + Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(root, "config.json"); var codec = new TextCodec(); var local = new LocalFileSystem(root);
        const string content = "{\r\n  \"name\": \"中文配置\",\r\n  \"port\": 8080,\r\n  \"enabled\": true\r\n}\r\n";
        await File.WriteAllBytesAsync(path, codec.Encode(content, TextEncodingInfo.Find(936)));
        var original = await local.ReadTextAsync(path);
        var owner = new Window(); owner.Show();
        var editing = new DialogService(codec) { Owner = owner }.EditFileAsync(original, (document, text) => local.SaveTextAsync(document, text));
        var window = owner.OwnedWindows.Single(); var vm = (FileEditorViewModel)window.DataContext!;
        try
        {
            await Task.Delay(60);
            var code = window.GetVisualDescendants().OfType<CodeEditor>().Single();
            Assert.True(code.TextArea.Bounds.Width > 500 && code.TextArea.Bounds.Height > 200);
            var encodings = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "EncodingSelector");
            var syntaxes = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "SyntaxSelector");
            Assert.Equal("JSON", code.SyntaxHighlighting.Name); Assert.True(code.ShowLineNumbers);
            encodings.SelectedItem = TextEncodingInfo.Find(936);
            var reload = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ReloadEncoding");
            reload.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var timeout = DateTime.UtcNow.AddSeconds(5);
            while (!vm.Status.StartsWith("已按")) { Assert.True(DateTime.UtcNow < timeout, vm.Status); await Task.Delay(20); }
            Assert.Equal(content, code.Text); Assert.Equal(936, vm.SelectedEncoding.CodePage);
            encodings.SelectedItem = TextEncodingInfo.Find(65001, true);
            await vm.SaveCommand.ExecuteAsync(null); Assert.False(vm.IsDirty);
            Assert.True(vm.IsSaveSuccessful);
            var status = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "SaveStatus");
            Assert.StartsWith("保存成功", status.Text);
            Assert.Equal(Color.Parse("#73C991"), Assert.IsAssignableFrom<ISolidColorBrush>(status.Foreground).Color);
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, bytes[..3]);
            Assert.Equal(content, (await local.ReadTextAsync(path)).Content);
            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
            {
                foreach (var (name, sample) in new[]
                {
                    ("JSON", content),
                    ("XML", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<!-- 中文配置 -->\r\n<configuration>\r\n  <server name=\"example.local\" port=\"8080\" />\r\n</configuration>\r\n"),
                    ("Nginx", "# 示例站点\r\nserver {\r\n    listen 80;\r\n    server_name example.local;\r\n    location / {\r\n        proxy_pass http://127.0.0.1:8080;\r\n        proxy_set_header Host $host;\r\n    }\r\n}\r\n")
                })
                {
                    syntaxes.SelectedItem = vm.Syntaxes.Single(s => s.Name == name); vm.Content = sample;
                    await Task.Delay(50); Assert.Equal(name, code.SyntaxHighlighting.Name);
                    using var bitmap = new RenderTargetBitmap(new PixelSize(1050, 720), new Vector(96, 96));
                    bitmap.Render(window); bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, "editor-" + name.ToLowerInvariant() + ".png"), PngBitmapEncoderOptions.Default);
                }
                vm.Content = content;
            }
            vm.Content += "😀"; Assert.False(vm.IsSaveSuccessful);
            encodings.SelectedItem = TextEncodingInfo.Find(936);
            await vm.SaveCommand.ExecuteAsync(null);
            var error = Assert.Single(window.OwnedWindows);
            Assert.Equal("保存失败", error.Title); Assert.True(error.IsVisible);
            Assert.Contains(error.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("无法表示部分字符") == true);
            Assert.False(vm.IsSaveSuccessful); Assert.True(vm.IsDirty);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } errorPreview)
            {
                await Task.Delay(50);
                using var bitmap = new RenderTargetBitmap(new PixelSize(560, 320), new Vector(96, 96));
                bitmap.Render(error); bitmap.Save(Path.Combine(Path.GetDirectoryName(errorPreview)!, "editor-save-error.png"), PngBitmapEncoderOptions.Default);
            }
            error.Close();
            vm.Content = content; encodings.SelectedItem = TextEncodingInfo.Find(65001, true);
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.True(vm.IsSaveSuccessful); Assert.Empty(window.OwnedWindows);
            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } successPreview)
            {
                await Task.Delay(50);
                using var bitmap = new RenderTargetBitmap(new PixelSize(1050, 720), new Vector(96, 96));
                bitmap.Render(window); bitmap.Save(Path.Combine(Path.GetDirectoryName(successPreview)!, "editor-save-success.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); await editing; owner.Close(); Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task OpeningAnotherFileAddsATabToTheSameNonModalEditorWindow()
    {
        var codec = new TextCodec();
        var owner = new Window(); owner.Show();
        var dialog = new DialogService(codec) { Owner = owner };
        var first = dialog.EditFileAsync(new("/etc/app.conf", "one", 3, DateTime.UtcNow), (document, _) => Task.FromResult(document));
        var second = dialog.EditFileAsync(new("/etc/nginx.conf", "two", 3, DateTime.UtcNow), (document, _) => Task.FromResult(document));
        Window? window = null;
        try
        {
            // 非模态：两次打开都在窗口出现后立即完成，不等用户关闭。
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            await second.WaitAsync(TimeSpan.FromSeconds(5));
            window = Assert.Single(owner.OwnedWindows);
            await Task.Delay(40);
            var tabs = window.GetVisualDescendants().OfType<ShellTabItemView>().ToArray();
            Assert.Equal(2, tabs.Length);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "nginx.conf");
            Assert.IsType<FileEditorViewModel>(window.DataContext);
            Assert.EndsWith("nginx.conf", window.Title);

            // 同一个文件再次点击只聚焦已有标签。
            var third = dialog.EditFileAsync(new("/etc/nginx.conf", "two", 3, DateTime.UtcNow), (document, _) => Task.FromResult(document));
            await third.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, window.GetVisualDescendants().OfType<ShellTabItemView>().Count());

            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
            {
                await Task.Delay(50);
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, "editor-tabs.png"), PngBitmapEncoderOptions.Default);
            }
            // 关闭窗口即结束，不再残留编辑器窗口。
            window.Close();
            await Task.Delay(40);
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            foreach (var owned in owner.OwnedWindows.ToArray()) owned.Close();
            owner.Close();
        }
    }
    [AvaloniaTheory]
    [InlineData("/app/config.json", "{\"name\":\"value\",\"count\":12,\"ok\":true}", "JSON", "Property", "String", "Keyword", "Number")]
    [InlineData("/app/config.xml", "<root name=\"value\"><child>text</child></root>", "XML", "Keyword", "Property", "String")]
    [InlineData("/etc/nginx/sites-available/default", "proxy_pass http://$upstream:8080; # comment", "Nginx", "Property", "Variable", "Number", "Comment")]
    public void DetectsLanguagesAndHighlightsDifferentTokenKinds(string path, string content, string language, params string[] expected)
    {
        var syntax = EditorSyntaxes.Detect(path, content); Assert.Equal(language, syntax.Name);
        using var highlighter = new DocumentHighlighter(new AvaloniaEdit.Document.TextDocument(content), syntax.Highlighting!);
        var colors = highlighter.HighlightLine(1).Sections.Select(section => section.Color.Name).ToArray();
        foreach (var name in expected) Assert.Contains(name, colors);
    }

    [AvaloniaFact]
    public async Task ReopeningWithSelectedEncodingAndSavingConversionUseTheRawSnapshot()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-editor-vm-" + Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(root, "sample.conf"); var codec = new TextCodec(); var local = new LocalFileSystem(root);
        try
        {
            var big5 = TextEncodingInfo.Find(950);
            await File.WriteAllBytesAsync(path, codec.Encode("name=繁體中文\r\n", big5));
            var original = await local.ReadTextAsync(path);
            var vm = new FileEditorViewModel(original, (document, content) => local.SaveTextAsync(document, content), codec);
            vm.SelectedEncoding = big5; await vm.ReloadCommand.ExecuteAsync(null);
            Assert.Equal("name=繁體中文\r\n", vm.Content); Assert.False(vm.IsDirty);
            vm.Content += "# edited\r\n"; await vm.SaveCommand.ExecuteAsync(null);
            Assert.StartsWith("保存成功", vm.Status); Assert.False(vm.IsDirty);
            vm.SelectedEncoding = TextEncodingInfo.Utf8; Assert.True(vm.IsDirty);
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.StartsWith("保存成功", vm.Status); Assert.False(vm.IsDirty);
            Assert.Equal(vm.Content, new System.Text.UTF8Encoding(false, true).GetString(await File.ReadAllBytesAsync(path)));
            vm.SelectedEncoding = big5; vm.Content += "😀";
            var before = await File.ReadAllBytesAsync(path); await vm.SaveCommand.ExecuteAsync(null);
            Assert.StartsWith("保存失败", vm.Status); Assert.Equal(before, await File.ReadAllBytesAsync(path)); Assert.True(vm.IsDirty);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public void LargeAndLongLineInputIsRejectedBeforeInsertion()
    {
        var editor = new CodeEditor { Text = "hello" }; string? reason = null;
        editor.InputRejected += message => reason = message;
        editor.TextArea.PerformTextInput(new string('x', TextEditorLimits.MaxCharacters + 1));
        Assert.Equal("hello", editor.Text); Assert.NotNull(reason);
        editor.TextArea.PerformTextInput(new string('x', TextEditorLimits.MaxLineLength + 1));
        Assert.Equal("hello", editor.Text);
    }
}
