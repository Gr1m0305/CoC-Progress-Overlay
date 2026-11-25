using ClashOverlay.App.ViewModels;

namespace ClashOverlay.App.Windows;

/// <summary>The four-step first-run guide.</summary>
internal partial class SetupWindow
{
    public SetupWindow(SetupViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.Finished += (_, _) => Close();
    }

    public SetupViewModel ViewModel => (SetupViewModel)DataContext;
}
