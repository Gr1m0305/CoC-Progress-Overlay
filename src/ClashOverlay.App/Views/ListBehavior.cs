using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ClashOverlay.App.Views;

/// <summary>
/// Keeps the selected row of a list in view, also when the tab is rebuilt after a refresh or a hand edit.
/// </summary>
public static class ListBehavior
{
    public static readonly DependencyProperty ScrollSelectedIntoViewProperty =
        DependencyProperty.RegisterAttached("ScrollSelectedIntoView", typeof(bool), typeof(ListBehavior), new PropertyMetadata(false, OnChanged));

    public static bool GetScrollSelectedIntoView(DependencyObject d) => (bool)d.GetValue(ScrollSelectedIntoViewProperty);

    public static void SetScrollSelectedIntoView(DependencyObject d, bool value) => d.SetValue(ScrollSelectedIntoViewProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list || e.NewValue is not true)
        {
            return;
        }

        list.Loaded += (_, _) => Scroll(list);
        list.SelectionChanged += (_, _) => Scroll(list);
    }

    private static void Scroll(ListBox list)
    {
        if (list.SelectedItem is { } item)
        {
            list.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => list.ScrollIntoView(item));
        }
    }
}
