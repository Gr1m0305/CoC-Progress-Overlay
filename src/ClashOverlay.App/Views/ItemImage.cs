using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClashOverlay.Core.Assets;

namespace ClashOverlay.App.Views;

/// <summary>
/// A game image, or a rounded tile with the name's initials when the artwork has not been downloaded. Dimmed when
/// <see cref="Dim"/> is set (levels the player does not have yet).
/// </summary>
public sealed class ItemImage : Grid
{
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(ImageSource), typeof(ItemImage), new PropertyMetadata(null, (d, _) => ((ItemImage)d).Update()));

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(ItemImage), new PropertyMetadata("", (d, _) => ((ItemImage)d).Update()));

    public static readonly DependencyProperty DimProperty =
        DependencyProperty.Register(nameof(Dim), typeof(bool), typeof(ItemImage), new PropertyMetadata(false, (d, _) => ((ItemImage)d).Update()));

    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Border _tile = new()
    {
        CornerRadius = new CornerRadius(8),
        Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
        BorderThickness = new Thickness(1),
    };

    private readonly TextBlock _initials = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(Color.FromRgb(0xAE, 0xB6, 0xC4)),
    };

    public ItemImage()
    {
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _tile.Child = _initials;
        Children.Add(_tile);
        Children.Add(_image);
        SizeChanged += (_, e) => _initials.FontSize = Math.Max(9, e.NewSize.Height * 0.32);
        Update();
    }

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool Dim
    {
        get => (bool)GetValue(DimProperty);
        set => SetValue(DimProperty, value);
    }

    private void Update()
    {
        _image.Source = Source;
        _image.Visibility = Source is null ? Visibility.Collapsed : Visibility.Visible;
        _tile.Visibility = Source is null ? Visibility.Visible : Visibility.Collapsed;
        _initials.Text = string.IsNullOrEmpty(Text) ? "?" : AssetResolver.Initials(Text);
        Opacity = Dim ? 0.35 : 1;
    }
}
