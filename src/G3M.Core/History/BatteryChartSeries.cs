namespace G3M.Core.History;

/// <summary>
/// 图表用的一个时间桶。<paramref name="HasGap"/> 为 true 表示这一段没有有效观测，
/// 曲线必须在此断开——跨过未观测区间连线等于凭空捏造数据。
/// </summary>
/// <param name="Start">桶的起始时刻。</param>
/// <param name="Min">桶内最低电量。</param>
/// <param name="Average">桶内平均电量。</param>
/// <param name="Max">桶内最高电量。</param>
/// <param name="Count">桶内采样数，0 表示这一段没有观测。</param>
/// <param name="HasGap">是否与之前的观测断开。</param>
public readonly record struct BatteryBucket(
    DateTimeOffset Start, int Min, int Average, int Max, int Count, bool HasGap)
{
    /// <summary>桶内是否有数据。</summary>
    public bool HasData => Count > 0;
}

/// <summary>把原始采样聚合成固定数量的时间桶，供曲线绘制使用。</summary>
public static class BatteryChartSeries
{
    /// <summary>
    /// 在 <paramref name="from"/> 到 <paramref name="to"/> 之间均匀切分
    /// <paramref name="bucketCount"/> 个桶并聚合采样。
    /// </summary>
    public static IReadOnlyList<BatteryBucket> Build(
        IReadOnlyList<BatterySample> samples,
        DateTimeOffset from,
        DateTimeOffset to,
        int bucketCount)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (bucketCount < 1 || to <= from)
        {
            return [];
        }

        TimeSpan span = to - from;
        double bucketSeconds = span.TotalSeconds / bucketCount;

        var buckets = new List<BatteryBucket>(bucketCount);
        BatterySample? previous = null;
        int cursor = 0;

        // 跳过窗口起点之前的采样，但保留最后一个用于判断连续性。
        while (cursor < samples.Count && samples[cursor].At < from)
        {
            previous = samples[cursor];
            cursor++;
        }

        for (int index = 0; index < bucketCount; index++)
        {
            DateTimeOffset start = from.AddSeconds(bucketSeconds * index);
            DateTimeOffset end = index == bucketCount - 1
                ? to
                : from.AddSeconds(bucketSeconds * (index + 1));

            int min = int.MaxValue;
            int max = int.MinValue;
            long sum = 0;
            int count = 0;
            BatterySample? firstInside = null;

            while (cursor < samples.Count && samples[cursor].At < end)
            {
                BatterySample sample = samples[cursor];
                firstInside ??= sample;

                min = Math.Min(min, sample.Percent);
                max = Math.Max(max, sample.Percent);
                sum += sample.Percent;
                count++;
                previous = sample;
                cursor++;
            }

            if (count == 0)
            {
                buckets.Add(new BatteryBucket(start, 0, 0, 0, 0, HasGap: true));
                continue;
            }

            bool hasGap = previous is not null
                          && firstInside is not null
                          && firstInside.At - previous.At > BatteryHistoryStore.MaxSampleGap;

            buckets.Add(new BatteryBucket(
                start, min, (int)Math.Round((double)sum / count), max, count, hasGap));
        }

        return buckets;
    }
}
