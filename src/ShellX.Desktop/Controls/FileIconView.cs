using Avalonia;
using ShellX.Core.Files;

namespace ShellX.Desktop.Controls;

/// <summary>文件列表图标：按文件类别显示线条图标，24×24 居中，未知类别退回普通文件。</summary>
public sealed class FileIconView : StrokeIcon
{
    private static readonly Dictionary<string, string> Shapes = new(StringComparer.Ordinal)
    {
        [FileTypes.Folder] = "M3,6.5 H9.5 L11.8,8.8 H21 V19 H3 Z",
        [FileTypes.Link] = "M6,18 L17.5,6.5 M11.5,6.5 H17.5 V12.5",
        [FileTypes.Code] = "M8.5,7 L3.5,12 L8.5,17 M15.5,7 L20.5,12 L15.5,17 M13.5,5.5 L10.5,18.5",
        [FileTypes.Image] = "M2.5,4.5 H21.5 V19.5 H2.5 Z M5.5,17 L10,11 L14,17 M13,17 L16,13 L19,17 M18.5,8 m-2,0 a2,2 0 1,0 4,0 a2,2 0 1,0 -4,0",
        [FileTypes.Video] = "M2.5,5.5 H21.5 V18.5 H2.5 Z M10,9 L15.5,12 L10,15 Z",
        [FileTypes.Audio] = "M3.7,17.5 a2.8,2.8 0 1,0 5.6,0 a2.8,2.8 0 1,0 -5.6,0 M9.3,17.5 V5.5 M13.2,15.5 a2.8,2.8 0 1,0 5.6,0 a2.8,2.8 0 1,0 -5.6,0 M18.8,15.5 V3.5 M9.3,5.5 L18.8,3.5",
        [FileTypes.Archive] = "M3.5,6.5 H20.5 V19.5 H3.5 Z M3.5,10.5 H20.5 M12,6.5 V10.5",
        [FileTypes.Text] = "M5,2.5 H14 L19,7.5 V21.5 H5 Z M14,2.5 V7.5 H19 M8,11 H16 M8,14.5 H16 M8,18 H13",
        [FileTypes.Sheet] = "M5,2.5 H14 L19,7.5 V21.5 H5 Z M14,2.5 V7.5 H19 M5,11 H19 M5,15.5 H19 M12.5,11 V19.5",
        [FileTypes.Config] = "M4,7 H20 M4,12 H20 M4,17 H20 M15,7 m-2.2,0 a2.2,2.2 0 1,0 4.4,0 a2.2,2.2 0 1,0 -4.4,0 M9,12 m-2.2,0 a2.2,2.2 0 1,0 4.4,0 a2.2,2.2 0 1,0 -4.4,0 M16.5,17 m-2.2,0 a2.2,2.2 0 1,0 4.4,0 a2.2,2.2 0 1,0 -4.4,0",
        [FileTypes.Program] = "M6.5,6.5 H17.5 V17.5 H6.5 Z M9.5,6.5 V3 M12,6.5 V3 M14.5,6.5 V3 M9.5,17.5 V21 M12,17.5 V21 M14.5,17.5 V21 M6.5,9.5 H3 M6.5,12 H3 M6.5,14.5 H3 M17.5,9.5 H21 M17.5,12 H21 M17.5,14.5 H21",
        [FileTypes.Database] = "M4.5,7 C4.5,5.1 7.8,3.5 12,3.5 C16.2,3.5 19.5,5.1 19.5,7 V17 C19.5,18.9 16.2,20.5 12,20.5 C7.8,20.5 4.5,18.9 4.5,17 Z M4.5,7 C4.5,8.9 7.8,10.5 12,10.5 C16.2,10.5 19.5,8.9 19.5,7 M4.5,13 C4.5,14.9 7.8,16.5 12,16.5 C16.2,16.5 19.5,14.9 19.5,13"
    };

    // 文档、PDF 和未知类别共用普通文件轮廓。
    private const string PlainFile = "M5,2.5 H14 L19,7.5 V21.5 H5 Z M14,2.5 V7.5 H19";

    public static string GeometryFor(string? category)
        => !string.IsNullOrEmpty(category) && Shapes.TryGetValue(category!, out var path) ? path : PlainFile;

    protected override string? ResolvePath(string? symbol) => GeometryFor(symbol);

    public FileIconView()
    {
        Width = 20;
        Height = 20;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }
}