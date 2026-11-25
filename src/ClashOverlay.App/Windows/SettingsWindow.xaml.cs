using System.Windows;
using ClashOverlay.App.ViewModels;

namespace ClashOverlay.App.Windows;

internal partial class SettingsWindow
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Closing += (_, _) => vm.Flush();
        Closed += (_, _) => vm.Dispose();
    }

    public SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    private void OnDone(object sender, RoutedEventArgs e) => Close();
}
