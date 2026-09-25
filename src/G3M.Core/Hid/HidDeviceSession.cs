using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace G3M.Core.Hid;

/// <summary>
/// 一个已打开的 HID 设备会话。所有读写都走 OVERLAPPED，以便设置超时——
/// 接收器在某些状态下不会回包，没有超时的同步读会把调用方永久挂住。
/// </summary>
/// <remarks>
/// 设备以 FILE_SHARE_READ | FILE_SHARE_WRITE 打开，因此托盘进程和设置进程
/// 可以同时持有各自的会话，互不干扰（已在真机上验证）。
/// </remarks>
public sealed class HidDeviceSession : IDisposable
{
    private readonly SafeFileHandle _handle;
    private bool _disposed;

    private HidDeviceSession(SafeFileHandle handle, int inputReportLength, int outputReportLength)
    {
        _handle = handle;
        InputReportLength = inputReportLength;
        OutputReportLength = outputReportLength;
    }

    /// <summary>设备上报的输入报文长度（含任何前缀字节）。</summary>
    public int InputReportLength { get; }

    /// <summary>设备上报的输出报文长度。</summary>
    public int OutputReportLength { get; }

    /// <summary>以 OVERLAPPED 方式打开指定设备路径。失败时抛出 <see cref="Win32Exception"/>。</summary>
    public static HidDeviceSession Open(HidInterfaceInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        IntPtr rawHandle = NativeMethods.CreateFileW(
            info.DevicePath,
            NativeMethods.GenericRead | NativeMethods.GenericWrite,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            NativeMethods.FileAttributeNormal | NativeMethods.FileFlagOverlapped,
            IntPtr.Zero);

        if (rawHandle == NativeMethods.InvalidHandleValue)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(), "打开 G3M Pro HID 设备失败");
        }

        return new HidDeviceSession(
            new SafeFileHandle(rawHandle, ownsHandle: true),
            info.InputReportByteLength,
            info.OutputReportByteLength);
    }

    /// <summary>写入一个完整报文，返回实际写入字节数。失败时抛出。</summary>
    public int Write(byte[] packet, int timeoutMilliseconds = 2000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(packet);
        return Transfer(isWrite: true, packet, timeoutMilliseconds);
    }

    /// <summary>
    /// 在超时内尝试读取一个输入报文。超时返回 false 且 <paramref name="bytesRead"/> 为 0，
    /// 而不是抛异常——调用方需要在一个事务里轮询多个时间片。
    /// </summary>
    public bool TryRead(byte[] buffer, int timeoutMilliseconds, out int bytesRead)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(buffer);

        try
        {
            bytesRead = Transfer(isWrite: false, buffer, timeoutMilliseconds);
            return true;
        }
        catch (TimeoutException)
        {
            bytesRead = 0;
            return false;
        }
    }

    /// <summary>丢弃积压的输入报文，避免上一次事务的残留响应被误认为本次的应答。</summary>
    public void Drain(int maxReports = 16, int perReadTimeoutMilliseconds = 2)
    {
        var scratch = new byte[Math.Max(InputReportLength, 128)];
        for (int i = 0; i < maxReports; i++)
        {
            if (!TryRead(scratch, perReadTimeoutMilliseconds, out int read) || read == 0)
            {
                return;
            }
        }
    }

    /// <summary>
    /// 执行一次带超时的 OVERLAPPED 读写。
    /// 读取长度恒为整个缓冲区长度——HID 类驱动要求读取缓冲区不小于输入报告长度，
    /// 请求更少的字节会直接返回 ERROR_INVALID_USER_BUFFER。
    /// </summary>
    private unsafe int Transfer(bool isWrite, byte[] buffer, int timeoutMilliseconds)
    {
        IntPtr completionEvent = NativeMethods.CreateEventW(
            IntPtr.Zero, manualReset: true, initialState: false, name: null);

        if (completionEvent == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建异步 I/O 事件失败");
        }

        try
        {
            var overlapped = new NativeOverlapped { EventHandle = completionEvent };
            IntPtr handle = _handle.DangerousGetHandle();
            uint transferred;
            bool started;

            fixed (byte* pointer = buffer)
            {
                started = isWrite
                    ? NativeMethods.WriteFile(handle, pointer, (uint)buffer.Length, out transferred, ref overlapped)
                    : NativeMethods.ReadFile(handle, pointer, (uint)buffer.Length, out transferred, ref overlapped);
            }

            if (started)
            {
                return (int)transferred;
            }

            int error = Marshal.GetLastWin32Error();
            if (error != NativeMethods.ErrorIoPending)
            {
                throw new Win32Exception(error, isWrite ? "写入 HID 报文失败" : "读取 HID 报文失败");
            }

            uint wait = NativeMethods.WaitForSingleObject(completionEvent, (uint)timeoutMilliseconds);
            if (wait == NativeMethods.WaitTimeout)
            {
                NativeMethods.CancelIoEx(handle, ref overlapped);
                // 必须回收已取消的操作，否则重叠结构体在栈上失效后内核仍会写它。
                NativeMethods.GetOverlappedResult(handle, ref overlapped, out _, wait: true);
                throw new TimeoutException(isWrite ? "写入 HID 报文超时" : "读取 HID 报文超时");
            }

            if (wait != NativeMethods.WaitObject0)
            {
                throw new IOException($"等待 HID I/O 完成时返回未知结果：{wait}");
            }

            if (!NativeMethods.GetOverlappedResult(handle, ref overlapped, out transferred, wait: true))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "获取 HID I/O 结果失败");
            }

            return (int)transferred;
        }
        finally
        {
            NativeMethods.CloseHandle(completionEvent);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handle.Dispose();
    }
}
