using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClashOverlay.App.ViewModels;

namespace ClashOverlay.App.Views;

internal partial class OverlayView : UserControl
{
    public OverlayView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged old)
            {
                old.PropertyChanged -= OnVmChanged;
            }

            if (e.NewValue is OverlayViewModel vm)
            {
                vm.PropertyChanged += OnVmChanged;
                ApplyAccent(vm);
            }
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.Village) && sender is OverlayViewModel vm)
        {
            ApplyAccent(vm);
        }
    }

    /// <summary>Gold for the Main Village, blue for the Builder Base: every DynamicResource AccentBrush follows.</summary>
    private void ApplyAccent(OverlayViewModel vm)
    {
        Resources["AccentBrush"] = vm.AccentBrush;
        Resources["AccentSoftBrush"] = vm.AccentSoftBrush;
    }

    private void OnOutsideAccountMenu(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is OverlayViewModel vm)
        {
            vm.IsAccountMenuOpen = false;
        }
    }
}
