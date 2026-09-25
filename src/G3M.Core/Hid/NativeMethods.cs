using System.Runtime.InteropServices;

namespace G3M.Core.Hid;

/// <summary>
/// SetupAPI / hid.dll / kernel32 的原始绑定。这些签名与
/// <c>g3m-battery/cmd/g3m-battery/hid_windows.go</c> 中已在真机验证过的调用一一对应。
/// </summary>
internal static class NativeMethods
{
    internal const uint DigcfPresent = 0x00000002;
    internal const uint DigcfDeviceInterface = 0x00000010;

    internal const uint GenericRead = 0x80000000;
    internal const uint GenericWrite = 0x40000000;
    internal const uint FileShareRead = 0x00000001;
    internal const uint FileShareWrite = 0x00000002;
    internal const uint OpenExisting = 3;
    internal const uint FileAttributeNormal = 0x00000080;
    internal const uint FileFlagOverlapped = 0x40000000;

    internal const uint ErrorNoMoreItems = 259;
    internal const uint ErrorIoPending = 997;
    internal const uint WaitObject0 = 0;
    internal const uint WaitTimeout = 258;
    internal const int HidpStatusSuccess = 0x00110000;

    internal static readonly IntPtr InvalidHandleValue = new(-1);
    internal static readonly IntPtr ErrorNoMoreItemsPtr = new(ErrorNoMoreItems);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpDeviceInterfaceData
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HiddAttributes
    {
        public uint Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    /// <summary>
    /// 字段布局必须与原生 HIDP_CAPS 完全一致，因为 HidP_GetCaps 会写入整个结构体。
    /// 保留字段用 fixed 缓冲区表达，保证大小精确。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        public fixed ushort Reserved[17];
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")]
    internal static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll")]
    internal static extern bool HidD_GetAttributes(IntPtr device, ref HiddAttributes attributes);

    [DllImport("hid.dll")]
    internal static extern bool HidD_GetPreparsedData(IntPtr device, out IntPtr preparsedData);

    [DllImport("hid.dll")]
    internal static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    internal static extern int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevsW(
        in Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, in Guid interfaceClassGuid,
        uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetupDiGetDeviceInterfaceDetailW(
        IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize,
        out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr CreateEventW(
        IntPtr eventAttributes, bool manualReset, bool initialState, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetOverlappedResult(
        IntPtr handle, ref NativeOverlapped overlapped, out uint numberOfBytesTransferred, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CancelIoEx(IntPtr handle, ref NativeOverlapped overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern unsafe bool WriteFile(
        IntPtr handle, byte* buffer, uint numberOfBytesToWrite,
        out uint numberOfBytesWritten, ref NativeOverlapped overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern unsafe bool ReadFile(
        IntPtr handle, byte* buffer, uint numberOfBytesToRead,
        out uint numberOfBytesRead, ref NativeOverlapped overlapped);
}
