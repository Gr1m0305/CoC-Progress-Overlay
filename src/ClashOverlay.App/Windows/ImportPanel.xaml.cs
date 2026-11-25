using System.Windows;
using System.Windows.Media;
using ClashOverlay.App.ViewModels;

namespace ClashOverlay.App.Windows;

/// <summary>Paste, drop or choose the village export (DataContext: <see cref="ImportViewModel"/>).</summary>
internal partial class ImportPanel
{
    private static readonly Brush Normal = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Hover = new SolidColorBrush(Color.FromArgb(0x33, 0xF5, 0xC5, 0x42));

    public ImportPanel()
    {
        InitializeComponent();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var ok = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DropZone.Background = ok ? Hover : Normal;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => DropZone.Background = Normal;

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropZone.Background = Normal;
        if (DataContext is not ImportViewModel vm)
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            vm.ImportFile(files[0]);
        }
        else if (e.Data.GetData(DataFormats.UnicodeText) is string text)
        {
            vm.ImportText(text);
        }
    }
}
