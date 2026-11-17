using ImageMagick;
using ImageMagick.Drawing;
using PerceptualHash = RoomLedger.Ai.PerceptualHash;

namespace RoomLedger.Functions.Tests.Ai;

/**
 * Perceptual hash (BR-26 RULE 2): cặp ảnh nhận phòng / trả phòng gần như giống nhau (khoảng cách &lt; 6) thì không gọi
 * Bedrock. Hash DCT 64 bit, khoảng cách Hamming. Ảnh dựng bằng Magick.NET: tường, sofa, cửa sổ, cửa.
 *
 * @author Sang
 * @version 1.0
 */
public class PerceptualHashTests
{
    private const int Threshold = 6;   // mặc định trong SSM (BR-26)

    /** Một "phòng" 640x480; mirrored đổi chỗ đồ đạc; stain thêm vết bẩn lớn. */
    private static byte[] Room(bool mirrored = false, bool stain = false, int quality = 92, int width = 640,
        int brightness = 100, MagickFormat format = MagickFormat.Jpeg)
    {
        using var img = new MagickImage(new MagickColor("#c8c4bc"), 640, 480);
        int X(int x) => mirrored ? 640 - x : x;
        new Drawables()
            .FillColor(new MagickColor("#3a3f4a")).Rectangle(Math.Min(X(40), X(300)), 300, Math.Max(X(40), X(300)), 440)   // sofa
            .FillColor(new MagickColor("#e8f0ff")).Rectangle(Math.Min(X(400), X(600)), 60, Math.Max(X(400), X(600)), 220)  // cửa sổ
            .FillColor(new MagickColor("#7a5230")).Rectangle(Math.Min(X(330), X(390)), 180, Math.Max(X(330), X(390)), 440) // cửa
            .Draw(img);
        if (stain)
        {
            new Drawables().FillColor(new MagickColor("#2a2018")).Ellipse(170, 150, 150, 110, 0, 360).Draw(img);
        }
        if (brightness != 100)
        {
            img.BrightnessContrast(new Percentage(brightness - 100), new Percentage(0));
        }
        if (width != 640)
        {
            img.Resize((uint)width, 0);
        }
        img.Quality = (uint)quality;
        return img.ToByteArray(format);
    }

    private static ulong Hash(byte[] image)
    {
        using var s = new MemoryStream(image);
        return PerceptualHash.Compute(s) ?? throw new Xunit.Sdk.XunitException("hash null");
    }

    [Fact]
    public void SameImage_HasDistanceZero_AndTheHashIsStable()
    {
        var room = Room();
        Assert.Equal(Hash(room), Hash(room));
        Assert.Equal(0, PerceptualHash.Distance(Hash(room), Hash(room)));
    }

    [Theory]
    [InlineData(40, 640, 100)]    // nén JPEG mạnh
    [InlineData(92, 320, 100)]    // ảnh nhỏ hơn một nửa
    [InlineData(92, 640, 112)]    // sáng hơn một chút
    public void SameRoom_ReEncodedResizedOrBrighter_IsBelowTheThreshold(int quality, int width, int brightness)
    {
        var d = PerceptualHash.Distance(Hash(Room()), Hash(Room(quality: quality, width: width, brightness: brightness)));
        Assert.True(d < Threshold, $"distance {d}");
    }

    [Fact]
    public void SameRoom_PngVersusJpeg_IsBelowTheThreshold()
    {
        var d = PerceptualHash.Distance(Hash(Room()), Hash(Room(format: MagickFormat.Png)));
        Assert.True(d < Threshold, $"distance {d}");
    }

    [Fact]
    public void FurnitureMoved_IsAtOrAboveTheThreshold()
    {
        var d = PerceptualHash.Distance(Hash(Room()), Hash(Room(mirrored: true)));
        Assert.True(d >= Threshold, $"distance {d}");
    }

    [Fact]
    public void LargeStain_IsAtOrAboveTheThreshold_SoBedrockLooksAtIt()
    {
        var d = PerceptualHash.Distance(Hash(Room()), Hash(Room(stain: true)));
        Assert.True(d >= Threshold, $"distance {d}");
    }

    [Fact]
    public void Distance_IsHammingOver64Bits()
    {
        Assert.Equal(0, PerceptualHash.Distance(0, 0));
        Assert.Equal(64, PerceptualHash.Distance(0, ulong.MaxValue));
        Assert.Equal(2, PerceptualHash.Distance(0b1010, 0b0000));
        Assert.Equal(PerceptualHash.Distance(5, 9), PerceptualHash.Distance(9, 5));
    }

    [Fact]
    public void NotAnImage_ReturnsNull()
    {
        using var junk = new MemoryStream("not an image"u8.ToArray());
        Assert.Null(PerceptualHash.Compute(junk));
    }
}
