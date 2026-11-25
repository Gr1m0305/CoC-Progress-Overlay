using System.Windows.Media;
using ClashOverlay.Core.Data;

namespace ClashOverlay.App.ViewModels;

/// <summary>The theme's colours for brushes chosen in code (same values as Themes/Theme.xaml).</summary>
internal static class Palette
{
    public static readonly Brush Gold = Make(0xF5, 0xC5, 0x42);
    public static readonly Brush GoldSoft = Make(0xF5, 0xC5, 0x42, 0x33);
    public static readonly Brush Blue = Make(0x3A, 0x9B, 0xFF);
    public static readonly Brush BlueSoft = Make(0x3A, 0x9B, 0xFF, 0x33);
    public static readonly Brush Elixir = Make(0xB1, 0x5C, 0xFF);
    public static readonly Brush DarkElixir = Make(0x8C, 0x7B, 0xD1);
    public static readonly Brush Red = Make(0xFF, 0x4D, 0x5E);
    public static readonly Brush Green = Make(0x3D, 0xDC, 0x84);
    public static readonly Brush Orange = Make(0xFF, 0x9F, 0x43);
    public static readonly Brush Muted = Make(0x77, 0x80, 0x8F);
    public static readonly Brush ShinyOre = Make(0x6F, 0xD3, 0xFF);
    public static readonly Brush GlowyOre = Make(0xFF, 0xB6, 0x48);
    public static readonly Brush StarryOre = Make(0xC2, 0x8B, 0xFF);

    public static readonly Brush GoldBar = Gradient(0xE0A422, 0xFFD966);
    public static readonly Brush BlueBar = Gradient(0x2477E0, 0x5DB4FF);
    public static readonly Brush ElixirBar = Gradient(0x8A3FFC, 0xD06BFF);
    public static readonly Brush GreenBar = Gradient(0x22B566, 0x5BEA9C);
    public static readonly Brush RedBar = Gradient(0xE0364A, 0xFF6B7A);
    public static readonly Brush OrangeBar = Gradient(0xE07B22, 0xFFB15C);

    /// <summary>The glass behind everything, at the panel opacity from settings.</summary>
    public static Brush Glass(double opacity)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), 0x0D, 0x12, 0x19));
        b.Freeze();
        return b;
    }

    public static Brush For(Resource r) => r switch
    {
        Resource.Gold or Resource.BuilderGold => Gold,
        Resource.Elixir or Resource.BuilderElixir => Elixir,
        Resource.DarkElixir => DarkElixir,
        Resource.ShinyOre => ShinyOre,
        Resource.GlowyOre => GlowyOre,
        Resource.StarryOre => StarryOre,
        Resource.Gems => Green,
        _ => Muted,
    };

    /// <summary>Bar fill by what is being upgraded: buildings gold (blue on the Builder Base), research purple.</summary>
    public static Brush BarFor(Category c, Village v) => c switch
    {
        Category.Troops or Category.Spells or Category.SiegeMachines => ElixirBar,
        Category.Heroes or Category.Pets => OrangeBar,
        Category.Equipment => ElixirBar,
        _ => v == Village.BuilderBase ? BlueBar : GoldBar,
    };

    private static Brush Make(byte r, byte g, byte b, byte a = 0xFF)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Brush Gradient(int from, int to)
    {
        static Color C(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8 & 0xFF), (byte)(rgb & 0xFF));
        var b = new LinearGradientBrush(C(from), C(to), 0);
        b.Freeze();
        return b;
    }
}
