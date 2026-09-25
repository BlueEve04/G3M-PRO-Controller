using G3M.Core.Hid;
using G3M.Core.Model;
using G3M.Core.Protocol;
using G3M.Core.Storage;

namespace G3M.Core.Services;

/// <summary>一次电量读取的结果。失败时 <see cref="Error"/> 是可以直接展示的中文原因。</summary>
/// <param name="State">成功时的电量状态。</param>
/// <param name="Error">失败原因。</param>
/// <param name="Present">当时设备是否可枚举到。</param>
public sealed record BatteryReadResult(BatteryState? State, string? Error, bool Present)
{
    /// <summary>是否读取成功。</summary>
    public bool Success => State is not null;
}

/// <summary>
/// 面向界面的一层：把原始命令组装成"读快照 / 写配置 / 写按键 / 还原"这些完整用例。
/// </summary>
/// <remarks>
/// 实例是短生命周期的——每次操作重新枚举并打开设备，用完即关。
/// 这样拔插接收器或切换有线/2.4G 之后不需要任何恢复逻辑，下一次操作自然就用新设备。
/// 设备以共享方式打开，因此托盘进程和设置进程可以同时各持一个实例。
/// </remarks>
public sealed class G3mController : IDisposable
{
    private readonly G3mDevice _device;
    private bool _disposed;

    private G3mController(G3mDevice device) => _device = device;

    /// <summary>当前连接方式。</summary>
    public G3mConnectionKind Connection => _device.Connection;

    /// <summary>当前连接方式的中文名称。</summary>
    public string ConnectionName => _device.Interface.ConnectionName;

    /// <summary>打开第一个可用设备；未找到时返回 null。</summary>
    public static G3mController? Open()
    {
        G3mDevice? device = G3mDevice.OpenFirst();
        return device is null ? null : new G3mController(device);
    }

    /// <summary>
    /// 只读一次电量。这是托盘每 5 秒轮询走的路径，因此刻意不做事先的在线探测：
    /// 直接发电量查询，省掉一次往返。行为与已验证的 <c>g3m-battery</c> 一致。
    /// </summary>
    public static BatteryReadResult ReadBatteryOnce()
    {
        try
        {
            using G3mDevice? device = G3mDevice.OpenFirst();
            if (device is null)
            {
                return new BatteryReadResult(null, "未找到 G3M Pro 设备", Present: false);
            }

            byte[] response = device.Transact(
                G3mCommands.QueryBattery, G3mCommands.BatterySubCommand);

            if (response[8] > 100)
            {
                return new BatteryReadResult(
                    null, $"设备返回了无效电量：{response[8]}", Present: true);
            }

            BatteryState state = BatteryState.FromDevice(
                response[8], response[9], device.Connection, DateTimeOffset.Now);

            return new BatteryReadResult(state, null, Present: true);
        }
        catch (G3mProtocolException exception)
        {
            return new BatteryReadResult(null, exception.Message, G3mDevice.IsPresent());
        }
    }

    /// <summary>
    /// 完整读取一次设备状态：设备头、板载配置、配置编号、按键映射表、电量。
    /// 电量读取失败不会让整个快照失败，此时 <see cref="DeviceSnapshot.Battery"/> 为 null。
    /// </summary>
    public DeviceSnapshot ReadSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _device.EnsureOnline();
        _device.BeginSession();

        byte[] headerBytes;
        byte[]? profileBytes = null;
        KeyMappingTable? keyMapping = null;
        byte profileIndex = 0;
        BatteryState? battery = null;
        string? batteryError = null;
        int chunk = G3mCommands.DefaultChunkSize;

