using System.Numerics;
using ImageMagick;

namespace RoomLedger.Ai;

/**
 * Perceptual hash DCT 64 bit để lọc cặp ảnh gần như giống hệt trước khi gọi Bedrock (BR-26 RULE 2).
 * Cách tính: xoay theo EXIF → xám → 32x32 → DCT 2 chiều → khối 8x8 tần số thấp → bit = hệ số &gt; trung vị
 * (trung vị bỏ hệ số DC). Hai ảnh cùng cảnh dù nén, đổi cỡ hay sáng hơn cho khoảng cách Hamming nhỏ;
 * ngưỡng mặc định 6 nằm trong SSM.
 *
 * @author Sang
 * @version 1.0
 */
public static class PerceptualHash
{
    private const int Size = 32;
    private const int Low = 8;
    private static readonly double[,] Cos = BuildCos();

    /**
     * Hash 64 bit của ảnh; không đọc được ảnh → null (bên gọi coi như không lọc được, gửi so sánh).
     *
     * @author Sang
     * @version 1.0
     */
    public static ulong? Compute(Stream image)
    {
        double[,] pixels;
        try
        {
            using var img = new MagickImage(image);
            img.AutoOrient();
            img.Grayscale(PixelIntensityMethod.Rec709Luminance);
            img.Resize(new MagickGeometry(Size, Size) { IgnoreAspectRatio = true });
            using var data = img.GetPixels();
            pixels = new double[Size, Size];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    pixels[x, y] = data.GetPixel(x, y)!.GetChannel(0);
                }
            }
        }
        catch (MagickException)
        {
            return null;
        }

        var coeffs = new double[Low * Low];
        for (var u = 0; u < Low; u++)
        {
            for (var v = 0; v < Low; v++)
            {
                double sum = 0;
                for (var x = 0; x < Size; x++)
                {
                    for (var y = 0; y < Size; y++)
                    {
                        sum += pixels[x, y] * Cos[u, x] * Cos[v, y];
                    }
                }
                coeffs[u * Low + v] = sum;
            }
        }

        var median = coeffs.Skip(1).Order().ElementAt((Low * Low - 1) / 2);
        ulong hash = 0;
        for (var i = 0; i < coeffs.Length; i++)
        {
            if (coeffs[i] > median)
            {
                hash |= 1UL << i;
            }
        }
        return hash;
    }

    /** Khoảng cách Hamming giữa hai hash (0 = giống hệt, 64 = khác mọi bit). */
    public static int Distance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    private static double[,] BuildCos()
    {
        var c = new double[Low, Size];
        for (var u = 0; u < Low; u++)
        {
            for (var x = 0; x < Size; x++)
            {
                c[u, x] = Math.Cos((2 * x + 1) * u * Math.PI / (2 * Size));
            }
        }
        return c;
    }
}
