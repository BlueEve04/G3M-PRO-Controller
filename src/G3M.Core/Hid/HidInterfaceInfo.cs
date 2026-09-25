namespace G3M.Core.Hid;

/// <summary>连接方式。</summary>
public enum G3mConnectionKind
{
    /// <summary>未发现设备。</summary>
    None = 0,

    /// <summary>USB 有线（PID 0x706B）。</summary>
    Wired,

    /// <summary>2.4G 接收器（PID 0x706E）。</summary>
    Receiver,
}

/// <summary>一个已通过筛选的 G3M Pro 厂商 HID 集合。</summary>
/// <param name="DevicePath">可直接传给 CreateFileW 的设备接口路径。</param>
/// <param name="ProductId">产品 ID，用于区分有线与 2.4G 接收器。</param>
/// <param name="VersionNumber">设备上报的版本号。</param>
/// <param name="UsagePage">HID 用途页，G3M Pro 厂商集合为 0xFF1C。</param>
/// <param name="Usage">HID 用途，G3M Pro 厂商集合为 0x0092。</param>
/// <param name="InputReportByteLength">输入报文长度，必须能容纳 64 字节查询。</param>
/// <param name="OutputReportByteLength">输出报文长度，必须能容纳 64 字节查询。</param>
public sealed record HidInterfaceInfo(
    string DevicePath,
    ushort ProductId,
    ushort VersionNumber,
    ushort UsagePage,
    ushort Usage,
    ushort InputReportByteLength,
    ushort OutputReportByteLength)
{
    /// <summary>该接口对应的连接方式。</summary>
    public G3mConnectionKind Connection => ProductId switch
    {
        HidDeviceEnumerator.G3mWiredProductId => G3mConnectionKind.Wired,
        HidDeviceEnumerator.G3mReceiverProductId => G3mConnectionKind.Receiver,
        _ => G3mConnectionKind.None,
    };

    /// <summary>用于界面展示的连接方式名称。</summary>
    public string ConnectionName => Connection switch
    {
        G3mConnectionKind.Wired => "USB 有线",
        G3mConnectionKind.Receiver => "2.4G 接收器",
        _ => "未知连接",
    };
}
