using System.Windows;
using ClashOverlay.App.ViewModels;

namespace ClashOverlay.App.Windows;

/// <summary>"Import village export…" from the tray or the Accounts page.</summary>
internal partial class ImportWindow
{
    public ImportWindow(ImportViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    public ImportViewModel ViewModel => (ImportViewModel)DataContext;

    private void OnDone(object sender, RoutedEventArgs e) => Close();
}
