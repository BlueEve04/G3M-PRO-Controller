namespace G3M.Core.Protocol;

/// <summary>
/// 接收器协议常量。命令字来自 <c>g3mpro-macos/Sources/CHIDBridge/CHIDBridge.c</c>，
/// 并已在本机 320F:706E 上用只读探针逐条验证。
/// </summary>
public static class G3mCommands
{
    /// <summary>报文总长度，收发都是 64 字节。</summary>
    public const int ReportSize = 64;

    /// <summary>报文首字节的固定类型标记。</summary>
    public const byte ReportType = 0x04;

    /// <summary>载荷在报文中的起始偏移。</summary>
    public const int PayloadOffset = 8;

    /// <summary>开始配置会话。</summary>
    public const byte BeginSession = 0x01;

    /// <summary>提交并结束配置会话，发送前需要 10 毫秒前摇。</summary>
    public const byte EndSession = 0x02;

    /// <summary>读取 34 字节设备头。</summary>
    public const byte ReadHeader = 0x03;

    /// <summary>读取 84 字节板载配置（偏移 = 86 × 配置编号）。也用于读取配置编号本身（长度 1、偏移 0）。</summary>
    public const byte ReadProfile = 0x05;

    /// <summary>写入 84 字节板载配置。</summary>
    public const byte WriteProfile = 0x06;

    /// <summary>读取按键映射表（槽位数 × 3 字节）。</summary>
    public const byte ReadKeyMapping = 0x07;

    /// <summary>写入按键映射表并持久化，必须包在会话里。</summary>
    public const byte WriteKeyMappingPersistent = 0x09;

    /// <summary>写入按键映射表但只作用于当前运行期，不需要会话。部分固件不支持。</summary>
    public const byte WriteKeyMappingLive = 0x0B;

    /// <summary>电量查询，配合 <see cref="BatterySubCommand"/> 使用。</summary>
    public const byte QueryBattery = 0x1A;

    /// <summary>电量查询的子命令。</summary>
    public const byte BatterySubCommand = 0x06;

    /// <summary>切换当前 DPI 档位，载荷 1 字节。</summary>
    public const byte SetActiveStage = 0x28;

    /// <summary>设置回报率档位索引，载荷 1 字节，取值 0..4。</summary>
    public const byte SetPollingRate = 0x29;

    /// <summary>在线探测。</summary>
    public const byte Ping = 0xAA;

    /// <summary>响应中 <c>response[3]</c> 为该值表示接收器直接拒绝了命令。</summary>
    public const byte ResponseRejected = 0xFF;

    /// <summary>响应中 <c>response[7]</c> 为该值表示设备返回命令错误。</summary>
    public const byte StatusCommandError = 0xFF;

    /// <summary>响应中 <c>response[7]</c> 为该值表示设备返回参数错误。</summary>
    public const byte StatusParameterError = 0xFE;

    /// <summary>在线探测成功时 <c>response[8]</c> 的值。</summary>
    public const byte OnlineMarker = 0xFF;

    /// <summary>结束会话前的固定等待，原驱动对运行期设置和配置区写入都保留了这个窗口。</summary>
    public static readonly TimeSpan CommitDelay = TimeSpan.FromMilliseconds(10);

    /// <summary>板载配置每份占用的存储步长。</summary>
    public const int ProfileStride = 86;

    /// <summary>板载配置的有效载荷长度。</summary>
    public const int ProfileLength = 84;

    /// <summary>设备头长度。</summary>
    public const int HeaderLength = 34;

    /// <summary>设备头未给出合法分块大小时使用的默认值。</summary>
    public const int DefaultChunkSize = 24;

    /// <summary>回报率档位对应的赫兹值，索引与 <see cref="SetPollingRate"/> 的载荷一致。</summary>
    public static readonly int[] PollingRates = [125, 250, 500, 1000, 2000];

    /// <summary>按键映射表每个槽位的字节数。</summary>
    public const int KeySlotSize = 3;

    /// <summary>设备支持的最大 DPI 档位数。</summary>
    public const int MaxDpiStages = 7;
}
