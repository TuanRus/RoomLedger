using System.Reflection;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Ai;

/**
 * Tên tác vụ AI và nội dung nhúng sẵn trong assembly (prompt gốc, JSON schema). Prompt chạy thật nằm trong
 * Parameter Store (AiStack nạp từ file prompt lúc deploy); bản nhúng là nguồn gốc và dùng cho test.
 *
 * @author Tuấn
 * @version 1.0
 */
public static class AiTasks
{
    public const string ReadElectricMeter = "readElectricMeter";
    public const string ReadWaterMeter = "readWaterMeter";

    public static string For(MeterKind kind) => kind == MeterKind.Water ? ReadWaterMeter : ReadElectricMeter;

    /** JSON schema của kết quả tác vụ. */
    public static string Schema(string task) => Resource($"Schemas.{task}.schema.json");

    /** Prompt nhúng của tác vụ (trùng nội dung nạp vào Parameter Store). */
    public static string Prompt(string task) => Resource($"Prompts.{task}.md");

    private static string Resource(string name)
    {
        var assembly = typeof(AiTasks).Assembly;
        using var stream = assembly.GetManifestResourceStream($"RoomLedger.Ai.{name}")
            ?? throw new ArgumentException($"Không có tài nguyên AI '{name}'");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
