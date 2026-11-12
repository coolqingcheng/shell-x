using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace ShellX.Desktop.Controls;

/// <summary>
/// 统一的页签外观：终端连接、文件管理器、文本编辑器共用这一套控件，
/// 三处的强调线、状态点、标题与关闭按钮因此始终保持一致。
/// 配色细节由 <see cref="ConnectionAccentBorder"/> 负责，这里只负责结构与状态点。
/// </summary>
public partial class ShellTabItemView : UserControl
{
    /// <summary>未保存提示点的颜色，与 VS Code 的脏标记一致。</summary>
    private const string DirtyColor = "#D7BA7D";

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<ShellTabItemView, string>(nameof(Title), string.Empty);
    public static readonly StyledProperty<string> StatusColorProperty =
        AvaloniaProperty.Register<ShellTabItemView, string>(nameof(StatusColor), string.Empty);
    public static readonly StyledProperty<string> AccentColorProperty =
        AvaloniaProperty.Register<ShellTabItemView, string>(nameof(AccentColor), string.Empty);
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<ShellTabItemView, bool>(nameof(IsActive));
    public static readonly StyledProperty<bool> IsDirtyProperty =
        AvaloniaProperty.Register<ShellTabItemView, bool>(nameof(IsDirty));
    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<ShellTabItemView, ICommand?>(nameof(CloseCommand));
    public static readonly StyledProperty<string> CloseToolTipProperty =
        AvaloniaProperty.Register<ShellTabItemView, string>(nameof(CloseToolTip), "关闭标签");
    public static readonly StyledProperty<string> TitleToolTipProperty =
        AvaloniaProperty.Register<ShellTabItemView, string>(nameof(TitleToolTip), string.Empty);
    public static readonly StyledProperty<double> TabMinWidthProperty =
        AvaloniaProperty.Register<ShellTabItemView, double>(nameof(TabMinWidth), 130);
    public static readonly StyledProperty<double> TitleMaxWidthProperty =
        AvaloniaProperty.Register<ShellTabItemView, double>(nameof(TitleMaxWidth), 180);

    /// <summary>页签标题。</summary>
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    /// <summary>状态点颜色（连接状态），为空时不显示状态点。</summary>
    public string StatusColor { get => GetValue(StatusColorProperty); set => SetValue(StatusColorProperty, value); }
    /// <summary>连接强调色，为空时使用主题默认强调色。</summary>
    public string AccentColor { get => GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
    /// <summary>是否为当前选中的页签。</summary>
    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    /// <summary>是否有未保存的修改。</summary>
    public bool IsDirty { get => GetValue(IsDirtyProperty); set => SetValue(IsDirtyProperty, value); }
    /// <summary>关闭按钮执行的命令。</summary>
    public ICommand? CloseCommand { get => GetValue(CloseCommandProperty); set => SetValue(CloseCommandProperty, value); }
    /// <summary>关闭按钮的提示文字。</summary>
    public string CloseToolTip { get => GetValue(CloseToolTipProperty); set => SetValue(CloseToolTipProperty, value); }
    /// <summary>标题的悬浮提示，为空时不显示。</summary>
    public string TitleToolTip { get => GetValue(TitleToolTipProperty); set => SetValue(TitleToolTipProperty, value); }
    /// <summary>页签最小宽度。</summary>
    public double TabMinWidth { get => GetValue(TabMinWidthProperty); set => SetValue(TabMinWidthProperty, value); }
    /// <summary>标题最大宽度，超出部分省略。</summary>
    public double TitleMaxWidth { get => GetValue(TitleMaxWidthProperty); set => SetValue(TitleMaxWidthProperty, value); }

    public ShellTabItemView()
    {
        InitializeComponent();
        Apply();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty || change.Property == TitleMaxWidthProperty || change.Property == StatusColorProperty
            || change.Property == IsDirtyProperty || change.Property == AccentColorProperty || change.Property == IsActiveProperty
            || change.Property == CloseCommandProperty || change.Property == CloseToolTipProperty || change.Property == TabMinWidthProperty
            || change.Property == TitleToolTipProperty)
            Apply();
    }

    /// <summary>把外部属性同步到模板内的各个元素。</summary>
    private void Apply()
    {
        TabBackground.AccentColor = AccentColor;
        TabBackground.IsActive = IsActive;
        TabBackground.MinWidth = TabMinWidth;
        // 圆角页签：选中的只圆上方两角，跟下方内容区连成一片；未选中的四角全圆，像一枚小卡片。
        TabBackground.CornerRadius = IsActive ? new CornerRadius(6, 6, 0, 0) : new CornerRadius(6);
        AccentLine.AccentColor = AccentColor;
        AccentLine.IsActive = IsActive;
        AccentLine.CornerRadius = new CornerRadius(3, 3, 0, 0);
        TitleText.Text = Title;
        TitleText.MaxWidth = TitleMaxWidth;
        ToolTip.SetTip(TitleText, string.IsNullOrWhiteSpace(TitleToolTip) ? null : TitleToolTip);
        CloseButton.Command = CloseCommand;
        ToolTip.SetTip(CloseButton, CloseToolTip);
        UpdateStatusDot();
    }

    /// <summary>状态点优先显示连接状态，没有状态时用未保存标记代替。</summary>
    private void UpdateStatusDot()
    {
        var color = !string.IsNullOrWhiteSpace(StatusColor) ? StatusColor : IsDirty ? DirtyColor : null;
        if (color is null || !Color.TryParse(color, out var parsed))
        {
            StatusDot.IsVisible = false;
            StatusDot.Fill = null;
            return;
        }
        StatusDot.IsVisible = true;
        StatusDot.Fill = new SolidColorBrush(parsed);
    }
}
