using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using G3M.Core.Hid;
using G3M.Core.History;
using G3M.Core.Model;
using G3M.Core.Services;

namespace G3M.Tray;

/// <summary>
/// 托盘程序的全部状态与行为。整个进程只有这一份上下文，
/// 没有窗口、没有窗体，常驻开销就是 NotifyIcon 加两个定时器。
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    /// <summary>设备在线时的轮询间隔。</summary>
    private static readonly TimeSpan PresentInterval = TimeSpan.FromSeconds(5);

    /// <summary>设备不在时的轮询间隔。放慢以减少无谓的设备枚举。</summary>
    private static readonly TimeSpan AbsentInterval = TimeSpan.FromSeconds(30);

    /// <summary>低电量提醒的冷却时间，避免反复打扰。</summary>
    private static readonly TimeSpan LowBatteryCooldown = TimeSpan.FromHours(6);

    /// <summary>历史落盘间隔。</summary>
    private static readonly TimeSpan HistorySaveInterval = TimeSpan.FromMinutes(5);

    private const int LowBatteryThreshold = 20;

    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly System.Windows.Forms.Timer _saveTimer;
    private readonly BatteryIconRenderer _iconRenderer = new();
    private readonly BatteryHistoryStore _history;

    private readonly ToolStripMenuItem _openItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _remainingItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _legacyItem;

    private DeviceWatcher? _deviceWatcher;
    private Icon? _currentIcon;
    private Icon? _retiredIcon;
    private BatteryReadResult? _lastResult;
    private bool _historySavedOnce;
    private DateTimeOffset? _lastLowNotification;
    private bool _disposed;

    public TrayApplicationContext()
    {
        _history = BatteryHistoryStore.Load();

        _openItem = new ToolStripMenuItem("打开设置(&S)…", null, (_, _) => OpenSettings());
        _openItem.Font = new Font(_openItem.Font, FontStyle.Bold);

        _statusItem = new ToolStripMenuItem("正在读取…") { Enabled = false };
        _remainingItem = new ToolStripMenuItem("预计剩余：暂无") { Enabled = false };

        var refreshItem = new ToolStripMenuItem("立即刷新(&R)", null, (_, _) => Refresh());
        _startupItem = new ToolStripMenuItem("开机启动(&A)", null, (_, _) => ToggleStartup())
        {
            CheckOnClick = false,
        };
        var historyItem = new ToolStripMenuItem("电量历史(&H)…", null, (_, _) => OpenSettings("battery"));
        _legacyItem = new ToolStripMenuItem("移除旧版 G3M Battery 开机启动", null, (_, _) => RemoveLegacy());

        var exitItem = new ToolStripMenuItem("退出(&X)", null, (_, _) => ExitApplication());

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(
        [
            _openItem,
            new ToolStripSeparator(),
            _statusItem,
            _remainingItem,
            new ToolStripSeparator(),
            refreshItem,
            historyItem,
            _startupItem,
            _legacyItem,
            new ToolStripSeparator(),
            exitItem,
        ]);

        // 旧版开机启动项只在确实存在时才显示，平时不占位置。
        _legacyItem.Visible = StartupRegistry.DescribeLegacyEntry() is not null;
        StartupRegistry.Reconcile();

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Visible = true,
            Text = "G3M Controller",
        };
        _notifyIcon.MouseClick += OnIconClicked;
        _notifyIcon.BalloonTipClicked += (_, _) => OpenSettings();

        _pollTimer = new System.Windows.Forms.Timer { Interval = (int)PresentInterval.TotalMilliseconds };
        _pollTimer.Tick += (_, _) => Refresh();

        _saveTimer = new System.Windows.Forms.Timer { Interval = (int)HistorySaveInterval.TotalMilliseconds };
        _saveTimer.Tick += (_, _) => _history.Save();

        TryStartDeviceWatcher();

        _pollTimer.Start();
        _saveTimer.Start();

        // 启动后立刻读一次，不等第一个定时周期。
        Refresh();
    }

    private void OnIconClicked(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            OpenSettings();
        }
    }

    /// <summary>读一次电量，记录历史，并刷新托盘显示。</summary>
    private void Refresh()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        BatteryReadResult result = G3mController.ReadBatteryOnce();

        // 设备短暂读取失败时保留上一次的成功读数，
        // 避免鼠标休眠唤醒的瞬间图标就变成错误状态闪烁。
        bool isNewSuccess = result.Success;
        if (isNewSuccess)
        {
            _lastResult = result;
        }
        else if (_lastResult is null || !_lastResult.Success)
        {
            _lastResult = result;
        }

        _history.Record(result, now);

        // 第一次采到数据就立刻落盘。否则要等满 5 分钟的定时保存，
        // 这段时间里用户打开电量历史页会看到"没有数据"。
        if (!_historySavedOnce && _history.IsDirty)
        {
            _historySavedOnce = _history.Save();
        }

        UpdateIcon(result);
        UpdateMenu(result, now);
        MaybeNotifyLowBattery(result, now);
        AdjustPollInterval(result.Present);
    }

    private void UpdateIcon(BatteryReadResult result)
    {
        int size = SystemInformation.SmallIconSize.Width;
        Icon icon;

        if (result.Success)
        {
            BatteryState state = result.State!;
            icon = _iconRenderer.Get(state.Percent, state.IsCharging, hasError: false, size);
        }
        else if (_lastResult?.Success == true)
        {
            BatteryState state = _lastResult.State!;
            icon = _iconRenderer.Get(state.Percent, state.IsCharging, hasError: true, size);
        }
        else
        {
            icon = _iconRenderer.Get(0, charging: false, hasError: true, size);
        }

        if (ReferenceEquals(icon, _currentIcon))
        {
            _notifyIcon.Text = BuildTooltip(result);
            return;
        }

        // 换下来的图标要隔一次刷新再释放：外壳可能仍在引用它，
        // 立刻销毁有让托盘图标短暂消失的风险。
        _retiredIcon?.Dispose();
        _retiredIcon = _currentIcon;
        _currentIcon = icon;
        _notifyIcon.Icon = icon;
        _notifyIcon.Text = BuildTooltip(result);
    }

    private string BuildTooltip(BatteryReadResult result)
    {
        string text;
        if (result.Success)
        {
            BatteryState state = result.State!;
            string bolt = state.IsCharging ? " ⚡" : "";
            text = $"G3M Pro · {state.Percent}%{bolt} · {state.ChargingText} · {DescribeConnection(state.Connection)}";
        }
        else if (_lastResult?.Success == true)
        {
            BatteryState state = _lastResult.State!;
            text = $"G3M Pro · {state.Percent}%（读取异常：{result.Error}）";
        }
        else
        {
            text = $"G3M Pro · {result.Error ?? "未找到设备"}";
        }

        // NotifyIcon.Text 上限 63 个字符，超长会直接抛异常。
        return text.Length <= 63 ? text : text[..60] + "…";
    }

    private void UpdateMenu(BatteryReadResult result, DateTimeOffset now)
    {
        if (result.Success)
        {
            BatteryState state = result.State!;
            _statusItem.Text = $"电量：{state.Percent}% · {state.ChargingText} · {DescribeConnection(state.Connection)}";
        }
        else
        {
            _statusItem.Text = $"电量：{result.Error ?? "未找到设备"}";
        }

        BatteryMetrics metrics = BatteryMetrics.Build(_history, now);
        _remainingItem.Text = metrics.Remaining is { } remaining
            ? $"预计剩余：{BatteryMetrics.Format(remaining)}"
            : "预计剩余：暂无";

        _startupItem.Checked = StartupRegistry.IsEnabled();
    }

    private void MaybeNotifyLowBattery(BatteryReadResult result, DateTimeOffset now)
    {
        if (!result.Success)
        {
            return;
        }

        BatteryState state = result.State!;
        if (!state.IsLow(LowBatteryThreshold))
        {
            return;
        }

        // 边沿触发：只在"降到阈值以下"的那一刻提醒，且 6 小时内不重复。
        if (_lastLowNotification is { } last && now - last < LowBatteryCooldown)
        {
            return;
        }

        _lastLowNotification = now;
        _notifyIcon.BalloonTipTitle = "G3M Pro 电量低";
        _notifyIcon.BalloonTipText = $"鼠标电量 {state.Percent}%，请及时充电。";
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
        _notifyIcon.ShowBalloonTip(10_000);
    }

    private void AdjustPollInterval(bool present)
    {
        int desired = (int)(present ? PresentInterval : AbsentInterval).TotalMilliseconds;
        if (_pollTimer.Interval != desired)
        {
            _pollTimer.Interval = desired;
        }
    }

    private void TryStartDeviceWatcher()
    {
        try
        {
            _deviceWatcher = new DeviceWatcher();
            _deviceWatcher.DeviceChanged += () =>
            {
                // 插拔事件可能早于设备真正就绪，稍等一拍再读。
                _pollTimer.Stop();
                _pollTimer.Interval = 400;
                _pollTimer.Start();
            };
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                                              or InvalidOperationException)
        {
            // 注册通知失败不影响主流程，退回纯轮询即可。
            _deviceWatcher = null;
        }
    }

    private void ToggleStartup()
    {
        bool desired = !StartupRegistry.IsEnabled();
        if (!StartupRegistry.SetEnabled(desired))
        {
            MessageBox.Show(
                "无法修改开机启动项，请检查当前用户注册表权限。",
                "G3M Controller", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        _startupItem.Checked = StartupRegistry.IsEnabled();
    }

    private void RemoveLegacy()
    {
        if (StartupRegistry.RemoveLegacyEntry())
        {
            _legacyItem.Visible = false;
            MessageBox.Show(
                "已移除旧版 G3M Battery 的开机启动项。\n程序本体不会被删除，可自行处理。",
                "G3M Controller", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>
    /// 定位设置程序。两个程序分开打包（设置程序是自包含的，体积大），
    /// 所以按几个常见位置依次找：同目录、同级的 G3M.Settings 目录、同目录的子目录。
    /// </summary>
    private static string? LocateSettings()
    {
        string root = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(root, "G3M.Settings.exe"),
            Path.Combine(root, "G3M.Settings", "G3M.Settings.exe"),
            Path.GetFullPath(Path.Combine(root, "..", "G3M.Settings", "G3M.Settings.exe")),
        ];

        return Array.Find(candidates, File.Exists);
    }

    private void OpenSettings(string? page = null)
    {
        string? path = LocateSettings();

        if (path is null)
        {
            MessageBox.Show(
                "未找到设置程序 G3M.Settings.exe。\n\n"
                + "请把它放在以下任一位置：\n"
                + $"· {AppContext.BaseDirectory}\n"
                + $"· {Path.Combine(AppContext.BaseDirectory, "G3M.Settings")}\\",
                "G3M Controller", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo(path) { UseShellExecute = true };
            if (page is not null)
            {
                startInfo.Arguments = $"--page {page}";
            }

            Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                                              or InvalidOperationException)
        {
            MessageBox.Show(
                $"启动设置程序失败：\n{exception.Message}",
                "G3M Controller", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExitApplication()
    {
        try
        {
            _history.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 退出路径上的落盘失败不值得打断退出流程。
        }

        ExitThread();
    }

    private static string DescribeConnection(G3mConnectionKind connection) => connection switch
    {
        G3mConnectionKind.Wired => "USB 有线",
        G3mConnectionKind.Receiver => "2.4G",
        _ => "未连接",
    };

    protected override void Dispose(bool disposing)
    {
        if (_disposed || !disposing)
        {
            base.Dispose(disposing);
            return;
        }

        _disposed = true;

        _pollTimer.Stop();
        _saveTimer.Stop();
        _pollTimer.Dispose();
        _saveTimer.Dispose();

        _deviceWatcher?.Dispose();

        // 图标隐藏后再释放，否则外壳可能短暂显示一个空图标。
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        _menu.Dispose();
        _iconRenderer.Dispose();
        _currentIcon?.Dispose();
        _retiredIcon?.Dispose();

        _history.Save();

        base.Dispose(disposing);
    }
}
