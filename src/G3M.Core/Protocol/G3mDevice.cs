using System.ComponentModel;
using G3M.Core.Hid;

namespace G3M.Core.Protocol;

/// <summary>
/// 面向一个已打开设备的命令层。只负责收发原始字节，不解释配置语义——
/// 解析成模型的工作在 <see cref="Services.G3mController"/> 里。
/// </summary>
/// <remarks>
/// 事务循环的行为照抄已在真机验证的 <c>CHIDBridge.c</c>：
/// 每次事务前排空积压报文，读超时按 100 毫秒切片，遇到不匹配的响应继续等而不是立刻失败。
/// </remarks>
public sealed class G3mDevice : IDisposable
{
    private const int ReadSliceMilliseconds = 100;

    private readonly HidDeviceSession _session;
    private bool _disposed;

    private G3mDevice(HidDeviceSession session, HidInterfaceInfo info)
    {
        _session = session;
        Interface = info;
    }

    /// <summary>当前会话对应的设备接口信息。</summary>
    public HidInterfaceInfo Interface { get; }

    /// <summary>连接方式。</summary>
    public G3mConnectionKind Connection => Interface.Connection;

    /// <summary>
    /// 打开第一个可用的 G3M Pro 厂商集合（有线优先）。
    /// 未找到设备时返回 null；找到但打开失败时抛出 <see cref="G3mProtocolException"/>。
    /// </summary>
    public static G3mDevice? OpenFirst()
    {
        IReadOnlyList<HidInterfaceInfo> candidates;
        try
        {
            candidates = HidDeviceEnumerator.EnumerateG3m();
        }
        catch (Win32Exception exception)
        {
            throw new G3mProtocolException(
                G3mErrorKind.IoFailed, "枚举 HID 设备失败：" + exception.Message, exception);
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        G3mProtocolException? lastFailure = null;
        foreach (HidInterfaceInfo candidate in candidates)
        {
            try
            {
                return new G3mDevice(HidDeviceSession.Open(candidate), candidate);
            }
            catch (Win32Exception exception)
            {
                lastFailure = new G3mProtocolException(
                    G3mErrorKind.OpenFailed, "打开设备失败：" + exception.Message, exception);
            }
        }

        throw lastFailure ?? new G3mProtocolException(G3mErrorKind.OpenFailed, "打开设备失败");
    }

    /// <summary>设备当前是否可枚举到（不区分有线或 2.4G）。</summary>
    public static bool IsPresent()
    {
        try
        {
            return HidDeviceEnumerator.EnumerateG3m().Count > 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    /// <summary>发送一条命令并等待匹配的响应。返回完整 64 字节响应。</summary>
    public byte[] Transact(
        byte command, int length = 0, int offset = 0, byte[]? payload = null,
        int timeoutMilliseconds = 1200)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _session.Drain();

        byte[] packet = G3mPacket.Build(command, length, offset, payload);
        int written;
        try
        {
            written = _session.Write(packet);
        }
        catch (Exception exception) when (exception is TimeoutException or Win32Exception or IOException)
        {
            throw new G3mProtocolException(
                G3mErrorKind.IoFailed, "发送 HID 报文失败：" + exception.Message, exception);
        }

        if (written != G3mCommands.ReportSize)
        {
            throw new G3mProtocolException(
                G3mErrorKind.IoFailed, $"HID 报文写入长度异常：{written}");
        }

        var buffer = new byte[Math.Max(_session.InputReportLength, 128)];
        int elapsed = 0;
        while (elapsed < timeoutMilliseconds)
        {
            int slice = Math.Min(ReadSliceMilliseconds, timeoutMilliseconds - elapsed);
            bool received;
            int count;
            try
            {
                received = _session.TryRead(buffer, slice, out count);
            }
            catch (Exception exception) when (exception is Win32Exception or IOException)
            {
                throw new G3mProtocolException(
                    G3mErrorKind.IoFailed, "读取 HID 响应失败：" + exception.Message, exception);
            }

            elapsed += slice;

            if (!received || count == 0)
            {
                continue;
            }

            if (count >= 4 && buffer[0] == G3mCommands.ReportType &&
                buffer[3] == G3mCommands.ResponseRejected)
            {
                throw new G3mProtocolException(
                    G3mErrorKind.CommandRejected,
                    $"接收器拒绝命令 0x{command:X2}");
            }

            if (count < G3mCommands.ReportSize || buffer[0] != G3mCommands.ReportType ||
                buffer[3] != command)
            {
                // 不匹配的报文属于上一次事务的残留，继续等待本命令的响应。
                continue;
            }

            var response = new byte[G3mCommands.ReportSize];
            Array.Copy(buffer, response, G3mCommands.ReportSize);

            if (response[7] == G3mCommands.StatusCommandError)
            {
                throw new G3mProtocolException(
                    G3mErrorKind.DeviceCommandError, $"设备返回命令错误（0x{command:X2}）");
            }

            if (response[7] == G3mCommands.StatusParameterError)
            {
                throw new G3mProtocolException(
                    G3mErrorKind.DeviceParameterError, $"设备返回参数错误（0x{command:X2}）");
            }

            return response;
        }

        throw new G3mProtocolException(
            G3mErrorKind.Timeout, $"等待命令 0x{command:X2} 响应超时");
    }

    /// <summary>在线探测一次。返回 false 表示接收器在但鼠标本体不在线。</summary>
    public bool Ping()
    {
        byte[] response = Transact(G3mCommands.Ping, timeoutMilliseconds: 350);
        return response[8] == G3mCommands.OnlineMarker;
    }

    /// <summary>
    /// 反复探测直到鼠标在线。接收器插着但鼠标休眠时，需要动一下鼠标才能唤醒。
    /// </summary>
    public void EnsureOnline(int attempts = 8, int delayMilliseconds = 100)
    {
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                if (Ping())
                {
                    return;
                }
            }
            catch (G3mProtocolException exception) when (exception.IsTransient)
            {
                // 单次探测失败不算致命，继续重试。
            }

            if (i < attempts - 1)
            {
                Thread.Sleep(delayMilliseconds);
            }
        }

        throw new G3mProtocolException(
            G3mErrorKind.MouseOffline,
            "接收器已连接，但鼠标未在线；移动鼠标后重试");
    }

