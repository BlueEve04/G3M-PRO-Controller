using System.Text.Json.Serialization;
using G3M.Core.Hid;
using G3M.Core.Model;

namespace G3M.Core.History;

/// <summary>历史事件的类型。</summary>
public enum IncidentKind
{
    /// <summary>设备断连。</summary>
    NoDevice = 0,

    /// <summary>设备枚举失败。</summary>
    Enumerate = 1,

    /// <summary>电量读取失败。</summary>
    Read = 2,
}

/// <summary>一次电量采样。</summary>
/// <param name="At">采样时刻（UTC）。</param>
/// <param name="Percent">电量百分比。</param>
/// <param name="Connection">采集时的连接方式，跨连接方式不做续航推算。</param>
/// <param name="Charging">充电状态。</param>
public sealed record BatterySample(
    DateTimeOffset At, int Percent, G3mConnectionKind Connection, ChargingState Charging)
{
    /// <summary>该采样是否处于正常放电状态。</summary>
    public bool IsDischarging => Charging == ChargingState.Normal;
}

/// <summary>一段异常观测区间。<see cref="EndedAt"/> 为 null 表示仍在持续。</summary>
/// <param name="Kind">事件类型。</param>
/// <param name="StartedAt">开始时刻。</param>
/// <param name="EndedAt">结束时刻，null 表示仍在持续。</param>
/// <param name="Message">补充说明。</param>
public sealed record BatteryIncident(
    IncidentKind Kind, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string Message)
{
    /// <summary>给用户看的事件类型名称。</summary>
    public string KindText => Kind switch
    {
        IncidentKind.NoDevice => "设备断连",
        IncidentKind.Enumerate => "设备枚举失败",
        IncidentKind.Read => "读取失败",
        _ => "未知事件",
    };

    /// <summary>是否仍在持续。</summary>
    public bool IsActive => EndedAt is null;
}

/// <summary>磁盘上的历史记录结构。</summary>
internal sealed class BatteryHistoryDocument
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("samples")]
    public List<BatterySampleDocument> Samples { get; set; } = [];

    [JsonPropertyName("incidents")]
    public List<BatteryIncidentDocument> Incidents { get; set; } = [];
}

internal sealed class BatterySampleDocument
{
    [JsonPropertyName("at")]
    public long At { get; set; }

    [JsonPropertyName("percent")]
    public int Percent { get; set; }

    [JsonPropertyName("transport")]
    public int Transport { get; set; }

    [JsonPropertyName("charge")]
    public int Charge { get; set; }
}

internal sealed class BatteryIncidentDocument
{
    [JsonPropertyName("kind")]
    public int Kind { get; set; }

    [JsonPropertyName("startedAt")]
    public long StartedAt { get; set; }

    [JsonPropertyName("endedAt")]
    public long? EndedAt { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
