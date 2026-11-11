using ImageMagick;
using RoomLedger.Ai.Imaging;

namespace RoomLedger.Functions.Tests.Photos;

/**
 * Xử lý ảnh (BR-16 RULE 4–5): đọc ngày chụp EXIF, HEIC → JPEG, thumbnail 400 px, xóa GPS khỏi bản xem.
 *
 * @author Sang
 * @version 1.0
 */
public class PhotoImagingTests
{
    [Fact]
    public void ReadTakenAt_Heic_UsesVietnamTimeWhenExifHasNoOffset()
    {
        using var heic = new MemoryStream(SampleImages.Heic());
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 8, 30, 0, TimeSpan.FromHours(7)), PhotoImaging.ReadTakenAt(heic));
    }

    [Fact]
    public void ReadTakenAt_Jpeg_UsesOffsetTimeOriginalWhenPresent()
    {
        using var jpeg = new MemoryStream(SampleImages.JpegWithGps());
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 20, 15, 0, TimeSpan.FromHours(9)), PhotoImaging.ReadTakenAt(jpeg));
    }

    [Fact]
    public void ReadTakenAt_NoExif_ReturnsNull()
    {
        using var png = new MemoryStream(SampleImages.PngWithoutExif());
        Assert.Null(PhotoImaging.ReadTakenAt(png));
    }

    [Fact]
    public void ReadTakenAt_NotAnImage_ReturnsNull()
    {
        using var junk = new MemoryStream("not an image"u8.ToArray());
        Assert.Null(PhotoImaging.ReadTakenAt(junk));
    }

    [Fact]
    public void CreateDerived_Heic_GivesFullSizeJpegAndPortraitThumbnail_WithoutGpsOrExif()
    {
        using var heic = new MemoryStream(SampleImages.Heic());
        var derived = PhotoImaging.CreateDerived(heic);

        using var view = new MagickImage(derived.ViewJpeg);
        using var thumb = new MagickImage(derived.ThumbnailJpeg);
        Assert.Equal(MagickFormat.Jpeg, view.Format);
        Assert.Equal(MagickFormat.Jpeg, thumb.Format);
        Assert.Equal((3024u, 4032u), (view.Width, view.Height));     // giữ kích thước, xoay đúng chiều dọc
        Assert.Equal(400u, Math.Max(thumb.Width, thumb.Height));
        Assert.True(thumb.Height > thumb.Width);                    // không bị xoay hai lần
        Assert.Null(view.GetExifProfile());
        Assert.Null(thumb.GetExifProfile());
    }

    [Fact]
    public void CreateDerived_JpegWithGps_StripsGps()
    {
        using var jpeg = new MemoryStream(SampleImages.JpegWithGps());
        var derived = PhotoImaging.CreateDerived(jpeg);

        using var view = new MagickImage(derived.ViewJpeg);
        Assert.Null(view.GetExifProfile()?.GetValue(ExifTag.GPSLatitude));
        Assert.Equal((800u, 600u), (view.Width, view.Height));
    }
}
