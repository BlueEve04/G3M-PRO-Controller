using G3M.Core.Model;
using G3M.Core.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace G3M.Settings.Views;

/// <summary>概览：当前 DPI、回报率、电量，以及 DPI 档位快切。</summary>
public sealed partial class OverviewPage : Page
{
    public OverviewPage()
    {
        InitializeComponent();

        App.Context.Changed += OnContextChanged;
        Unloaded += (_, _) => App.Context.Changed -= OnContextChanged;

        Render();
    }

    private void OnContextChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(Render);

    private void Render()
    {
        DeviceSnapshot? snapshot = App.Context.Snapshot;
        bool connected = snapshot is not null;

        if (snapshot is null)
        {
            DpiValue.Text = "—";
            RateValue.Text = "—";
            BatteryValue.Text = "—";
            DpiDetail.Text = "等待设备";
            BatteryDetail.Text = App.Context.LastError ?? "等待设备";
            ConnectionValue.Text = "—";
            SensorValue.Text = "—";
            StepValue.Text = "—";
            ProfileValue.Text = "—";
            StageHint.Text = "连接 2.4G 接收器或 USB 线后可用";
            BuildStageButtons(null);
            BuildStageValues(null);
            return;
        }

        DpiValue.Text = snapshot.ActiveDpi.ToString();
        DpiDetail.Text = $"档位 {snapshot.ActiveStage + 1} / {snapshot.Header.StageCount}";
        RateValue.Text = snapshot.PollingRateHz.ToString();
        BatteryValue.Text = snapshot.Battery is { } battery ? $"{battery.Percent}%" : "—";
        BatteryDetail.Text = snapshot.Battery?.ChargingText ?? snapshot.BatteryError ?? "读取失败";

        ConnectionValue.Text = snapshot.ConnectionName;
        SensorValue.Text = $"{snapshot.SensorModelText} ({snapshot.Header.SensorModel})";
        StepValue.Text = snapshot.Header.DpiStep.ToString();
        ProfileValue.Text = snapshot.ProfileNumber.ToString();
        StageHint.Text = "选择后写入板载配置";

        BuildStageButtons(snapshot);
        BuildStageValues(snapshot);
    }

    private void BuildStageButtons(DeviceSnapshot? snapshot)
    {
        StageButtons.Children.Clear();
        if (snapshot is null)
        {
            return;
        }

        for (int stage = 0; stage < snapshot.Header.StageCount; stage++)
        {
            bool isActive = stage == snapshot.ActiveStage;

            var button = new Button
            {
                Content = isActive ? $"{stage + 1} · 当前" : (stage + 1).ToString(),
                Tag = stage,
                IsEnabled = !isActive && !App.Context.IsBusy,
            };

            if (isActive)
            {
                button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            }

            button.Click += async (sender, _) =>
            {
                if (sender is Button { Tag: int target })
                {
                    await App.Context.SetActiveStageAsync(target);
                }
            };

            StageButtons.Children.Add(button);
        }
    }

    private void BuildStageValues(DeviceSnapshot? snapshot)
    {
        StageValues.Items.Clear();
        if (snapshot is null)
        {
            return;
        }

        for (int stage = 0; stage < snapshot.Header.StageCount; stage++)
        {
            bool enabled = snapshot.Profile.GetStage(stage).Enabled;
            bool isActive = stage == snapshot.ActiveStage;

            StageValues.Items.Add(new TextBlock
            {
                Text = enabled ? snapshot.Profile.GetStageDpi(snapshot.Header, stage).ToString() : "—",
                Opacity = isActive ? 1.0 : 0.6,
                FontWeight = isActive ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                MinWidth = 56,
            });
        }
    }
}
