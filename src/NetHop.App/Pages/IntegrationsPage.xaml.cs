using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace NetHop.App.Pages;

public sealed partial class IntegrationsPage : Page
{
    public IntegrationsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) =>
        NoServiceInfo.IsOpen = App.Current?.Connection.IsLocal == true;

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e) => App.Current?.ShowSettings();
}
