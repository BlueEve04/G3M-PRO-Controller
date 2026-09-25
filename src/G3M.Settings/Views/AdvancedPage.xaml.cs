using G3M.Core.Model;
using G3M.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace G3M.Settings.Views;

/// <summary>高级页：直线修正、波纹修正、按键防抖、静默高度，以及从备份还原。</summary>
public sealed partial class AdvancedPage : Page
{
    private bool _suppressEvents;

    public AdvancedPage()
    {
        InitializeComponent();

        DebounceCombo.ItemsSource = MouseProfile.DebounceOptionsMs.Select(static ms => $"{ms} ms").ToList();
        LiftCombo.ItemsSource = MouseProfile.LiftHeightOptionsMm.Select(static mm => $"{mm} mm").ToList();

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
        bool interactive = connected && !App.Context.IsBusy;

        // 赋值会触发控件的变更事件，用标志位挡住，避免把程序化赋值误当成用户修改。
        _suppressEvents = true;
        try
        {
            if (snapshot is not null)
            {
                LineCorrectionSwitch.IsOn = snapshot.Profile.LineCorrection;
                WaveCorrectionSwitch.IsOn = snapshot.Profile.WaveCorrection;
                DebounceCombo.SelectedIndex = snapshot.Profile.DebounceIndex;
                LiftCombo.SelectedIndex = snapshot.Profile.LiftHeightIndex;
            }
            else
            {
                LineCorrectionSwitch.IsOn = false;
                WaveCorrectionSwitch.IsOn = false;
                DebounceCombo.SelectedIndex = -1;
                LiftCombo.SelectedIndex = -1;
            }
        }
        finally
        {
            _suppressEvents = false;
        }

        LineCorrectionSwitch.IsEnabled = interactive;
        WaveCorrectionSwitch.IsEnabled = interactive;
        DebounceCombo.IsEnabled = interactive;
        LiftCombo.IsEnabled = interactive;
        ApplyButton.IsEnabled = interactive;
        ResetButton.IsEnabled = interactive;

        // 两个分支都要写：页面构造时快照还是空的，等数据到了只走下面这一支，
        // 不重置的话提示文字会一直停在"连接设备后可用"。
        Hint.Text = connected
            ? "修改后写入当前板载配置，写入前会自动备份，写入后逐字节读回校验"
            : "连接设备后可用";

        DeviceBackup? backup = Services.SettingsContext.LatestBackup;
        RestoreButton.IsEnabled = backup is not null && interactive;
        BackupHint.Text = backup is null
            ? "还没有可用的备份"
            : $"最近备份：{backup.Summary}";
    }

    private async void OnApplyClicked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || App.Context.Snapshot is not { } snapshot)
        {
            return;
        }

        MouseProfile draft = snapshot.Profile.Clone();
        draft.LineCorrection = LineCorrectionSwitch.IsOn;
        draft.WaveCorrection = WaveCorrectionSwitch.IsOn;
        draft.DebounceIndex = Math.Max(0, DebounceCombo.SelectedIndex);
        draft.LiftHeightIndex = Math.Max(0, LiftCombo.SelectedIndex);

        await App.Context.WriteProfileAsync(draft);
    }

    private void OnResetClicked(object sender, RoutedEventArgs e) => Render();

    private async void OnRestoreClicked(object sender, RoutedEventArgs e)
    {
        DeviceBackup? backup = Services.SettingsContext.LatestBackup;
        if (backup is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "从备份还原",
            Content = $"将把板载配置和按键映射整体还原为：\n\n{backup.Summary}\n\n当前设置会被覆盖。",
            PrimaryButtonText = "还原",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await App.Context.RestoreAsync(backup);
        }
    }
}
