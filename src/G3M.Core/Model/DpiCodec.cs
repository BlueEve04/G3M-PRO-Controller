namespace G3M.Core.Model;

/// <summary>
/// 原始 DPI 码值与实际 DPI 之间的换算。换算规则取决于传感器型号：
/// 13093 是分段线性的，13193 与 13205 是简单线性，其余型号按步进直乘。
/// </summary>
/// <remarks>
/// 这里的整数运算刻意与 <c>CHIDBridge.c</c> 的 <c>g3m_encode_dpi</c> / <c>g3m_decode_dpi</c>
/// 保持逐位一致，而不是采用 Swift 版里基于 Double 的等价写法——C 版才是真机验证过的生产路径。
/// </remarks>
public static class DpiCodec
{
    public const int MinDpi = 50;
    public const int MaxDpi = 26_000;

    private const ushort SensorPiecewise = 13093;
    private const ushort SensorLinear3395 = 13193;
    private const ushort SensorLinear3325 = 13205;

    /// <summary>把配置里存的原始码值换算成实际 DPI。</summary>
    public static int Decode(DeviceHeader header, ushort raw)
    {
        ArgumentNullException.ThrowIfNull(header);

        uint step = (uint)header.DpiStep;
        uint baseValue = (uint)header.DpiBase;

        if (header.SensorModel == SensorPiecewise)
        {
            return raw > 48
                ? (int)(baseValue + (200u * (raw - 48u)) + (48u * step))
                : (int)(baseValue + (raw * step));
        }

        if (header.SensorModel is SensorLinear3395 or SensorLinear3325)
        {
            return (int)(baseValue + (raw * step));
        }

        return (int)(raw * step);
    }

    /// <summary>把实际 DPI 换算成配置里要写的原始码值。</summary>
    public static ushort Encode(DeviceHeader header, int dpi)
    {
        ArgumentNullException.ThrowIfNull(header);

        uint clamped = (uint)Math.Clamp(dpi, MinDpi, MaxDpi);
        uint step = (uint)header.DpiStep;
        uint baseValue = (uint)header.DpiBase;
        uint raw;

        if (header.SensorModel == SensorPiecewise)
        {
            uint threshold = baseValue + (48u * step);
            raw = clamped <= threshold
                ? (clamped > baseValue ? (clamped - baseValue + (step / 2)) / step : 0u)
                : 48u + ((clamped - threshold + 100u) / 200u);
        }
        else if (header.SensorModel is SensorLinear3395 or SensorLinear3325)
        {
            raw = clamped > baseValue ? (clamped - baseValue + (step / 2)) / step : 0u;
        }
        else
        {
            raw = (clamped + (step / 2)) / step;
        }

        return raw > ushort.MaxValue ? ushort.MaxValue : (ushort)raw;
    }
}
