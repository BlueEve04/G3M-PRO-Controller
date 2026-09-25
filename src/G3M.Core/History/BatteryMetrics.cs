using G3M.Core.Model;

namespace G3M.Core.History;

/// <summary>由历史记录推算出来的续航指标。无法可靠推算的项为 null，绝不给出猜测数字。</summary>
/// <param name="LastSample">最近一次有效采样。</param>
/// <param name="CurrentCharging">当前这一轮充电已持续多久。</param>
/// <param name="LastCharging">最近一次完整充电的时长。</param>
/// <param name="LastDischarge">最近一次从 100% 降到 20% 的观测时长。</param>
/// <param name="Remaining">按近期放电速度估算的剩余使用时间。</param>
public sealed record BatteryMetrics(
    BatterySample? LastSample,
    TimeSpan? CurrentCharging,
    TimeSpan? LastCharging,
    TimeSpan? LastDischarge,
    TimeSpan? Remaining)
{
    /// <summary>数据不足时给出原因，供界面展示。</summary>
    public string RemainingUnavailableReason
    {
        get
        {
            if (Remaining is not null)
            {
                return "";
            }

            if (LastSample is null)
            {
                return "暂无采样数据";
            }

            if (!LastSample.IsDischarging)
            {
                return "正在充电，暂不估算";
            }

            return $"连续放电数据不足（需至少 {Describe(BatteryHistoryStore.MinEstimateWindow)}且电量下降 2% 以上）";
        }
    }

    /// <summary>按历史记录计算全部指标。</summary>
    public static BatteryMetrics Build(BatteryHistoryStore store, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(store);

        IReadOnlyList<BatterySample> samples = store.Samples;
        IReadOnlyList<BatteryIncident> incidents = store.Incidents;

        BatterySample? last = samples.Count > 0 ? samples[^1] : null;
        bool hasActiveIncident = incidents.Count > 0 && incidents[^1].IsActive;

        (TimeSpan? lastCharging, TimeSpan? currentCharging) =
            CalculateChargingDurations(store, now);

        TimeSpan? lastDischarge = CalculateDischargeDuration(store);

        // 设备当前处于故障状态时，任何基于"还在正常放电"的推算都不成立。
        TimeSpan? remaining = hasActiveIncident
            ? null
            : EstimateRemaining(store, now);

        if (hasActiveIncident)
        {
            currentCharging = null;
        }

        return new BatteryMetrics(last, currentCharging, lastCharging, lastDischarge, remaining);
    }

    /// <summary>把时长格式化成"3 小时 12 分钟"。</summary>
    public static string Format(TimeSpan? duration)
    {
        if (duration is not { } value)
        {
            return "暂无";
        }

        if (value < TimeSpan.FromMinutes(1))
        {
            return "不到 1 分钟";
        }

        int hours = (int)(value.TotalHours);
        int minutes = value.Minutes;

        if (hours == 0)
        {
            return $"{minutes} 分钟";
        }

        return minutes == 0 ? $"{hours} 小时" : $"{hours} 小时 {minutes} 分钟";
    }

    private static string Describe(TimeSpan duration) => Format(duration);

    private static (TimeSpan? Last, TimeSpan? Current) CalculateChargingDurations(
        BatteryHistoryStore store, DateTimeOffset now)
    {
        IReadOnlyList<BatterySample> samples = store.Samples;
        TimeSpan? lastDuration = null;
        DateTimeOffset? activeStart = null;

        for (int index = 0; index < samples.Count; index++)
        {
            BatterySample sample = samples[index];

            if (index > 0 && !SamplesContinuous(store, samples[index - 1], sample))
            {
                activeStart = null;
            }

            if (sample.Charging == ChargingState.Charging)
            {
                activeStart ??= sample.At;
                continue;
            }

            if (activeStart is null)
            {
                continue;
            }

            if (sample.Charging is ChargingState.Full or ChargingState.Normal)
            {
                TimeSpan duration = sample.At - activeStart.Value;
                if (duration > TimeSpan.Zero)
                {
                    lastDuration = duration;
                }
            }

            activeStart = null;
        }

        if (activeStart is null || samples.Count == 0)
        {
            return (lastDuration, null);
        }

        BatterySample lastSample = samples[^1];

        // 最近一次采样发生在上一次运行期间，当前这一轮充电时长无从谈起。
        if (lastSample.At < store.SessionStartedAt)
        {
            return (lastDuration, null);
        }

        if (lastSample.Charging != ChargingState.Charging ||
            now - lastSample.At > BatteryHistoryStore.MaxSampleGap)
        {
            return (lastDuration, null);
        }

        TimeSpan current = now - activeStart.Value;
        return current <= TimeSpan.Zero ? (lastDuration, null) : (lastDuration, current);
    }

    private static TimeSpan? CalculateDischargeDuration(BatteryHistoryStore store)
    {
        IReadOnlyList<BatterySample> samples = store.Samples;
        TimeSpan? lastDuration = null;
        DateTimeOffset? start = null;

        for (int index = 0; index < samples.Count; index++)
        {
            BatterySample sample = samples[index];

            if (index > 0 && !SamplesContinuous(store, samples[index - 1], sample))
            {
                start = null;
            }

            if (!sample.IsDischarging)
            {
                start = null;
                continue;
            }

            if (start is null)
            {
                if (sample.Percent >= 100)
                {
                    start = sample.At;
                }

                continue;
            }

            if (sample.Percent <= 20)
            {
                TimeSpan duration = sample.At - start.Value;
                if (duration > TimeSpan.Zero)
                {
                    lastDuration = duration;
                }

                start = null;
            }
        }

        return lastDuration;
    }

    private static TimeSpan? EstimateRemaining(BatteryHistoryStore store, DateTimeOffset now)
    {
        IReadOnlyList<BatterySample> samples = store.Samples;
        if (samples.Count < 2)
        {
            return null;
        }

        BatterySample last = samples[^1];
        if (last.At < store.SessionStartedAt ||
            !last.IsDischarging ||
            now - last.At > BatteryHistoryStore.MaxSampleGap)
        {
            return null;
        }

        DateTimeOffset cutoff = now - BatteryHistoryStore.EstimateWindow;
        int firstIndex = samples.Count - 1;

        for (int index = samples.Count - 2; index >= 0; index--)
        {
            BatterySample current = samples[index];
            BatterySample next = samples[index + 1];

            if (current.At < cutoff ||
                !current.IsDischarging ||
                current.Connection != last.Connection ||
                current.Percent < next.Percent ||
                !SamplesContinuous(store, current, next))
            {
                break;
            }

            firstIndex = index;
        }

        BatterySample first = samples[firstIndex];
        TimeSpan elapsed = last.At - first.At;
        int drop = first.Percent - last.Percent;

        if (elapsed < BatteryHistoryStore.MinEstimateWindow || drop < 2)
        {
            return null;
        }

        double ratePerHour = drop / elapsed.TotalHours;
        if (ratePerHour <= 0)
        {
            return null;
        }

        return TimeSpan.FromHours(last.Percent / ratePerHour);
    }

    /// <summary>
    /// 两次采样之间是否可以视为连续观测。跨连接方式、跨故障区间、
    /// 或间隔超过 <see cref="BatteryHistoryStore.MaxSampleGap"/> 都算断开。
    /// </summary>
    private static bool SamplesContinuous(
        BatteryHistoryStore store, BatterySample before, BatterySample after)
    {
        if (after.At - before.At > BatteryHistoryStore.MaxSampleGap)
        {
            return false;
        }

        if (after.At < before.At)
        {
            return false;
        }

        if (before.Connection != after.Connection)
        {
            return false;
        }

        if (before.At < store.SessionStartedAt && after.At >= store.SessionStartedAt)
        {
            return false;
        }

        return !IncidentBetween(store.Incidents, before.At, after.At);
    }

    private static bool IncidentBetween(
        IReadOnlyList<BatteryIncident> incidents, DateTimeOffset start, DateTimeOffset end)
    {
        foreach (BatteryIncident incident in incidents)
        {
            if (incident.StartedAt >= end)
            {
                continue;
            }

            if (incident.EndedAt is null || incident.EndedAt > start)
            {
                return true;
            }
        }

        return false;
    }
}
