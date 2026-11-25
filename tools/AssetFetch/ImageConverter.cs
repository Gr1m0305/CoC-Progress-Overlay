using SkiaSharp;

namespace ClashOverlay.AssetFetch;

/// <summary>WebP (or PNG/JPEG) to PNG with SkiaSharp, scaled down so the long side is at most <see cref="MaxSize"/>.</summary>
public static class ImageConverter
{
    /// <summary>Overlay tiles are 40–52 px at 100% (up to ~160 px at 150% on a 2× screen); 256 leaves headroom.</summary>
    public const int MaxSize = 256;

    public static byte[] ToPng(byte[] source, int maxSize = MaxSize)
    {
        using var decoded = Decode(source) ?? throw new InvalidDataException("Not an image SkiaSharp can read.");
        var bitmap = decoded;
        SKBitmap? scaled = null;
        try
        {
            var longSide = Math.Max(decoded.Width, decoded.Height);
            if (longSide > maxSize)
            {
                var f = maxSize / (double)longSide;
                var info = new SKImageInfo(Math.Max(1, (int)Math.Round(decoded.Width * f)), Math.Max(1, (int)Math.Round(decoded.Height * f)),
                    SKColorType.Rgba8888, SKAlphaType.Premul);
                scaled = decoded.Resize(info, SKFilterQuality.High) ?? throw new InvalidDataException("Could not scale the image.");
                bitmap = scaled;
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidDataException("Could not encode PNG.");
            return png.ToArray();
        }
        finally
        {
            scaled?.Dispose();
        }
    }

    private static SKBitmap? Decode(byte[] source)
    {
        try
        {
            return SKBitmap.Decode(source);
        }
        catch (ArgumentException)
        {
            return null; // SkiaSharp throws when it finds no codec for the bytes
        }
    }

    public static bool IsPng(ReadOnlySpan<byte> bytes) =>
        bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G';
}
