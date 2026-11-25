using System.Windows.Controls;
using System.Windows.Input;
using ClashOverlay.Core.Settings;

namespace ClashOverlay.App.Views;

/// <summary>
/// A box that records the key combination pressed in it ("Ctrl+R"), written to <see cref="TextBox.Text"/>. Backspace
/// or Delete on its own clears it.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        Cursor = Cursors.Hand;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        var wpf = Keyboard.Modifiers;
        if (wpf == ModifierKeys.None && key is Key.Back or Key.Delete)
        {
            SetCurrentValue(TextProperty, "");
            return;
        }

        var mods = KeyModifiers.None;
        if (wpf.HasFlag(ModifierKeys.Control))
        {
            mods |= KeyModifiers.Ctrl;
        }

        if (wpf.HasFlag(ModifierKeys.Alt))
        {
            mods |= KeyModifiers.Alt;
        }

        if (wpf.HasFlag(ModifierKeys.Shift))
        {
            mods |= KeyModifiers.Shift;
        }

        if (wpf.HasFlag(ModifierKeys.Windows))
        {
            mods |= KeyModifiers.Win;
        }

        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk != 0)
        {
            SetCurrentValue(TextProperty, new HotkeyGesture(mods, vk).ToString());
        }
    }
}
