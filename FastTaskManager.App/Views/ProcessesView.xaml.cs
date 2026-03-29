using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using FastTaskManager.App.Models;
using FastTaskManager.App.ViewModels;
using System.Linq;

namespace FastTaskManager.App.Views;

public partial class ProcessesView : UserControl
{
    private const double PathColumnMinWidth = 320;

    public ProcessesView()
    {
        InitializeComponent();
        Loaded += (_, _) => AdjustPathColumnWidth();
    }

    private void ListViewItem_SelectOnRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem item)
            item.IsSelected = true;
    }

    private void ListViewItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ProcessesViewModel viewModel) return;
        if (sender is ListViewItem { DataContext: ProcessDisplayItem { IsGroup: true } group })
            viewModel.ToggleGroupExpand(group);
    }

    private void GroupArrowButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProcessesViewModel viewModel) return;
        if (sender is not FrameworkElement { DataContext: ProcessDisplayItem { IsGroup: true } group }) return;

        viewModel.ToggleGroupExpand(group);
        e.Handled = true;
    }

    private void ProcessesListView_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        AdjustPathColumnWidth();
    }

    private void AdjustPathColumnWidth()
    {
        var listView = FindName("ProcessesListView") as ListView;
        if (listView?.View is not GridView gridView || gridView.Columns.Count == 0)
            return;

        var pathColumn = gridView.Columns.Last();
        var otherWidth = gridView.Columns
            .Where(column => !ReferenceEquals(column, pathColumn))
            .Sum(column => column.ActualWidth > 0 ? column.ActualWidth : column.Width);

        const double chromeWidth = 36;
        var available = listView.ActualWidth - otherWidth - chromeWidth;
        pathColumn.Width = Math.Max(PathColumnMinWidth, available);
    }
}
