using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using ShellX.Core.Settings;
using ShellX.Desktop.Features.FileEditor;
using ShellX.Desktop.Features.Security;
using ShellX.Desktop.Features.Settings;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Files;
using ShellX.Infrastructure.Security;
using ShellX.Desktop.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace ShellX.Desktop.Tests;

public sealed class AppLockTests
{
    [AvaloniaFact]
    public void PasswordAutomationCanEnterButCannotReadSecretText()
    {
        var input = new SecretTextBox { Text = "private-password" };
        var peer = ControlAutomationPeer.CreatePeerForElement(input)!;
        var provider = Assert.IsAssignableFrom<IValueProvider>(peer);
        Assert.Equal("", provider.Value); Assert.DoesNotContain("private-password", peer.GetName());
        provider.SetValue("new-private-password"); Assert.Equal("new-private-password", input.Text); Assert.Equal("", provider.Value);
        input.IsReadOnly = true; Assert.Throws<InvalidOperationException>(() => provider.SetValue("blocked"));
    }
    [AvaloniaFact]
    public async Task IdleUsesUserActivityAndStartupRequiresTheConfiguredPassword()
    {
        var clock = new ManualTime(); var passwords = new LockPasswordHasher();
        var settings = new AppSettings { LockPasswordHash = passwords.Hash("test-idle-password"), AutoLockEnabled = true, AutoLockMinutes = 120 };
        var state = new AppLockService(passwords, clock);
        state.Configure(settings, startup: true); Assert.True(state.IsLocked);
        state.Password = "test-idle-password"; await state.UnlockCommand.ExecuteAsync(null); Assert.False(state.IsLocked);
        clock.Advance(TimeSpan.FromMinutes(119)); state.CheckIdle(); Assert.False(state.IsLocked);
        state.RecordActivity(); clock.Advance(TimeSpan.FromMinutes(119)); state.CheckIdle(); Assert.False(state.IsLocked);
        clock.Advance(TimeSpan.FromMinutes(1)); state.CheckIdle(); Assert.True(state.IsLocked);
        state.Configure(settings with { EditorFontSize = 18 }); Assert.True(state.IsLocked);
        state.Password = "wrong"; await state.UnlockCommand.ExecuteAsync(null); Assert.True(state.IsLocked); Assert.Contains("密码错误", state.Error); Assert.Empty(state.Password);
        clock.Advance(TimeSpan.FromSeconds(2)); state.Password = "test-idle-password";
        await state.UnlockCommand.ExecuteAsync(null); Assert.False(state.IsLocked);
        state.Configure(new AppSettings()); clock.Advance(TimeSpan.FromDays(1)); state.CheckIdle(); Assert.False(state.IsLocked); Assert.False(state.Lock());
    }

    [AvaloniaFact]
    public async Task SettingsRequireTheCurrentPasswordAndConvertHoursToMinutes()
    {
        var passwords = new LockPasswordHasher();
        var original = new AppSettings { LockPasswordHash = passwords.Hash("old-password") };
        var vm = new SettingsViewModel(original, passwords, ["Consolas", "Courier New"])
        { TerminalFontFamily = "Consolas", TerminalFontSize = 17, EditorFontFamily = "Courier New", EditorFontSize = 19,
            ChangePassword = true, NewPassword = "new-password", ConfirmPassword = "new-password", AutoLock = true, Duration = 2, DurationUnit = 1 };
        AppSettings? saved = null; vm.Saved += settings => saved = settings;
        vm.CurrentPassword = "wrong"; await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(saved); Assert.Contains("当前锁定密码", vm.Error);
        vm.CurrentPassword = "old-password"; await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(saved); Assert.Equal(120, saved.AutoLockMinutes);
        Assert.True(passwords.Verify("new-password", saved.LockPasswordHash));
        Assert.Equal("Courier New", saved.EditorFontFamily); Assert.Equal(19, saved.EditorFontSize); Assert.Empty(vm.NewPassword);
        var removing = new SettingsViewModel(saved, passwords, vm.Fonts) { RemovePassword = true, CurrentPassword = "new-password" };
        removing.Saved += settings => saved = settings; await removing.SaveCommand.ExecuteAsync(null);
        Assert.Empty(saved.LockPasswordHash); Assert.False(saved.AutoLockEnabled);
    }

