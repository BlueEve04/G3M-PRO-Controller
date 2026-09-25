using G3M.Core.Protocol;

namespace G3M.Core.Model;

/// <summary>
/// 34 字节设备头。字段偏移与 <c>G3MModel.swift</c> 的 <c>G3MSnapshot</c> 一致，
/// 各字段值已在本机设备上核对（slots=42、chunk=24、sensor=13205、step=50、base=50、stages=7）。
/// </summary>
public sealed class DeviceHeader
{
    /// <summary>设备头原始字节，长度 34。</summary>
    public byte[] Raw { get; }

    public DeviceHeader(byte[] raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length < G3mCommands.HeaderLength)
        {
            throw new ArgumentException(
                $"设备头长度必须为 {G3mCommands.HeaderLength} 字节，实际 {raw.Length}", nameof(raw));
        }

        Raw = raw;
    }

    /// <summary><c>[5]</c> 按键映射槽位数量。</summary>
    public int KeySlotCount => Raw[5];

    /// <summary><c>[6]</c> 分块大小，非法时回退到协议默认值。</summary>
    public int ChunkSize => G3mDevice.ChunkFromHeader(Raw);

    /// <summary><c>[9..10]</c> 传感器型号（小端）。</summary>
    public ushort SensorModel => (ushort)(Raw[9] | (Raw[10] << 8));

    /// <summary><c>[11]</c> DPI 步进，恒为正数。</summary>
    public int DpiStep => Math.Max(1, (int)Raw[11]);

    /// <summary><c>[12]</c> DPI 档位数量，收敛到 1..7。</summary>
    public int StageCount => Math.Clamp((int)Raw[12], 1, G3mCommands.MaxDpiStages);

    /// <summary><c>[14..15]</c> DPI 基准值（小端）。</summary>
    public int DpiBase => Raw[14] | (Raw[15] << 8);
}
