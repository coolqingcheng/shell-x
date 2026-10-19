using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ShellX.Core.Security;

namespace ShellX.Desktop.Features.Security;

public sealed class AppLockCoordinator : IDisposable
{
    private readonly AppLockService _state;
    private readonly ISystemSessionMonitor? _systemSession;
    private bool _disposed;
    private readonly Dictionary<Window, Protection> _windows = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public bool SupportsSystemSessionMonitoring => _systemSession?.IsSupported ?? false;
    public AppLockCoordinator(AppLockService state, ISystemSessionMonitor? systemSession = null)
    {
        _state = state; _state.PropertyChanged += StateChanged;
        _systemSession = systemSession;
        if (_systemSession != null)
        {
            _systemSession.StateChanged += SystemSessionChanged;
            _systemSession.Start();
            if (_systemSession.CurrentState is { } current) SystemSessionChanged(current);
        }
        _timer.Tick += (_, _) => state.CheckIdle(); _timer.Start();
    }
    private void SystemSessionChanged(SystemSessionLockState state)
    {
        void Apply() { if (!_disposed) _state.UpdateSystemSession(state); }
        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
    }
    public void Protect(Window window)
    {
        if (_windows.ContainsKey(window)) return;
        if (window.Content is not Control content) throw new InvalidOperationException("窗口内容必须为控件。");
        window.Content = null;
        var screen = new LockScreen(_state);
        var wrapper = new Grid(); wrapper.Children.Add(content); wrapper.Children.Add(screen);
        var protection = new Protection(window, content, screen);
        _windows.Add(window, protection); window.Content = wrapper;
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => KeyDown(protection, e), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerPressedEvent, (_, e) => Activity(e), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerMovedEvent, (_, e) => Activity(e), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) => Activity(e), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.TextInputEvent, (_, e) => Activity(e), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.Activated += (_, _) => { if (_state.IsLocked) SelectInput(protection); };
        window.Closing += (_, e) => { if (_state.IsLocked && _state.IsReady) e.Cancel = true; };
        window.Closed += (_, _) => _windows.Remove(window);
        Apply(protection);
        if (_state.IsLocked) SelectInput(protection);
    }
    private void StateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppLockService.IsLocked))
        {
            foreach (var protection in _windows.Values) Apply(protection);
            if (_state.IsLocked && (_windows.Values.LastOrDefault(p => p.Window.IsActive) ?? _windows.Values.LastOrDefault(p => p.Window.IsVisible)) is { } active) SelectInput(active);
        }
        else if (e.PropertyName is nameof(AppLockService.IsReady) or nameof(AppLockService.IsVerifying) && _state.IsLocked && _state.IsReady && !_state.IsVerifying &&
                 (_windows.Values.LastOrDefault(p => p.Window.IsActive) ?? _windows.Values.LastOrDefault(p => p.Window.IsVisible)) is { } active) SelectInput(active);
    }
    private void Apply(Protection protection)
    {
        if (_state.IsLocked)
        {
            if (!protection.Covered)
            {
                protection.Focus = protection.Window.FocusManager?.GetFocusedElement();
                protection.WasVisible = protection.Content.IsVisible; protection.WasEnabled = protection.Content.IsEnabled;
                protection.Bindings = protection.Window.KeyBindings.ToArray(); protection.Window.KeyBindings.Clear();
                foreach (var control in protection.Content.GetVisualDescendants().OfType<Control>())
                {
                    control.ContextMenu?.Close();
                    ToolTip.SetIsOpen(control, false);
                    if (control is Button button) button.Flyout?.Hide();
                    if (control is ComboBox combo) combo.IsDropDownOpen = false;
                }
                protection.Content.SetCurrentValue(Visual.IsVisibleProperty, false);
                protection.Content.SetCurrentValue(InputElement.IsEnabledProperty, false);
                protection.Covered = true;
            }
            protection.Screen.IsVisible = true;
        }
        else
        {
            protection.Screen.IsVisible = false;
            if (protection.Covered)
            {
                protection.Content.SetCurrentValue(Visual.IsVisibleProperty, protection.WasVisible);
                protection.Content.SetCurrentValue(InputElement.IsEnabledProperty, protection.WasEnabled);
                foreach (var binding in protection.Bindings) protection.Window.KeyBindings.Add(binding);
                protection.Bindings = []; protection.Covered = false;
                if (protection.Window.IsActive && protection.Focus is Control { IsEffectivelyVisible: true } focused) focused.Focus();
                protection.Focus = null;
            }
        }
    }
    private void SelectInput(Protection selected)
    {
        foreach (var protection in _windows.Values) protection.Screen.SetInputVisible(ReferenceEquals(protection, selected));
        Dispatcher.UIThread.Post(selected.Screen.FocusPassword, DispatcherPriority.Input);
    }
    private void KeyDown(Protection protection, KeyEventArgs input)
    {
        Activity(input);
        if (input.Handled) return;
        if (input.Key == Key.L && input.KeyModifiers.HasFlag(KeyModifiers.Control) && _state.CanLock)
        { input.Handled = true; _state.Lock(); SelectInput(protection); return; }
        if (_state.IsLocked && input.Key is Key.Escape or Key.F4) input.Handled = true;
    }
    private void Activity(RoutedEventArgs input)
    {
        var locked = _state.IsLocked;
        _state.CheckIdle();
        if (!locked && _state.IsLocked) input.Handled = true;
        else _state.RecordActivity();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop(); _state.PropertyChanged -= StateChanged;
        if (_systemSession != null) _systemSession.StateChanged -= SystemSessionChanged;
        _windows.Clear();
    }
    private sealed class Protection(Window window, Control content, LockScreen screen)
    {
        public Window Window { get; } = window;
        public Control Content { get; } = content;
        public LockScreen Screen { get; } = screen;
        public bool Covered, WasVisible, WasEnabled;
        public KeyBinding[] Bindings = [];
        public IInputElement? Focus;
    }
}