        try
        {
            // 设备头必须先用默认分块读，因为分块大小本身就存在里面。
            headerBytes = _device.ReadChunked(
                G3mCommands.ReadHeader, G3mCommands.HeaderLength, 0, G3mCommands.DefaultChunkSize);

            var header = new DeviceHeader(headerBytes);
            chunk = header.ChunkSize;

            int slots = header.KeySlotCount;
            if (slots is < 1 or > KeyMappingTable.MaxSlots)
            {
                throw new G3mProtocolException(
                    G3mErrorKind.DeviceParameterError, $"设备返回了无效的按键槽位数：{slots}");
            }

            try
            {
                byte[] batteryResponse = _device.Transact(
                    G3mCommands.QueryBattery, G3mCommands.BatterySubCommand);

                if (batteryResponse[8] > 100)
                {
                    batteryError = $"设备返回了无效电量：{batteryResponse[8]}";
                }
                else
                {
                    battery = BatteryState.FromDevice(
                        batteryResponse[8], batteryResponse[9], Connection, DateTimeOffset.Now);
                }
            }
            catch (G3mProtocolException exception)
            {
                batteryError = exception.Message;
            }

            byte[] indexBytes = _device.ReadChunked(G3mCommands.ReadProfile, 1, 0, chunk);
            profileIndex = indexBytes[0];

            profileBytes = _device.ReadChunked(
                G3mCommands.ReadProfile, G3mCommands.ProfileLength,
                G3mCommands.ProfileStride * profileIndex, chunk);

            byte[] mappingBytes = _device.ReadChunked(
                G3mCommands.ReadKeyMapping, slots * G3mCommands.KeySlotSize, 0, chunk);
            keyMapping = new KeyMappingTable(mappingBytes);

            var profile = new MouseProfile(profileBytes);
            return new DeviceSnapshot(
                Connection, header, profile, profileIndex, keyMapping, battery, batteryError);
        }
        finally
        {
            // 会话必须结束，否则设备会停留在配置模式。
            TryEndSession();
        }
    }

    /// <summary>
    /// 写入整份板载配置，写入前备份、写入后逐字节读回校验。
    /// 校验不通过时抛 <see cref="G3mErrorKind.VerifyFailed"/>，调用方可以据此提示用户还原。
    /// </summary>
    /// <param name="profile">要写入的配置。</param>
    /// <param name="profileIndex">配置编号。</param>
    /// <param name="backupSource">写入前用于备份的当前快照。</param>
    /// <returns>读回校验通过后的实际板载内容。</returns>
    public MouseProfile WriteProfile(
        MouseProfile profile, byte profileIndex, DeviceSnapshot backupSource)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(backupSource);

        SaveBackup(backupSource);

        byte[] payload = profile.ToArray();

        _device.EnsureOnline();
        _device.BeginSession();
        try
        {
            byte[] headerBytes = _device.ReadChunked(
                G3mCommands.ReadHeader, G3mCommands.HeaderLength, 0, G3mCommands.DefaultChunkSize);
            int chunk = G3mDevice.ChunkFromHeader(headerBytes);

            _device.WriteChunked(
                G3mCommands.WriteProfile, payload, G3mCommands.ProfileStride * profileIndex, chunk);
        }
        finally
        {
            TryEndSession();
        }

        // 必须重新读取并逐字节比对。设备对写入的应答不代表内容真的落盘了。
        DeviceSnapshot verified = ReadSnapshot();
        if (verified.ProfileIndex != profileIndex)
        {
            throw new G3mProtocolException(
                G3mErrorKind.VerifyFailed,
                $"写入后配置编号变成了 {verified.ProfileNumber}，期望 {profileIndex + 1}");
        }

        if (!verified.Profile.AsSpan().SequenceEqual(payload))
        {
            throw new G3mProtocolException(
                G3mErrorKind.VerifyFailed, "板载配置写入后读回内容与写入内容不一致");
        }

        return verified.Profile;
    }

    /// <summary>
    /// 切换当前 DPI 档位。设备对 <c>0x28</c> 即使在无效固件上也会应答，
    /// 因此这里发完运行期命令后还要把档位写进配置并读回确认，才算真正成功。
    /// </summary>
    public DeviceSnapshot SetActiveStage(int stage, DeviceSnapshot current)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(current);

        if (stage < 0 || stage >= current.Header.StageCount)
        {
            throw new G3mProtocolException(
                G3mErrorKind.InvalidArgument, $"档位 {stage + 1} 超出设备支持的 1..{current.Header.StageCount}");
        }

        _device.EnsureOnline();
        if (!RunRuntimeCommand(G3mCommands.SetActiveStage, (byte)stage))
        {
            throw new G3mProtocolException(
                G3mErrorKind.CommandRejected, $"切换到档位 {stage + 1} 失败");
        }

        MouseProfile updated = current.Profile.Clone();
        updated.ActiveStage = stage;
        MouseProfile readback = WriteProfile(updated, current.ProfileIndex, current);

        if (readback.ActiveStage != stage)
        {
            throw new G3mProtocolException(
                G3mErrorKind.VerifyFailed, $"档位写入后读回为 {readback.ActiveStage + 1}，期望 {stage + 1}");
        }

        return ReadSnapshot();
    }

    /// <summary>设置回报率：先发运行期命令立刻生效，再写进板载配置持久化。</summary>
    public DeviceSnapshot SetPollingRate(int rateIndex, DeviceSnapshot current)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(current);

        if (rateIndex < 0 || rateIndex >= G3mCommands.PollingRates.Length)
        {
            throw new G3mProtocolException(
                G3mErrorKind.InvalidArgument, $"回报率档位 {rateIndex} 超出 0..{G3mCommands.PollingRates.Length - 1}");
        }

        _device.EnsureOnline();
        if (!RunRuntimeCommand(G3mCommands.SetPollingRate, (byte)rateIndex))
        {
            throw new G3mProtocolException(
                G3mErrorKind.CommandRejected, $"设置回报率 {G3mCommands.PollingRates[rateIndex]}Hz 失败");
        }

        MouseProfile updated = current.Profile.Clone();
        updated.PollingRateIndex = rateIndex;
        WriteProfile(updated, current.ProfileIndex, current);
        return ReadSnapshot();
    }

    /// <summary>
    /// 写入按键映射表。优先走 <c>0x0B</c>（不需要会话的易失写入），
    /// 部分固件不支持时回退到 <c>0x09</c>（会话内持久写入）。
    /// 两条路径都以读回比对作为成功判据。
    /// </summary>
    /// <remarks>
    /// <b>在 320F:706E 接收器上这条路径不工作。</b>实测结论：
    /// <c>0x0B</c> 被固件直接拒绝；<c>0x09</c> 与 <c>0x08</c> 会被接受、
    /// 响应格式与真正生效的 <c>0x06</c> 完全一致，但映射表内容毫无变化；
    /// 在同一会话里追加一次配置写入试图触发整体提交也无效。
    /// 因此界面目前只提供读取。代码保留是为了在别的固件版本上仍可使用，
    /// 调用方必须处理 <see cref="G3mErrorKind.VerifyFailed"/>。
    /// </remarks>
    public KeyMappingTable WriteKeyMapping(KeyMappingTable table, DeviceSnapshot current)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(current);

        SaveBackup(current);

        if (table.SlotCount != current.KeyMapping.SlotCount)
        {
            throw new G3mProtocolException(
                G3mErrorKind.InvalidArgument,
                $"映射表槽位数 {table.SlotCount} 与设备当前的 {current.KeyMapping.SlotCount} 不一致");
        }

        byte[] payload = table.ToArray();

        _device.EnsureOnline();

        // 路径一：0x0B 易失写入，原驱动的实时预览通道，不包会话。
        try
        {
            _device.WriteChunked(G3mCommands.WriteKeyMappingLive, payload, 0, current.Header.ChunkSize);
            KeyMappingTable live = ReadKeyMapping(current.Header);
            if (live.ContentEquals(table))
            {
                return live;
            }
        }
        catch (G3mProtocolException)
        {
            // 该固件不支持易失通道，走持久通道。
        }

        // 路径二：0x09 持久写入，必须包在会话里。
        _device.BeginSession();
        try
        {
            byte[] headerBytes = _device.ReadChunked(
                G3mCommands.ReadHeader, G3mCommands.HeaderLength, 0, G3mCommands.DefaultChunkSize);
            int chunk = G3mDevice.ChunkFromHeader(headerBytes);

            _device.WriteChunked(G3mCommands.WriteKeyMappingPersistent, payload, 0, chunk);
        }
        finally
        {
            TryEndSession();
        }

        KeyMappingTable readback = ReadKeyMapping(current.Header);
        if (!readback.ContentEquals(table))
        {
            throw new G3mProtocolException(
                G3mErrorKind.VerifyFailed, "按键映射写入后读回内容与写入内容不一致");
        }

        return readback;
    }

    /// <summary>只读按键映射表。</summary>
    public KeyMappingTable ReadKeyMapping(DeviceHeader header)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(header);

        _device.EnsureOnline();
        _device.BeginSession();
        try
        {
            int slots = header.KeySlotCount;
            if (slots is < 1 or > KeyMappingTable.MaxSlots)
            {
                throw new G3mProtocolException(
                    G3mErrorKind.DeviceParameterError, $"设备返回了无效的按键槽位数：{slots}");
            }

            byte[] mappingBytes = _device.ReadChunked(
                G3mCommands.ReadKeyMapping, slots * G3mCommands.KeySlotSize, 0, header.ChunkSize);
            return new KeyMappingTable(mappingBytes);
        }
        finally
        {
            TryEndSession();
        }
    }

    /// <summary>用一份备份整体还原配置和按键映射，并逐项读回校验。</summary>
    public DeviceSnapshot Restore(DeviceBackup backup)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(backup);

        DeviceSnapshot current = ReadSnapshot();

        MouseProfile restored = WriteProfile(
            backup.ToProfile(), backup.ProfileIndex, current);

        DeviceSnapshot afterProfile = ReadSnapshot();
        KeyMappingTable restoredKeys = WriteKeyMapping(backup.ToKeyMapping(), afterProfile);

        if (!restored.AsSpan().SequenceEqual(backup.ProfileBytes))
        {
            throw new G3mProtocolException(
                G3mErrorKind.VerifyFailed, "还原后板载配置与备份内容不一致");
        }

        if (!restoredKeys.AsSpan().SequenceEqual(backup.KeyMappingBytes))
        {
            throw new G3mProtocolException(
                G3mErrorKind.VerifyFailed, "还原后按键映射与备份内容不一致");
        }

        return ReadSnapshot();
    }

    /// <summary>保存一份备份，返回落盘路径；失败返回 null（备份失败不应中断主流程，但调用方应记录）。</summary>
    public static string? SaveBackup(DeviceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var backup = new DeviceBackup(
            DateTimeOffset.Now,
            snapshot.ConnectionName,
            snapshot.ProfileIndex,
            snapshot.Profile.ToArray(),
            snapshot.KeyMapping.ToArray());

        return DeviceBackupStore.Save(backup);
    }

    /// <summary>发一条运行期命令，包在会话里。返回设备是否正常应答。</summary>
    private bool RunRuntimeCommand(byte command, byte value)
    {
        _device.BeginSession();
        try
        {
            _device.Transact(command, 1, 0, [value]);
            return true;
        }
        catch (G3mProtocolException)
        {
            return false;
        }
        finally
        {
            TryEndSession();
        }
    }

    private void TryEndSession()
    {
        try
        {
            _device.EndSession();
        }
        catch (G3mProtocolException)
        {
            // 设备可能已经被拔出；会话结束失败不应覆盖真正的失败原因。
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _device.Dispose();
    }
}
