using G3M.Core.History;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace G3M.Settings.Views;

/// <summary>电量历史页：曲线、续航指标与断连事件。</summary>
/// <remarks>
/// 历史文件由托盘进程写入，本页只读取。托盘没在运行时这里不会有新数据，
/// 但已有记录仍然可以查看。
/// </remarks>
public sealed partial class BatteryPage : Page
{
    private static readonly TimeSpan DayRange = TimeSpan.FromHours(24);
    private static readonly TimeSpan WeekRange = TimeSpan.FromDays(7);

    private TimeSpan _range = DayRange;

    public BatteryPage()
    {
        InitializeComponent();

        App.Context.Changed += OnContextChanged;
        Unloaded += (_, _) => App.Context.Changed -= OnContextChanged;

        Load();
    }

    private void OnContextChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(Load);

    private void OnRangeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            _range = tag == "week" ? WeekRange : DayRange;
        }

        Load();
    }

    private void OnReloadClicked(object sender, RoutedEventArgs e) => Load();

    private void Load()
    {
        BatteryHistoryStore store = BatteryHistoryStore.Load();
        DateTimeOffset now = DateTimeOffset.Now;

        DayButton.Style = _range == DayRange
            ? (Style)Application.Current.Resources["AccentButtonStyle"]
            : null;
        WeekButton.Style = _range == WeekRange
            ? (Style)Application.Current.Resources["AccentButtonStyle"]
            : null;

        DateTimeOffset from = now - _range;

        // 分辨率：24 小时看 10 分钟粒度，7 天看 1 小时粒度。
        int bucketCount = _range == DayRange ? 144 : 168;

        IReadOnlyList<BatteryBucket> buckets =
            BatteryChartSeries.Build(store.Samples, from, now, bucketCount);

        Chart.Render(buckets, from, now);

        int observed = buckets.Count(static b => b.HasData);
        ChartHint.Text = observed == 0
            ? "这段时间没有采样数据。托盘程序需要保持运行才会记录电量。"
            : $"共 {store.Samples.Count} 条采样，图中 {observed} 个时间点有数据；"
              + "曲线在设备未连接的区间会断开，不会跨越空白连线。";

        BatteryMetrics metrics = BatteryMetrics.Build(store, now);

        LastSampleText.Text = metrics.LastSample is { } last
            ? $"{last.At.LocalDateTime:yyyy-MM-dd HH:mm} · {last.Percent}%"
            : "暂无";

        CurrentStateText.Text = App.Context.Snapshot?.Battery is { } battery
            ? $"{battery.Percent}% · {battery.ChargingText}"
            : "未连接设备";

        CurrentChargeText.Text = BatteryMetrics.Format(metrics.CurrentCharging);
        LastChargeText.Text = BatteryMetrics.Format(metrics.LastCharging);
        LastDischargeText.Text = BatteryMetrics.Format(metrics.LastDischarge);

        RemainingText.Text = metrics.Remaining is { } remaining
            ? BatteryMetrics.Format(remaining)
            : metrics.RemainingUnavailableReason;

        BuildIncidents(store.Incidents);
    }

    private void BuildIncidents(IReadOnlyList<BatteryIncident> incidents)
    {
        IncidentList.Children.Clear();

        if (incidents.Count == 0)
        {
            IncidentList.Children.Add(new TextBlock
            {
                Text = "没有记录到断连或读取失败。",
                Opacity = 0.7,
            });
            return;
        }

        // 最近的排前面，最多列 12 条。
        foreach (BatteryIncident incident in incidents.OrderByDescending(static i => i.StartedAt).Take(12))
        {
            string ended = incident.EndedAt is { } end
                ? end.LocalDateTime.ToString("MM-dd HH:mm")
                : "仍在持续";

            IncidentList.Children.Add(new TextBlock
            {
                Text = $"{incident.StartedAt.LocalDateTime:MM-dd HH:mm} → {ended}  "
                       + $"{incident.KindText}　{incident.Message}",
                Opacity = incident.IsActive ? 1.0 : 0.7,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }
}
