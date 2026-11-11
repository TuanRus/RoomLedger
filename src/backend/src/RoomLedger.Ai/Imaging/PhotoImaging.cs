using System.Globalization;
using ImageMagick;

namespace RoomLedger.Ai.Imaging;

/**
 * Bản xem JPEG (giữ kích thước) và thumbnail JPEG 400 px của một ảnh, không còn EXIF/GPS.
 *
 * @author Sang
 * @version 1.0
 */
public sealed record DerivedImages(byte[] ViewJpeg, byte[] ThumbnailJpeg);

/**
 * Xử lý ảnh cho photo-ingest và photo-derive (BR-16 RULE 4–5), dùng Magick.NET (đọc được HEIC của iPhone).
 * Ảnh gốc trong bucket incoming/evidence không bao giờ bị sửa; chỉ bản xem trong bucket derived bị xóa GPS.
 *
 * @author Sang
 * @version 1.0
 */
public static class PhotoImaging
{
    public const int ThumbnailSize = 400;

    /** EXIF không ghi múi giờ thì coi là giờ Việt Nam (người dùng của hệ thống ở Việt Nam). */
    public static readonly TimeSpan DefaultOffset = TimeSpan.FromHours(7);

    /**
     * Ngày chụp từ EXIF DateTimeOriginal (+ OffsetTimeOriginal nếu có). Không có EXIF hoặc không đọc được ảnh → null.
     * Chỉ đọc metadata (Ping), không giải mã điểm ảnh.
     *
     * @author Sang
     * @version 1.0
     */
    public static DateTimeOffset? ReadTakenAt(Stream image)
    {
        try
        {
            using var img = new MagickImage();
            img.Ping(image);
            var exif = img.GetExifProfile();
            var raw = exif?.GetValue(ExifTag.DateTimeOriginal)?.Value?.Trim('\0', ' ');
            if (raw is null || !DateTime.TryParseExact(raw, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var local))
            {
                return null;
            }
            return new DateTimeOffset(local, ParseOffset(exif!.GetValue(ExifTag.OffsetTimeOriginal)?.Value) ?? DefaultOffset);
        }
        catch (MagickException)
        {
            return null;
        }
    }

    /**
     * HEIC/PNG/JPEG → bản xem JPEG cùng kích thước và thumbnail 400 px. Xoay theo EXIF Orientation rồi xóa EXIF, XMP,
     * IPTC (có GPS); giữ ICC để màu không đổi.
     *
     * @author Sang
     * @version 1.0
     */
    public static DerivedImages CreateDerived(Stream image)
    {
        using var img = new MagickImage(image);
        img.AutoOrient();
        img.RemoveProfile("exif");
        img.RemoveProfile("xmp");
        img.RemoveProfile("iptc");
        if (img.HasAlpha)
        {
            img.BackgroundColor = MagickColors.White;
            img.Alpha(AlphaOption.Remove);
        }

        img.Quality = 85;
        var view = img.ToByteArray(MagickFormat.Jpeg);

        img.Thumbnail(new MagickGeometry(ThumbnailSize, ThumbnailSize));
        img.Quality = 80;
        var thumbnail = img.ToByteArray(MagickFormat.Jpeg);
        return new DerivedImages(view, thumbnail);
    }

    /** "+09:00" hoặc "-05:00" → TimeSpan; không hợp lệ → null. */
    private static TimeSpan? ParseOffset(string? value)
    {
        var v = value?.Trim('\0', ' ');
        if (string.IsNullOrEmpty(v))
        {
            return null;
        }
        return TimeSpan.TryParse(v.TrimStart('+'), CultureInfo.InvariantCulture, out var offset) ? offset : null;
    }
}