    /// <summary>开始配置会话。</summary>
    public void BeginSession() => Transact(G3mCommands.BeginSession);

    /// <summary>结束配置会话。发送前保留原驱动的 10 毫秒提交窗口。</summary>
    public void EndSession()
    {
        Thread.Sleep(G3mCommands.CommitDelay);
        Transact(G3mCommands.EndSession);
    }

    /// <summary>按分块读取一段连续数据。</summary>
    public byte[] ReadChunked(byte command, int totalLength, int offset, int chunkSize)
    {
        ValidateChunk(chunkSize);
        var destination = new byte[totalLength];

        for (int copied = 0; copied < totalLength;)
        {
            int amount = Math.Min(chunkSize, totalLength - copied);
            byte[] response = Transact(command, amount, offset + copied);
            Array.Copy(response, G3mCommands.PayloadOffset, destination, copied, amount);
            copied += amount;
        }

        return destination;
    }

    /// <summary>按分块写入一段连续数据。</summary>
    public void WriteChunked(byte command, byte[] source, int offset, int chunkSize)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateChunk(chunkSize);

        for (int copied = 0; copied < source.Length;)
        {
            int amount = Math.Min(chunkSize, source.Length - copied);
            var payload = new byte[amount];
            Array.Copy(source, copied, payload, 0, amount);

            try
            {
                Transact(command, amount, offset + copied, payload);
            }
            catch (G3mProtocolException exception)
            {
                throw new G3mProtocolException(
                    exception.Kind,
                    $"写入命令 0x{command:X2}（偏移 {offset + copied}）失败：{exception.Message}",
                    exception);
            }

            copied += amount;
        }
    }

    /// <summary>从设备头推导分块大小，非法值回退到协议默认值。</summary>
    public static int ChunkFromHeader(byte[] header34)
    {
        ArgumentNullException.ThrowIfNull(header34);
        if (header34.Length < G3mCommands.HeaderLength)
        {
            return G3mCommands.DefaultChunkSize;
        }

        byte chunk = header34[6];
        return chunk is >= 1 and <= 56 ? chunk : G3mCommands.DefaultChunkSize;
    }

    private static void ValidateChunk(int chunkSize)
    {
        if (chunkSize is < 1 or > 56)
        {
            throw new G3mProtocolException(
                G3mErrorKind.InvalidArgument, $"分块大小 {chunkSize} 超出 1..56");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Dispose();
    }
}
