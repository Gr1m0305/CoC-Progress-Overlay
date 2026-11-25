using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClashOverlay.Core.Assets;
using ClashOverlay.Core.Data;
using Microsoft.Extensions.Logging;

namespace ClashOverlay.App.Services;

/// <summary>
/// Decoded, frozen bitmaps of the downloaded artwork, keyed by file. Images are decoded at the size they are drawn
/// (rows use 40–52 px) so hundreds of rows stay light. Missing files give null and the view draws initials instead.
/// </summary>
internal sealed class ImageCache(AssetResolver assets, ILogger logger)
{
    private readonly Dictionary<(string Path, int Size), ImageSource?> _cache = new();

    public AssetResolver Assets => assets;

    public ImageSource? Item(ItemDef? item, int level, int size = 96) =>
        item is null ? null : File(assets.Item(item, level), size);

    public ImageSource? HeroPortrait(ItemDef hero, int size = 128) =>
        File(assets.Path(AssetPaths.HeroPortrait(hero)), size) ?? Item(hero, 1, size);

    public ImageSource? Remote(string remotePath, int size = 96) => File(assets.Path(remotePath), size);

    public ImageSource? File(string? path, int size = 96)
    {
        if (path is null)
        {
            return null;
        }

        var key = (path, size);
        lock (_cache)
        {
            if (_cache.TryGetValue(key, out var hit))
            {
                return hit;
            }
        }

        ImageSource? image = null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.DecodePixelWidth = size;
            bmp.EndInit();
            bmp.Freeze();
            image = bmp;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UriFormatException or ArgumentException or InvalidOperationException)
        {
            logger.LogDebug("Image {Path} could not be loaded: {Message}", path, e.Message);
        }

        lock (_cache)
        {
            _cache[key] = image;
        }

        return image;
    }

    /// <summary>After an asset update: forget what was loaded so new files show.</summary>
    public void Clear()
    {
        lock (_cache)
        {
            _cache.Clear();
        }
    }
}