    [AvaloniaFact(Timeout = 20000)]
    public async Task CtrlLCoversEditorAndOwnerAndEnterUnlockRestoresUnsavedText()
    {
        var clock = new ManualTime(); var state = new AppLockService(new LockPasswordHasher(), clock);
        state.Configure(new AppSettings { LockPasswordHash = state.Passwords.Hash("test-ui-password"), EditorFontFamily = "Consolas", EditorFontSize = 18 });
        using var coordinator = new AppLockCoordinator(state);
        var originalBody = new TextBox { Text = "private connection workspace" };
        var owner = new Window { Content = originalBody, Width = 900, Height = 650 };
        var shortcutCalls = 0; owner.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.N, KeyModifiers.Control), Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => shortcutCalls++) });
        coordinator.Protect(owner); owner.Show();
        var editing = new DialogService(new TextCodec(), state, coordinator) { Owner = owner }.EditFileAsync(new("config.json", "{\"key\":1}", 9, DateTime.UtcNow), (document, text) => Task.FromResult(document with { Content = text }));
        var editor = owner.OwnedWindows.Single();
        try
        {
            await Task.Delay(50);
            var code = editor.GetVisualDescendants().OfType<CodeEditor>().Single();
            Assert.Equal(18, code.FontSize); Assert.Equal("Consolas", code.FontFamily.Name);
            code.Text = "{\"unsaved\":true}"; code.Focus();
            state.Configure(state.Settings with { AutoLockEnabled = true, AutoLockMinutes = 1 });
            clock.Advance(TimeSpan.FromSeconds(50));
            editor.MouseMove(code.TranslatePoint(new Point(100, 100), editor)!.Value);
            clock.Advance(TimeSpan.FromSeconds(59)); state.CheckIdle(); Assert.False(state.IsLocked);
            clock.Advance(TimeSpan.FromSeconds(1)); state.CheckIdle(); Assert.True(state.IsLocked);
            state.Password = "test-ui-password"; await state.UnlockCommand.ExecuteAsync(null);
            Assert.Equal("{\"unsaved\":true}", code.Text);
            editor.KeyPress(Key.L, RawInputModifiers.Control, PhysicalKey.None, null);
            Assert.True(state.IsLocked); Assert.False(originalBody.IsVisible); Assert.False(code.IsEffectivelyVisible);
            editor.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); owner.KeyPress(Key.N, RawInputModifiers.Control, PhysicalKey.None, null);
            Assert.True(editor.IsVisible); Assert.Equal(0, shortcutCalls);
            var lockView = editor.GetVisualDescendants().OfType<LockScreen>().Single();
            var password = lockView.FindControl<TextBox>("UnlockPassword")!;
            await Task.Delay(30); password.Focus(); editor.KeyTextInput("wrong"); editor.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            await Until(() => state.Error.Contains("密码错误")); Assert.True(state.IsLocked);
            clock.Advance(TimeSpan.FromSeconds(2)); password.Focus(); editor.KeyTextInput("test-ui-password"); editor.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            await Until(() => !state.IsLocked);
            Assert.True(originalBody.IsVisible); Assert.True(code.IsEffectivelyVisible); Assert.Equal("{\"unsaved\":true}", code.Text);
            state.Configure(state.Settings with { EditorFontFamily = "Courier New", EditorFontSize = 20 });
            Assert.Equal(20, code.FontSize); Assert.Equal("Courier New", code.FontFamily.Name);
            state.Lock(); await Task.Delay(40);
            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize(1050, 720), new Vector(96, 96));
                bitmap.Render(editor); bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, "application-locked.png"), PngBitmapEncoderOptions.Default);
            }
            var information = new DialogService(new TextCodec(), state, coordinator) { Owner = editor }.ShowInformationAsync("private modal", "private modal content");
            var modal = editor.OwnedWindows.Single();
            Assert.DoesNotContain(modal.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "private modal content" && text.IsEffectivelyVisible);
            Assert.True(modal.GetVisualDescendants().OfType<LockScreen>().Single().IsVisible);
            state.Password = "test-ui-password"; await state.UnlockCommand.ExecuteAsync(null);
            modal.Close(); await information;
            code.Text = "{\"key\":1}";
        }
        finally
        {
            if (state.IsLocked) { clock.Advance(TimeSpan.FromSeconds(2)); state.Password = "test-ui-password"; await state.UnlockCommand.ExecuteAsync(null); }
            var code = editor.GetVisualDescendants().OfType<CodeEditor>().Single(); code.Text = "{\"key\":1}";
            editor.Close(); await editing; owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task SettingsDialogShowsSeparateFontAndSecurityControls()
    {
        var owner = new Window(); owner.Show();
        var dialog = new DialogService(new TextCodec()) { Owner = owner };
        var result = dialog.SettingsAsync(new AppSettings());
        var window = owner.OwnedWindows.Single();
        try
        {
            Assert.NotNull(window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "TerminalFontSelector"));
            var vm = (SettingsViewModel)window.DataContext!;
            vm.TerminalFontFamily = "Consolas"; vm.EditorFontFamily = "Courier New";
            vm.ChangePassword = true; vm.NewPassword = vm.ConfirmPassword = "test-dialog-password";
            vm.AutoLock = true; vm.Duration = 1; vm.DurationUnit = 1;
            if (Environment.GetEnvironmentVariable("SHELLX_PREVIEW_PATH") is { Length: > 0 } preview)
            {
                await Task.Delay(40);
                using (var bitmap = new RenderTargetBitmap(new PixelSize(700, 620), new Vector(96, 96)))
                { bitmap.Render(window); bitmap.Save(Path.Combine(Path.GetDirectoryName(preview)!, "settings-fonts.png"), PngBitmapEncoderOptions.Default); }
                window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 1;
                await Task.Delay(40);
                using var security = new RenderTargetBitmap(new PixelSize(700, 620), new Vector(96, 96));
                security.Render(window); security.Save(Path.Combine(Path.GetDirectoryName(preview)!, "settings-security.png"), PngBitmapEncoderOptions.Default);
            }
            await vm.SaveCommand.ExecuteAsync(null);
            var saved = await result; Assert.NotNull(saved); Assert.Equal(60, saved.AutoLockMinutes);
            Assert.Equal("Consolas", saved.TerminalFontFamily); Assert.Equal("Courier New", saved.EditorFontFamily);
        }
        finally { if (window.IsVisible) window.Close(); owner.Close(); }
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition()) { Assert.True(DateTime.UtcNow < deadline, "等待锁定状态超时。"); await Task.Delay(20); }
    }
    private sealed class ManualTime : TimeProvider
    {
        private TimeSpan _elapsed;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _elapsed.Ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + _elapsed;
        public void Advance(TimeSpan duration) => _elapsed += duration;
    }
}
