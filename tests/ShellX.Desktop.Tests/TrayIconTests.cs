using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ShellX.Core.Security;
using ShellX.Core.Settings;
using ShellX.Desktop.Features.Connections;
using ShellX.Desktop.Features.Security;
using ShellX.Desktop.Features.Settings;
using ShellX.Desktop.Features.Shell;
using ShellX.Desktop.Services;
using ShellX.Infrastructure.Security;
using Xunit;

namespace ShellX.Desktop.Tests;

public class TrayIconTests
{
    [Fact]
    public void CloseToTrayIsEnabledByDefault() => Assert.True(new AppSettings().CloseToTray);

    [AvaloniaFact]
    public async Task SettingsViewModelSavesTheCloseToTraySwitch()
    {
        var vm = new SettingsViewModel(new AppSettings { CloseToTray = false }, new LockPasswordHasher(), ["Consolas"]);
        Assert.False(vm.CloseToTray);
        AppSettings? saved = null; vm.Saved += settings => saved = settings;
        vm.CloseToTray = true;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(saved); Assert.True(saved.CloseToTray);
    }

    [AvaloniaFact]
    public void UserCloseHidesTheWorkspaceInsteadOfClosingIt()
    {
        var tray = CreateTray(closeToTray: true);
        var window = new MainWindow(tray);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        SimulateUserClose(window);
        Assert.False(closed);
        Assert.False(window.IsVisible);
        window.Show();
        Assert.True(window.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void UserCloseClosesTheConnectionManagerWhenTheTrayIsDisabled()
    {
        var tray = CreateTray(closeToTray: false);
        var window = new ConnectionManagerWindow(null, null, tray);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        SimulateUserClose(window);
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void TrayIconInitializesWithoutThrowing()
    {
        var tray = CreateTray(closeToTray: true);
        var restored = false;
        tray.Initialize(() => restored = true, () => { }, () => { });
        Assert.False(restored);
    }

    private static TrayIconService CreateTray(bool closeToTray)
    {
        var state = new AppLockService(new LockPasswordHasher());
        state.Configure(new AppSettings { CloseToTray = closeToTray });
        return new TrayIconService(state);
    }

    /// <summary>模拟用户点击标题栏关闭按钮（平台发起的关闭，IsProgrammatic 为 false）。</summary>
    private static void SimulateUserClose(Window window)
    {
        var close = typeof(Window).GetMethod("CloseCore", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(close);
        close!.Invoke(window, [WindowCloseReason.WindowClosing, false, false]);
    }
}
