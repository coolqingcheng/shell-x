using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace ShellX.Desktop.Controls;

/// <summary>
/// 面包屑式路径栏：每一段都可以点击跳转，最后一段（或空白处）切换为路径编辑模式。
/// </summary>
public partial class PathBar : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<PathBar, string>(nameof(Text), string.Empty);
    public static readonly StyledProperty<bool> WindowsStyleProperty =
        AvaloniaProperty.Register<PathBar, bool>(nameof(WindowsStyle), false);
    public static readonly StyledProperty<string> PlaceholderTextProperty =
        AvaloniaProperty.Register<PathBar, string>(nameof(PlaceholderText), string.Empty);

    /// <summary>用户选择路径或确认编辑后的完整路径。</summary>
    public event EventHandler<string>? NavigateRequested;
    /// <summary>编辑被取消。</summary>
    public event EventHandler? EditCancelled;

    private readonly ObservableCollection<PathSegment> _segments = [];
    private bool _ready;
    private bool _editing;
    private string _editOriginal = string.Empty;

    public PathBar()
    {
        InitializeComponent();
        SegmentsHost.ItemsSource = _segments;
        EditBox.PlaceholderText = PlaceholderText;
        _ready = true;
        Rebuild();
    }

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool WindowsStyle { get => GetValue(WindowsStyleProperty); set => SetValue(WindowsStyleProperty, value); }
    public string PlaceholderText { get => GetValue(PlaceholderTextProperty); set => SetValue(PlaceholderTextProperty, value); }
    public bool IsEditing => _editing;
    /// <summary>路径编辑框，供测试与外部调用。</summary>
    public TextBox EditInput => EditBox;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (!_ready) return;
        if (change.Property == TextProperty || change.Property == WindowsStyleProperty) Rebuild();
        else if (change.Property == PlaceholderTextProperty) EditBox.PlaceholderText = PlaceholderText;
    }

    private void Rebuild()
    {
        _segments.Clear();
        foreach (var segment in VisibleSegments(PathSegmentBuilder.Split(Text, WindowsStyle))) _segments.Add(segment);
        if (!_editing) return;
        // 路径在编辑期间变化：用户还没改动输入内容时保持编辑并同步文本，否则放弃编辑。
        if (IsTextUntouched) EditBox.Text = Text;
        else EndEdit(false);
    }

    private bool IsTextUntouched => (EditBox.Text ?? string.Empty) == _editOriginal;

    // 路径段太多时保留根目录和最后几段，中间用省略号代替，点击省略号回到被折叠的上一层。
    private static IReadOnlyList<PathSegment> VisibleSegments(IReadOnlyList<PathSegment> all)
    {
        const int maxVisible = 5;
        if (all.Count <= maxVisible) return all;
        var result = new List<PathSegment> { all[0] };
        result.Add(new PathSegment { Name = "…", Path = all[all.Count - 4].Path, ShowSeparator = true, IsEllipsis = true });
        for (var i = all.Count - 3; i < all.Count; i++)
            result.Add(new PathSegment { Name = all[i].Name, Path = all[i].Path, ShowSeparator = true, IsLast = all[i].IsLast });
        return result;
    }

    private void SegmentClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PathSegment segment }) return;
        if (segment.IsLast) BeginEdit();
        else NavigateRequested?.Invoke(this, segment.Path);
    }

    private void EmptyAreaPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_editing) return;
        BeginEdit();
        e.Handled = true;
    }

    /// <summary>切换到路径编辑模式。</summary>
    public void BeginEdit()
    {
        if (_editing) return;
        _editing = true;
        _editOriginal = Text ?? string.Empty;
        EditBox.Text = Text;
        SegmentsHost.IsVisible = false;
        EditBox.IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_editing || EditBox.IsFocused) return;
            EditBox.Focus();
            EditBox.SelectAll();
        });
    }

    private void EndEdit(bool commit)
    {
        if (!_editing) return;
        _editing = false;
        var text = (EditBox.Text ?? string.Empty).Trim();
        EditBox.IsVisible = false;
        SegmentsHost.IsVisible = true;
        if (commit && text.Length > 0)
        {
            if (text != Text) NavigateRequested?.Invoke(this, text);
        }
        else EditCancelled?.Invoke(this, EventArgs.Empty);
    }

    private void EditKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; EndEdit(true); }
        else if (e.Key == Key.Escape) { e.Handled = true; EndEdit(false); }
    }

    private void EditLostFocus(object? sender, RoutedEventArgs e)
    {
        if (!_editing) return;
        // 改过内容后离开编辑框就提交路径；没有改动时保持编辑状态，避免焦点被其它控件抢走就丢失编辑模式。
        if (!IsTextUntouched) EndEdit(true);
    }
}
