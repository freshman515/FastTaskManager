using System.Windows.Controls;
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
}
