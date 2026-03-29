using System.Windows.Media;
using FastTaskManager.App.Infrastructure;

namespace FastTaskManager.App.Models;

public sealed class PerformanceMetricItem : ObservableObject
{
    private string _subtitle = string.Empty;
    private string _valueText = "0%";
    private string _detailText = string.Empty;
    private string _primaryLabel = string.Empty;
    private string _primaryValue = string.Empty;
    private string _secondaryLabel = string.Empty;
    private string _secondaryValue = string.Empty;
    private string _tertiaryLabel = string.Empty;
    private string _tertiaryValue = string.Empty;
    private PointCollection _chartPoints = [];
    private PointCollection _areaPoints = [];

    public PerformanceMetricItem(string key, string title, Brush accentBrush, Brush fillBrush)
    {
        Key = key;
        Title = title;
        AccentBrush = accentBrush;
        FillBrush = fillBrush;
    }

    public string Key { get; }
    public string Title { get; }
    public Brush AccentBrush { get; }
    public Brush FillBrush { get; }

    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    public string ValueText
    {
        get => _valueText;
        private set => SetProperty(ref _valueText, value);
    }

    public string DetailText
    {
        get => _detailText;
        private set => SetProperty(ref _detailText, value);
    }

    public string PrimaryLabel
    {
        get => _primaryLabel;
        private set => SetProperty(ref _primaryLabel, value);
    }

    public string PrimaryValue
    {
        get => _primaryValue;
        private set => SetProperty(ref _primaryValue, value);
    }

    public string SecondaryLabel
    {
        get => _secondaryLabel;
        private set => SetProperty(ref _secondaryLabel, value);
    }

    public string SecondaryValue
    {
        get => _secondaryValue;
        private set => SetProperty(ref _secondaryValue, value);
    }

    public string TertiaryLabel
    {
        get => _tertiaryLabel;
        private set => SetProperty(ref _tertiaryLabel, value);
    }

    public string TertiaryValue
    {
        get => _tertiaryValue;
        private set => SetProperty(ref _tertiaryValue, value);
    }

    public PointCollection ChartPoints
    {
        get => _chartPoints;
        private set => SetProperty(ref _chartPoints, value);
    }

    public PointCollection AreaPoints
    {
        get => _areaPoints;
        private set => SetProperty(ref _areaPoints, value);
    }

    public void Update(
        string subtitle,
        string valueText,
        string detailText,
        string primaryLabel,
        string primaryValue,
        string secondaryLabel,
        string secondaryValue,
        string tertiaryLabel,
        string tertiaryValue,
        PointCollection chartPoints,
        PointCollection areaPoints)
    {
        Subtitle = subtitle;
        ValueText = valueText;
        DetailText = detailText;
        PrimaryLabel = primaryLabel;
        PrimaryValue = primaryValue;
        SecondaryLabel = secondaryLabel;
        SecondaryValue = secondaryValue;
        TertiaryLabel = tertiaryLabel;
        TertiaryValue = tertiaryValue;
        ChartPoints = chartPoints;
        AreaPoints = areaPoints;
    }
}
