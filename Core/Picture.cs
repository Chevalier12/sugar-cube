using SkiaSharp;

namespace SugarCube.Core;

/// <summary>An RGBA picture as plain bytes (row 0 at the top), read and written as PNG/JPEG through SkiaSharp.</summary>
public sealed class Picture
{
    public int W, H;
    public byte[] Data;  // R G B A per pixel

    public Picture(int w, int h) { W = w; H = h; Data = new byte[w * h * 4]; }

    public static Picture Decode(byte[] encoded)
    {
        using var bmp = SKBitmap.Decode(encoded) ?? throw new InvalidDataException("the picture cannot be read");
        return FromBitmap(bmp);
    }

    public static Picture Load(string path) => Decode(File.ReadAllBytes(path));

    static Picture FromBitmap(SKBitmap src)
    {
        var info = new SKImageInfo(src.Width, src.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bmp = new SKBitmap(info);
        using (var canvas = new SKCanvas(bmp)) { canvas.Clear(SKColors.Transparent); canvas.DrawBitmap(src, 0, 0); }
        var p = new Picture(bmp.Width, bmp.Height);
        System.Runtime.InteropServices.Marshal.Copy(bmp.GetPixels(), p.Data, 0, p.Data.Length);
        return p;
    }

    public void Save(string path)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bmp = new SKBitmap(info);
        System.Runtime.InteropServices.Marshal.Copy(Data, 0, bmp.GetPixels(), Data.Length);
        using var img = SKImage.FromBitmap(bmp);
        using var png = img.Encode(SKEncodedImageFormat.Png, 100);
        using var f = File.Create(path);
        png.SaveTo(f);
    }

    public (byte r, byte g, byte b, byte a) Get(int x, int y)
    {
        int i = (y * W + x) * 4;
        return (Data[i], Data[i + 1], Data[i + 2], Data[i + 3]);
    }

    public void Set(int x, int y, byte r, byte g, byte b, byte a)
    {
        int i = (y * W + x) * 4;
        Data[i] = r; Data[i + 1] = g; Data[i + 2] = b; Data[i + 3] = a;
    }

    /// <summary>Nearest-pixel resize (pixel art stays pixel art).</summary>
    public Picture ResizedNearest(int w, int h)
    {
        var p = new Picture(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = Math.Min(W - 1, x * W / w), sy = Math.Min(H - 1, y * H / h);
                Buffer.BlockCopy(Data, (sy * W + sx) * 4, p.Data, (y * w + x) * 4, 4);
            }
        return p;
    }
}
