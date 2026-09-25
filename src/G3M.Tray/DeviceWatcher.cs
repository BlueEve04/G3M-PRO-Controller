using System.Runtime.InteropServices;
using System.Windows.Forms;
using G3M.Core.Hid;

namespace G3M.Tray;

/// <summary>
/// 监听 HID 设备的到达与移除，让托盘在拔插接收器时立刻刷新，
/// 而不必等下一次轮询。设备不在时轮询可以因此放慢，省下无谓的枚举开销。
/// </summary>
internal sealed class DeviceWatcher : NativeWindow, IDisposable
{
    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8001;
    private const int DbtDevTypeDeviceInterface = 0x00000005;
    private const int DeviceNotifyWindowHandle = 0x00000000;

    private IntPtr _notificationHandle = IntPtr.Zero;
    private bool _disposed;

    /// <summary>设备列表发生变化时触发。回调在 UI 线程上。</summary>
    public event Action? DeviceChanged;

    public DeviceWatcher()
    {
        // 一个只用来收消息的隐藏窗口。它不是 Form，不参与消息循环之外的生命周期。
        CreateHandle(new CreateParams
        {
            Caption = "G3M.Tray.DeviceWatcher",
            X = 0,
            Y = 0,
            Width = 0,
            Height = 0,
            Style = 0,
            ExStyle = 0,
            Parent = IntPtr.Zero,
        });

        Guid hidGuid = HidDeviceEnumerator.GetHidGuid();

        var filter = new DeviceBroadcastDeviceInterface
        {
            Size = Marshal.SizeOf<DeviceBroadcastDeviceInterface>(),
            DeviceType = DbtDevTypeDeviceInterface,
            Reserved = 0,
            ClassGuid = hidGuid,
            Name = 0,
        };

        _notificationHandle = RegisterDeviceNotification(
            Handle, ref filter, DeviceNotifyWindowHandle);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmDeviceChange)
        {
            int eventType = message.WParam.ToInt32();
            if (eventType is DbtDeviceArrival or DbtDeviceRemoveComplete)
            {
                DeviceChanged?.Invoke();
            }
        }

        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_notificationHandle != IntPtr.Zero)
        {
            UnregisterDeviceNotification(_notificationHandle);
            _notificationHandle = IntPtr.Zero;
        }

        DestroyHandle();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceBroadcastDeviceInterface
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public Guid ClassGuid;
        public ushort Name;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr RegisterDeviceNotification(
        IntPtr recipient, ref DeviceBroadcastDeviceInterface notificationFilter, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);
}
