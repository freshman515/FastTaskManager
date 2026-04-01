using System.Windows.Controls;
using System.Windows.Input;
using FastTaskManager.App.ViewModels;

namespace FastTaskManager.App.Views;

public partial class ServicesView : UserControl
{
    public ServicesView()
    {
        InitializeComponent();
    }

    private async void UserControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not ServicesViewModel viewModel)
            return;

        await viewModel.EnsureLoadedAsync();
    }

    private void ListViewItem_SelectOnRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem item)
            item.IsSelected = true;
    }
}
