using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ShellX.Core.Files;

namespace ShellX.Desktop.Controls;

/// <summary>按文件类别显示 24×24 图标；类别来自 <see cref="FileTypes.Category"/>，未知类别退回普通文件。</summary>
public sealed class FileIconView : PathIcon
{
    public static readonly StyledProperty<string> SymbolProperty =
        AvaloniaProperty.Register<FileIconView, string>(nameof(Symbol), "");
    public string Symbol { get => GetValue(SymbolProperty); set => SetValue(SymbolProperty, value); }

    private static readonly Dictionary<string, string> Shapes = new(StringComparer.Ordinal)
    {
        [FileTypes.Folder] = "M1.5,3.5 H6.5 L8.5,5.5 H14.5 V14.5 H1.5 Z",
        [FileTypes.Link] = "M2,12 L11,3 H7 V1 H15 V9 H13 V5 L4,14 Z",
        [FileTypes.Code] = "M5.4,3.2 L2,8 L5.4,12.8 L6.9,11.6 L4.4,8 L6.9,4.4 Z M10.6,3.2 L14,8 L10.6,12.8 L9.1,11.6 L11.6,8 L9.1,4.4 Z M8.4,2.8 L9.4,3 L7.6,13.2 L6.6,13 Z",
        [FileTypes.Image] = "M0.5,2.5 H15.5 V13.5 H0.5 Z M2.2,4.2 V11.8 H13.8 V4.2 Z M3,11.6 L6.8,5.6 L10.6,11.6 Z M8.8,11.6 L11.2,7.6 L13.6,11.6 Z M10.1,5.6 A1.5,1.5 0 1 1 13.1,5.6 A1.5,1.5 0 1 1 10.1,5.6 Z",
        [FileTypes.Video] = "M0.5,3 H15.5 V13 H0.5 Z M2.2,4.7 V11.3 H13.8 V4.7 Z M5.9,5.9 L10.9,8.6 L5.9,11.3 Z",
        [FileTypes.Audio] = "M2.3,11.4 A2,2 0 1 1 6.3,11.4 A2,2 0 1 1 2.3,11.4 Z M6.3,3.2 H7.3 V11.4 H6.3 Z M9.3,9.4 A2,2 0 1 1 13.3,9.4 A2,2 0 1 1 9.3,9.4 Z M13.3,1.2 H14.3 V9.4 H13.3 Z M6.3,3.2 L13.3,1.2 L14.3,1.2 L7.3,3.2 Z",
        [FileTypes.Archive] = "M1.5,2 H6.6 V4 H9.4 V2 H14.5 V6 H13 V14 H3 V6 H1.5 Z M3,6 V6.8 H13 V6 Z",
        [FileTypes.Text] = "M3.5,1.5 H10.5 L14.5,5.5 V14.5 H3.5 Z M5,6.5 V7.4 H12.5 V6.5 Z M5,9 V9.9 H12.5 V9 Z M5,11.5 V12.4 H10 V11.5 Z",
        [FileTypes.Sheet] = "M3.5,1.5 H10.5 L14.5,5.5 V14.5 H3.5 Z M9,6 V13 H9.8 V6 Z M4.5,9 V9.8 H13.5 V9 Z",
        [FileTypes.Config] = "M1.5,3.4 H14.5 V4.6 H1.5 Z M8,4 A2,2 0 1 1 12,4 A2,2 0 1 1 8,4 Z M1.5,7.4 H14.5 V8.6 H1.5 Z M3.5,8 A2,2 0 1 1 7.5,8 A2,2 0 1 1 3.5,8 Z M1.5,11.4 H14.5 V12.6 H1.5 Z M9,12 A2,2 0 1 1 13,12 A2,2 0 1 1 9,12 Z",
        [FileTypes.Program] = "M4.5,4.5 H11.5 V11.5 H4.5 Z M6.8,6.8 V9.2 H9.2 V6.8 Z M5.5,2.5 H6.5 V4.5 H5.5 Z M7.5,2.5 H8.5 V4.5 H7.5 Z M9.5,2.5 H10.5 V4.5 H9.5 Z M5.5,11.5 H6.5 V13.5 H5.5 Z M7.5,11.5 H8.5 V13.5 H7.5 Z M9.5,11.5 H10.5 V13.5 H9.5 Z M2.5,5.5 H4.5 V6.5 H2.5 Z M2.5,7.5 H4.5 V8.5 H2.5 Z M2.5,9.5 H4.5 V10.5 H2.5 Z M11.5,5.5 H13.5 V6.5 H11.5 Z M11.5,7.5 H13.5 V8.5 H11.5 Z M11.5,9.5 H13.5 V10.5 H11.5 Z",
        [FileTypes.Database] = "M2.5,4 V12 C2.5,13.4 5,14.6 8,14.6 C11,14.6 13.5,13.4 13.5,12 V4 A5.5,2.6 0 0 0 2.5,4 Z"
    };

    private const string PlainFile = "M3.5,1.5 H10.5 L14.5,5.5 V14.5 H3.5 Z";

    // 文档、PDF 和未知类别共用普通文件轮廓。
    public static string GeometryFor(string? category)
        => !string.IsNullOrEmpty(category) && Shapes.TryGetValue(category!, out var path) ? path : PlainFile;

    // 派生类默认用自己的类型当样式键，必须指回 PathIcon 才能套用它的 ControlTheme。
    protected override System.Type StyleKeyOverride => typeof(PathIcon);

    public FileIconView()
    {
        // StyleKey 指向 PathIcon 后类型选择器也按 StyleKey 匹配，用标记类定位本控件的配色。
        Classes.Add("file-icon");
        Width = 24;
        Height = 24;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        ApplySymbol();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SymbolProperty) ApplySymbol();
    }
    private void ApplySymbol() => Data = Geometry.Parse(GeometryFor(Symbol));
}