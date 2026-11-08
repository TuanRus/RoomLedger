using Amazon.CDK;

namespace RoomLedger.Cdk.Stacks;

/**
 * Thông tin dùng chung của một stage (dev, demo) và quy tắc đặt tên tài nguyên theo Coding Convention.
 *
 * @author Tuấn
 * @version 1.0
 */
public record StageConfig(string Stage, string Account, string Region)
{
    /** Stage viết hoa chữ đầu cho tên stack: Dev, Demo. */
    public string StageTitle => char.ToUpperInvariant(Stage[0]) + Stage[1..];

    /**
     * Tên stack RoomLedger-{Stage}-{Tên}.
     *
     * @author Tuấn
     * @version 1.0
     */
    public string StackName(string name) => $"RoomLedger-{StageTitle}-{name}";

    /**
     * Tên tài nguyên roomledger-{stage}-{tên}.
     *
     * @author Tuấn
     * @version 1.0
     */
    public string Name(string name) => $"roomledger-{Stage}-{name}";

    /** Stage demo giữ tài nguyên khi xóa stack; dev xóa luôn để đỡ tốn tiền. */
    public RemovalPolicy Removal => Stage == "demo" ? RemovalPolicy.RETAIN : RemovalPolicy.DESTROY;
}
