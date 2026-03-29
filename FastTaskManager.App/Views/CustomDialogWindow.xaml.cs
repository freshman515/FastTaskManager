using System.Windows;
using System.Windows.Media;

namespace FastTaskManager.App.Views;

public enum CustomDialogVariant
{
    Warning,
    Error
}

public partial class CustomDialogWindow : Window
{
    public string DialogTitle { get; }
    public string DialogMessage { get; }
    public string PrimaryButtonText { get; }
    public string? SecondaryButtonText { get; }
    public bool HasSecondaryButton => !string.IsNullOrWhiteSpace(SecondaryButtonText);
    public string BadgeText { get; }
    public Brush BadgeBackground { get; }
    public Brush BadgeBorderBrush { get; }
    public Brush BadgeForeground { get; }
    public string IconGlyph { get; }
    public Brush IconBrush { get; }
    public Brush PrimaryButtonBackground { get; }

    public CustomDialogWindow(
        string title,
        string message,
        CustomDialogVariant variant,
        string primaryButtonText,
        string? secondaryButtonText,
        bool isPrimaryDanger)
    {
        DialogTitle = title;
        DialogMessage = message;
        PrimaryButtonText = primaryButtonText;
        SecondaryButtonText = secondaryButtonText;

        BadgeText = variant == CustomDialogVariant.Error ? "错误" : "确认";
        BadgeBackground = CreateBrush(variant == CustomDialogVariant.Error ? "#241217" : "#0D1D24");
        BadgeBorderBrush = CreateBrush(variant == CustomDialogVariant.Error ? "#4A1F29" : "#1A3C44");
        BadgeForeground = CreateBrush(variant == CustomDialogVariant.Error ? "#FF8A97" : "#00C2D8");
        IconGlyph = variant == CustomDialogVariant.Error ? "\uEA39" : "\uE7BA";
        IconBrush = BadgeForeground;
        PrimaryButtonBackground = CreateBrush(isPrimaryDanger ? "#FF4C5B" : "#00C2D8");

        InitializeComponent();
        DataContext = this;
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static SolidColorBrush CreateBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
}
