namespace FastTaskManager.App.Models;

using System.Windows.Media;

public sealed class StartupAppItem
{
    public required string Name { get; init; }
    public required string Publisher { get; init; }
    public required string StartupImpactText { get; init; }
    public required string SourceText { get; init; }
    public required string CommandText { get; init; }
    public required string ApprovalRegistryPath { get; init; }
    public required string ApprovalValueName { get; init; }
    public required string LocationPath { get; init; }
    public bool IsCurrentUser { get; init; }
    public bool IsEnabled { get; init; }
    public ImageSource? Icon { get; set; }

    public string StatusText => IsEnabled ? "已启用" : "已禁用";
    public string ActionText => IsEnabled ? "禁用" : "启用";
    public bool CanOpenLocation => !string.IsNullOrWhiteSpace(LocationPath);
}
