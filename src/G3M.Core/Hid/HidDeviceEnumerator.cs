using System.ComponentModel;
using System.Runtime.InteropServices;

namespace G3M.Core.Hid;

/// <summary>
/// 通过 SetupAPI 枚举 HID 设备接口，并按 G3M Pro 的厂商集合特征筛选。
/// 筛选条件与 <c>g3m-battery/cmd/g3m-battery/hid_windows.go</c> 中已验证的实现一致：
/// 只有同时满足 VID、PID、用途页、用途和报文长度要求的接口才会被保留，
/// 因此不会把普通键盘鼠标误判成 G3M Pro。
/// </summary>
public static class HidDeviceEnumerator
{
    public const ushort G3mVendorId = 0x320F;
    public const ushort G3mWiredProductId = 0x706B;
    public const ushort G3mReceiverProductId = 0x706E;
    public const ushort G3mVendorUsagePage = 0xFF1C;
    public const ushort G3mVendorUsage = 0x0092;
    public const ushort RequiredReportLength = 64;

    /// <summary>设备路径在 SP_DEVICE_INTERFACE_DETAIL_DATA_W 中的偏移（cbSize 字段之后）。</summary>
    private const int DevicePathOffset = 4;

    /// <summary>取 HID 设备接口类 GUID，用于注册设备到达/移除通知。</summary>
    public static Guid GetHidGuid()
    {
        NativeMethods.HidD_GetHidGuid(out Guid hidGuid);
        return hidGuid;
    }

    /// <summary>
    /// 枚举所有 G3M Pro 厂商集合，按"有线优先、2.4G 接收器其次"排序。
    /// 无匹配设备时返回空列表，不抛异常。
    /// </summary>
    public static IReadOnlyList<HidInterfaceInfo> EnumerateG3m()
    {
        var matches = new List<HidInterfaceInfo>();
        foreach (var info in EnumerateAll())
        {
            if (info.UsagePage != G3mVendorUsagePage || info.Usage != G3mVendorUsage)
            {
                continue;
            }

            if (info.ProductId is not (G3mWiredProductId or G3mReceiverProductId))
            {
                continue;
            }

            if (info.InputReportByteLength < RequiredReportLength ||
                info.OutputReportByteLength < RequiredReportLength)
            {
                continue;
            }

            matches.Add(info);
        }

        matches.Sort((left, right) => Priority(left.ProductId).CompareTo(Priority(right.ProductId)));
        return matches;
    }

    /// <summary>枚举本机所有 HID 设备接口。单个接口探测失败会被跳过，不影响其余结果。</summary>
    public static IReadOnlyList<HidInterfaceInfo> EnumerateAll()
    {
        var results = new List<HidInterfaceInfo>();

        NativeMethods.HidD_GetHidGuid(out Guid hidGuid);
        IntPtr deviceInfoSet = NativeMethods.SetupDiGetClassDevsW(
            in hidGuid, IntPtr.Zero, IntPtr.Zero,
            NativeMethods.DigcfPresent | NativeMethods.DigcfDeviceInterface);

        if (deviceInfoSet == NativeMethods.InvalidHandleValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetClassDevsW 调用失败");
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new NativeMethods.SpDeviceInterfaceData
                {
                    cbSize = (uint)Marshal.SizeOf<NativeMethods.SpDeviceInterfaceData>(),
                };

                if (!NativeMethods.SetupDiEnumDeviceInterfaces(
                        deviceInfoSet, IntPtr.Zero, in hidGuid, index, ref interfaceData))
                {
                    // ERROR_NO_MORE_ITEMS 表示枚举正常结束；其他错误同样停止，避免死循环。
                    break;
                }

                string? path = TryGetDevicePath(deviceInfoSet, ref interfaceData);
                if (path is null)
                {
                    continue;
                }

                HidInterfaceInfo? info = TryInspect(path);
                if (info is not null)
                {
                    results.Add(info);
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return results;
    }

    private static string? TryGetDevicePath(
        IntPtr deviceInfoSet, ref NativeMethods.SpDeviceInterfaceData interfaceData)
    {
        // 先问一次所需缓冲区大小；缓冲区传 NULL 时该调用必然"失败"，属于正常流程。
        NativeMethods.SetupDiGetDeviceInterfaceDetailW(
            deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);

        if (requiredSize < DevicePathOffset + sizeof(char))
        {
            return null;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            // x64 上 cbSize 必须是 8，x86 上是 6；这与结构体本身的托管大小不同。
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);

            if (!NativeMethods.SetupDiGetDeviceInterfaceDetailW(
                    deviceInfoSet, ref interfaceData, buffer, requiredSize, out _, IntPtr.Zero))
            {
                return null;
            }

            return Marshal.PtrToStringUni(IntPtr.Add(buffer, DevicePathOffset));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>打开设备路径读取 HID 属性与能力。任何一步不满足都返回 null。</summary>
    private static HidInterfaceInfo? TryInspect(string path)
    {
        IntPtr handle = NativeMethods.CreateFileW(
            path,
            NativeMethods.GenericRead | NativeMethods.GenericWrite,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            NativeMethods.FileAttributeNormal,
            IntPtr.Zero);

        if (handle == NativeMethods.InvalidHandleValue)
        {
            return null;
        }

        try
        {
            var attributes = new NativeMethods.HiddAttributes
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.HiddAttributes>(),
            };

            if (!NativeMethods.HidD_GetAttributes(handle, ref attributes) ||
                attributes.VendorID != G3mVendorId)
            {
                return null;
            }

            if (!NativeMethods.HidD_GetPreparsedData(handle, out IntPtr preparsedData) ||
                preparsedData == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                if (NativeMethods.HidP_GetCaps(preparsedData, out NativeMethods.HidpCaps caps)
                    != NativeMethods.HidpStatusSuccess)
                {
                    return null;
                }

                return new HidInterfaceInfo(
                    path,
                    attributes.ProductID,
                    attributes.VersionNumber,
                    caps.UsagePage,
                    caps.Usage,
                    caps.InputReportByteLength,
                    caps.OutputReportByteLength);
            }
            finally
            {
                NativeMethods.HidD_FreePreparsedData(preparsedData);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static int Priority(ushort productId) =>
        productId == G3mWiredProductId ? 0 : 1;
}
