using G3M.Core.Hid;

namespace G3M.Core.Model;

/// <summary>充电状态。</summary>
public enum ChargingState
{
    /// <summary>正常放电中。</summary>
    Normal,

    /// <summary>正在充电。</summary>
    Charging,

    /// <summary>已充满。</summary>
    Full,

    /// <summary>原始状态字节的组合尚未确认，不猜测。</summary>
    Unknown,
}

/// <summary>
/// 一次电量查询的结果。状态字节的归一化规则照抄 <c>g3m-battery</c> 里
/// 已在真机验证过的映射表：<c>0x02</c> 恒为充电，<c>0x01</c> 只在有线连接下有意义。
/// </summary>
/// <param name="Percent">电量百分比，0..100。</param>
/// <param name="RawFlag">设备返回的原始状态字节，仅内部参考。</param>
/// <param name="Charging">归一化后的充电状态。</param>
/// <param name="Connection">读取时的连接方式。</param>
/// <param name="ObservedAt">读取时刻。</param>
public sealed record BatteryState(
    int Percent,
    byte RawFlag,
    ChargingState Charging,
    G3mConnectionKind Connection,
    DateTimeOffset ObservedAt)
{
    /// <summary>是否处于充电或已充满。</summary>
    public bool IsCharging => Charging is ChargingState.Charging or ChargingState.Full;

    /// <summary>给用户看的状态文字。</summary>
    public string ChargingText => Charging switch
    {
        ChargingState.Normal => "普通状态",
        ChargingState.Charging => "正在充电",
        ChargingState.Full => "已充满",
        _ => "状态未知",
    };

    /// <summary>是否应当触发低电量提醒。</summary>
    public bool IsLow(int threshold = 20) => Percent < threshold && !IsCharging;

    /// <summary>
    /// 按 g3m-battery 的规则把原始状态字节归一化。
    /// </summary>
    public static BatteryState FromDevice(
        int percent, byte rawFlag, G3mConnectionKind connection, DateTimeOffset observedAt) =>
        new(percent, rawFlag, Normalize(rawFlag, percent, connection), connection, observedAt);

    private static ChargingState Normalize(
        byte rawFlag, int percent, G3mConnectionKind connection) => rawFlag switch
    {
        0x00 => ChargingState.Normal,
        0x02 => ChargingState.Charging,
        0x01 when connection == G3mConnectionKind.Wired =>
            percent >= 100 ? ChargingState.Full : ChargingState.Charging,
        // 0x01 在非有线连接下的含义尚未确认，按 g3m-battery 的做法显示未知而不是猜。
        _ => ChargingState.Unknown,
    };
}
