using G3M.Core.Hid;

namespace G3M.Core.Model;

/// <summary>一次完整读取的结果：设备头、板载配置、按键映射表、电量。</summary>
/// <param name="Connection">连接方式。</param>
/// <param name="Header">34 字节设备头。</param>
/// <param name="Profile">84 字节板载配置。</param>
/// <param name="ProfileIndex">当前配置编号，本机设备恒为 0。</param>
/// <param name="KeyMapping">按键映射表。</param>
/// <param name="Battery">电量，读取失败时为 null。</param>
/// <param name="BatteryError">电量读取失败的原因，成功时为 null。</param>
public sealed record DeviceSnapshot(
    G3mConnectionKind Connection,
    DeviceHeader Header,
    MouseProfile Profile,
    byte ProfileIndex,
    KeyMappingTable KeyMapping,
    BatteryState? Battery,
    string? BatteryError)
{
    /// <summary>连接方式的中文名称。</summary>
    public string ConnectionName => Connection switch
    {
        G3mConnectionKind.Wired => "USB 有线",
        G3mConnectionKind.Receiver => "2.4G 接收器",
        _ => "未连接",
    };

    /// <summary>当前生效的 DPI 档位索引。</summary>
    public int ActiveStage => Profile.ActiveStage;

    /// <summary>当前生效档位的实际 DPI。</summary>
    public int ActiveDpi => Profile.GetStageDpi(Header, ActiveStage);

    /// <summary>当前回报率，单位赫兹。</summary>
    public int PollingRateHz => Profile.PollingRateHz;

    /// <summary>用于界面展示的传感器型号。</summary>
    public string SensorModelText => $"0x{Header.SensorModel:X4}";

    /// <summary>用于界面展示的配置编号（从 1 开始）。</summary>
    public int ProfileNumber => ProfileIndex + 1;
}
