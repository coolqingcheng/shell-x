using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ShellX.Desktop.Controls;

public sealed class FileIconView : PathIcon
{
    public static readonly StyledProperty<string> SymbolProperty =
        AvaloniaProperty.Register<FileIconView, string>(nameof(Symbol), "");
    public string Symbol { get => GetValue(SymbolProperty); set => SetValue(SymbolProperty, value); }
    public FileIconView()
    {
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
    private void ApplySymbol()
    {
        Data = Geometry.Parse(Symbol switch
        {
            "▸" => "M1.5,3.5 H6.5 L8.5,5.5 H14.5 V14.5 H1.5 Z",
            "↗" => "M2,12 L11,3 H7 V1 H15 V9 H13 V5 L4,14 Z",
            _ => "M3.5,1.5 H10.5 L14.5,5.5 V16.5 H3.5 Z"
        });
    }
}