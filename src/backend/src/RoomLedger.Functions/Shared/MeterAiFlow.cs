using System.Text.Json.Serialization;

namespace RoomLedger.Functions.Shared;

/**
 * Hợp đồng dữ liệu giữa các bước của state machine đọc đồng hồ (AiStack): meter-prepare (VPC) → read-document
 * (ngoài VPC, gọi Bedrock) → meter-persist (VPC). Tên trường cố định bằng JsonPropertyName vì state machine
 * (Amazon States Language) trỏ tới chúng bằng JSONPath.
 *
 * @author Tuấn
 * @version 1.0
 */
public sealed record MeterPrepareResult(
    [property: JsonPropertyName("found")] bool Found,
    [property: JsonPropertyName("readingId")] Guid? ReadingId = null,
    [property: JsonPropertyName("key")] string? Key = null,
    [property: JsonPropertyName("task")] string? Task = null);

/** Đầu vào của read-document: ảnh nào, tác vụ nào. */
public sealed record ReadDocumentInput(
    [property: JsonPropertyName("readingId")] Guid ReadingId,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("task")] string Task);

/** Kết quả đọc ảnh; readable = false thì value là null. */
public sealed record ReadDocumentOutput(
    [property: JsonPropertyName("readingId")] Guid ReadingId,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("readable")] bool Readable,
    [property: JsonPropertyName("value")] long? Value,
    [property: JsonPropertyName("confidence")] decimal Confidence,
    [property: JsonPropertyName("note")] string Note);

/** Đầu vào của meter-persist: kết quả đọc, hoặc lỗi (Bedrock lỗi sau khi thử lại) để đánh dấu NEEDS_REVIEW. */
public sealed record MeterPersistInput(
    [property: JsonPropertyName("readingId")] Guid ReadingId,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("readable")] bool Readable = false,
    [property: JsonPropertyName("value")] long? Value = null,
    [property: JsonPropertyName("confidence")] decimal? Confidence = null,
    [property: JsonPropertyName("note")] string? Note = null,
    [property: JsonPropertyName("error")] string? Error = null);
