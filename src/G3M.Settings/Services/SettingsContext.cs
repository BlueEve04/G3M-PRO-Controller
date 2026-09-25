using G3M.Core.Model;
using G3M.Core.Protocol;
using G3M.Core.Services;
using G3M.Core.Storage;

namespace G3M.Settings.Services;

/// <summary>连接与操作状态的集中管理，供各个页面共享。</summary>
/// <remarks>
/// 所有设备操作都在线程池上执行：一次 HID 会话包含十几次往返，会阻塞上百毫秒，
/// 放在 UI 线程上会让界面明显卡顿。await 之后靠同步上下文回到 UI 线程再通知页面。
/// </remarks>
public sealed class SettingsContext
{
    private string _busyText = "";

    /// <summary>最近一次成功读取的快照。未连接时为 null。</summary>
    public DeviceSnapshot? Snapshot { get; private set; }

    /// <summary>最近一次失败的原因，成功时为 null。</summary>
    public string? LastError { get; private set; }

    /// <summary>是否有正在进行的设备操作。</summary>
    public bool IsBusy { get; private set; }

    /// <summary>是否已连接到设备。</summary>
    public bool IsConnected => Snapshot is not null;

    /// <summary>状态栏文字。</summary>
    public string StatusText
    {
        get
        {
            if (IsBusy)
            {
                return _busyText;
            }

            if (LastError is not null)
            {
                return LastError;
            }

            if (Snapshot is null)
            {
                return "未检测到 G3M Pro 设备";
            }

            string battery = Snapshot.Battery is { } state ? $"{state.Percent}%" : "—";
            return $"已连接 · {Snapshot.ConnectionName} · 电量 {battery}";
        }
    }

    /// <summary>快照或状态发生变化时触发，页面据此重绘。</summary>
    public event EventHandler? Changed;

    /// <summary>重新读取全部设备状态。</summary>
    public Task RefreshAsync() =>
        ExecuteAsync("正在读取设备…", controller => controller.ReadSnapshot());

    /// <summary>写入整份板载配置。</summary>
    public Task WriteProfileAsync(MouseProfile profile) => ExecuteAsync(
        "正在写入板载配置…",
        controller =>
        {
            DeviceSnapshot current = RequireSnapshot();
            controller.WriteProfile(profile, current.ProfileIndex, current);
            return controller.ReadSnapshot();
        });

    /// <summary>切换当前 DPI 档位。</summary>
    public Task SetActiveStageAsync(int stage) => ExecuteAsync(
        $"正在切换到档位 {stage + 1}…",
        controller => controller.SetActiveStage(stage, RequireSnapshot()));

    /// <summary>设置回报率。</summary>
    public Task SetPollingRateAsync(int rateIndex) => ExecuteAsync(
        "正在设置回报率…",
        controller => controller.SetPollingRate(rateIndex, RequireSnapshot()));

    /// <summary>写入按键映射表。</summary>
    public Task WriteKeyMappingAsync(KeyMappingTable table) => ExecuteAsync(
        "正在写入按键映射…",
        controller =>
        {
            DeviceSnapshot current = RequireSnapshot();
            controller.WriteKeyMapping(table, current);
            return controller.ReadSnapshot();
        });

    /// <summary>从备份整体还原配置与按键映射。</summary>
    public Task RestoreAsync(DeviceBackup backup) => ExecuteAsync(
        "正在还原备份…",
        controller => controller.Restore(backup));

    private static DeviceBackup? _cachedBackup;
    private static bool _backupLoaded;

    /// <summary>
    /// 最近一次自动备份。每次写入都会产生新备份，因此写入成功后缓存会被置为失效。
    /// 这里做缓存是因为页面每次重绘都会读它，不该反复访问磁盘。
    /// </summary>
    public static DeviceBackup? LatestBackup
    {
        get
        {
            if (!_backupLoaded)
            {
                _cachedBackup = DeviceBackupStore.LoadLatest();
                _backupLoaded = true;
            }

            return _cachedBackup;
        }
    }

    /// <summary>让备份缓存失效，下次读取时重新访问磁盘。</summary>
    public static void InvalidateBackupCache() => _backupLoaded = false;

    /// <summary>
    /// 在线程池上执行一次设备操作。成功后刷新界面，失败时把中文原因放进状态栏。
    /// 每次操作重新打开设备，因此拔插接收器后不需要任何恢复逻辑。
    /// </summary>
    private async Task ExecuteAsync(string busyText, Func<G3mController, DeviceSnapshot> operation)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        _busyText = busyText;
        Notify();

        try
        {
            (DeviceSnapshot? snapshot, string? error) = await Task.Run(() =>
            {
                try
                {
                    using G3mController? controller = G3mController.Open();
                    if (controller is null)
                    {
                        return ((DeviceSnapshot?)null, "未检测到 G3M Pro 设备");
                    }

                    return (operation(controller), (string?)null);
                }
                catch (G3mProtocolException exception)
                {
                    return ((DeviceSnapshot?)null, exception.Message);
                }
            }).ConfigureAwait(true);

            if (snapshot is not null)
            {
                Snapshot = snapshot;
                LastError = null;

                // 写操作会在设备侧留下新的自动备份，缓存必须失效。
                InvalidateBackupCache();
            }
            else
            {
                LastError = error;
            }
        }
        finally
        {
            IsBusy = false;
            Notify();
        }
    }

    private DeviceSnapshot RequireSnapshot() =>
        Snapshot ?? throw new G3mProtocolException(
            G3mErrorKind.ReceiverNotFound, "尚未读取到设备配置，请先刷新");

    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
}
