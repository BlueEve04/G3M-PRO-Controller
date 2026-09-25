using System.Text;
using G3M.Core.Hid;
using G3M.Core.Keys;
using G3M.Core.Model;
using G3M.Core.Protocol;
using G3M.Core.Services;
using G3M.Core.Storage;

// 命令行诊断工具：不依赖 GUI，直接把设备原始状态打印出来。
// GUI 行为异常时用它对照，可以快速判断问题出在协议层还是界面层。
// 只读，不发送任何写入命令。

Console.OutputEncoding = Encoding.UTF8;

string command = args.Length > 0 ? args[0].ToLowerInvariant() : "snapshot";

return command switch
{
    "interfaces" => ListInterfaces(),
    "battery" => ReadBattery(),
    "snapshot" => ReadSnapshot(),
    "keymap" => ReadKeyMapping(),

    // 写入路径的验证命令。全部会先自动备份，写入后逐字节读回校验。
    "write-identity" => WriteIdentity(),
    "set-dpi" => SetDpi(args),
    "set-key" => SetKey(args),
    "debug-key" => DebugKey(args),
    "try-write" => TryWrite(args),
    "debug-profile-write" => DebugProfileWrite(),
    "set-key-commit" => SetKeyCommit(args),
    "restore" => Restore(),
    _ => Usage(),
};

/// <summary>
/// 在同一个会话里先写按键表（0x09），再写一次板载配置（0x06），最后结束会话。
/// 依据是：配置区写入需要 0x01/0x02 包裹才落盘，按键表可能也要靠同一次
/// 配置写入把整个区块一起提交，单独写按键表只会留在暂存区。
/// </summary>
static int SetKeyCommit(string[] arguments)
{
    if (arguments.Length < 5 ||
        !int.TryParse(arguments[1], out int slot) ||
        !byte.TryParse(arguments[2], System.Globalization.NumberStyles.HexNumber, null, out byte b0) ||
        !byte.TryParse(arguments[3], System.Globalization.NumberStyles.HexNumber, null, out byte b1) ||
        !byte.TryParse(arguments[4], System.Globalization.NumberStyles.HexNumber, null, out byte b2))
    {
        Console.WriteLine("用法：set-key-commit <槽位> <b0> <b1> <b2>");
        return 2;
    }

    using G3mController? controller = G3mController.Open();
    if (controller is null)
    {
        Console.WriteLine("未找到 G3M Pro 设备。");
        return 1;
    }

    DeviceSnapshot baseline = controller.ReadSnapshot();
    var target = new KeyAction(b0, b1, b2);
    Console.WriteLine($"槽位 {slot}：{baseline.KeyMapping.GetSlot(slot).Describe()} -> {target.Describe()}");

    byte[] keyPayload = baseline.KeyMapping.ToArray();
    keyPayload[slot * 3] = b0;
    keyPayload[(slot * 3) + 1] = b1;
    keyPayload[(slot * 3) + 2] = b2;

    byte[] profilePayload = baseline.Profile.ToArray();
    ushort profileOffset = (ushort)(G3mCommands.ProfileStride * baseline.ProfileIndex);

    using G3mDevice? device = G3mDevice.OpenFirst()!;
    device.EnsureOnline();
    device.BeginSession();
    try
    {
        byte[] header = device.ReadChunked(
            G3mCommands.ReadHeader, G3mCommands.HeaderLength, 0, G3mCommands.DefaultChunkSize);
        int chunk = G3mDevice.ChunkFromHeader(header);

        SendChunked(device, G3mCommands.WriteKeyMappingPersistent, keyPayload, 0, chunk);
        Console.WriteLine("  按键表已发送");

        SendChunked(device, G3mCommands.WriteProfile, profilePayload, profileOffset, chunk);
        Console.WriteLine("  板载配置已发送（内容未改动，仅用于触发提交）");
    }
    finally
    {
        try
        {
            device.EndSession();
            Console.WriteLine("  会话已结束");
        }
        catch (G3mProtocolException exception)
        {
            Console.WriteLine($"  结束会话失败：{exception.Message}");
        }
    }

    DeviceSnapshot after = controller.ReadSnapshot();
    KeyAction result = after.KeyMapping.GetSlot(slot);
    Console.WriteLine($"  读回：{result.Describe()} ({result.Byte0:X2} {result.Byte1:X2} {result.Byte2:X2})");
    Console.WriteLine(result == target ? "  >>> 生效" : "  >>> 未生效");
    return result == target ? 0 : 1;
}

static void SendChunked(G3mDevice device, byte command, byte[] payload, ushort offset, int chunk)
{
    for (int copied = 0; copied < payload.Length; copied += chunk)
    {
        int amount = Math.Min(chunk, payload.Length - copied);
        var piece = new byte[amount];
        Array.Copy(payload, copied, piece, 0, amount);
        device.Transact(command, amount, offset + copied, piece);
    }
}

/// <summary>
/// 用原样内容走一次已知可用的配置写入（0x06），把每个分块的原始响应打出来。
/// 目的是取得一个「确定成功的写命令」的响应样本作为对照：
/// 如果它也回显旧数据，那"回显"就不能用来判断一条命令是读还是写。
/// 内容与原值完全相同，因此没有任何副作用。
/// </summary>
static int DebugProfileWrite()
{
    using G3mController? controller = G3mController.Open();
    if (controller is null)
    {
        Console.WriteLine("未找到 G3M Pro 设备。");
        return 1;
    }

    DeviceSnapshot snapshot = controller.ReadSnapshot();
    byte[] payload = snapshot.Profile.ToArray();
    ushort offset = (ushort)(G3mCommands.ProfileStride * snapshot.ProfileIndex);

    Console.WriteLine($"配置编号 {snapshot.ProfileNumber}，写入偏移 {offset}，长度 {payload.Length}");
    Console.WriteLine("内容与原值完全相同，仅用于观察响应格式。");
    Console.WriteLine();

    using G3mDevice? device = G3mDevice.OpenFirst()!;
    device.EnsureOnline();
    device.BeginSession();
    try
    {
        byte[] header = device.ReadChunked(
            G3mCommands.ReadHeader, G3mCommands.HeaderLength, 0, G3mCommands.DefaultChunkSize);
        int chunk = G3mDevice.ChunkFromHeader(header);

        for (int copied = 0; copied < payload.Length; copied += chunk)
        {
            int amount = Math.Min(chunk, payload.Length - copied);
            var piece = new byte[amount];
            Array.Copy(payload, copied, piece, 0, amount);

            byte[] response = device.Transact(
                G3mCommands.WriteProfile, amount, offset + copied, piece);

            Console.WriteLine(
                $"  分块 offset={offset + copied,3} len={amount,2} -> " +
                $"响应[3..7]={response[3]:X2} {response[4]:X2} {response[5]:X2} {response[6]:X2} {response[7]:X2} " +
                $"数据[8..]={Convert.ToHexString(response[8..Math.Min(28, response.Length)])}");
        }
    }
    finally
    {
        try
        {
            device.EndSession();
            Console.WriteLine("  会话已结束");
        }
        catch (G3mProtocolException exception)
        {
            Console.WriteLine($"  结束会话失败：{exception.Message}");
        }
    }

    return 0;
}

/// <summary>
/// 用指定命令字尝试写一个槽位，然后读回判断是否真的生效。
/// 用来在不知道正确写命令时逐条试出来——协议里读写命令成对出现
/// （0x05 读配置 / 0x06 写配置），所以按键很可能是 0x07 读 / 0x08 写。
/// </summary>
static int TryWrite(string[] arguments)
{
    if (arguments.Length < 6 ||
        !byte.TryParse(arguments[1], System.Globalization.NumberStyles.HexNumber, null, out byte cmd) ||
        !int.TryParse(arguments[2], out int slot) ||
        !byte.TryParse(arguments[3], System.Globalization.NumberStyles.HexNumber, null, out byte b0) ||
        !byte.TryParse(arguments[4], System.Globalization.NumberStyles.HexNumber, null, out byte b1) ||
        !byte.TryParse(arguments[5], System.Globalization.NumberStyles.HexNumber, null, out byte b2))
    {
        Console.WriteLine("用法：try-write <命令字十六进制> <槽位> <b0> <b1> <b2>");
        Console.WriteLine("例如：try-write 08 6 20 00 73");
        return 2;
    }

    using G3mController? controller = G3mController.Open();
    if (controller is null)
    {
        Console.WriteLine("未找到 G3M Pro 设备。");
        return 1;
    }

    DeviceSnapshot baseline = controller.ReadSnapshot();
    KeyAction original = baseline.KeyMapping.GetSlot(slot);
    var target = new KeyAction(b0, b1, b2);

    Console.WriteLine($"命令字 0x{cmd:X2}，槽位 {slot}：{original.Describe()} -> {target.Describe()}");

    byte[] payload = baseline.KeyMapping.ToArray();
    payload[slot * 3] = b0;
    payload[(slot * 3) + 1] = b1;
    payload[(slot * 3) + 2] = b2;

    try
    {
        using G3mDevice? device = G3mDevice.OpenFirst()!;
        device.EnsureOnline();
        device.BeginSession();
        try
        {
            byte[] header = device.ReadChunked(
                G3mCommands.ReadHeader, G3mCommands.HeaderLength, 0, G3mCommands.DefaultChunkSize);
            int chunk = G3mDevice.ChunkFromHeader(header);

            for (int copied = 0; copied < payload.Length; copied += chunk)
            {
                int amount = Math.Min(chunk, payload.Length - copied);
                var piece = new byte[amount];
                Array.Copy(payload, copied, piece, 0, amount);
                device.Transact(cmd, amount, copied, piece);
            }

            Console.WriteLine("  写入命令全部被接受");
        }
        finally
        {
            try
            {
                device.EndSession();
            }
            catch (G3mProtocolException exception)
            {
                Console.WriteLine($"  结束会话失败：{exception.Message}");
            }
        }
    }
    catch (G3mProtocolException exception)
    {
        Console.WriteLine($"  写入失败：{exception.Kind} - {exception.Message}");
        return 1;
    }

    DeviceSnapshot after = controller.ReadSnapshot();
    KeyAction result = after.KeyMapping.GetSlot(slot);
    Console.WriteLine($"  读回：{result.Describe()} ({result.Byte0:X2} {result.Byte1:X2} {result.Byte2:X2})");
    Console.WriteLine(result == target ? "  >>> 生效" : "  >>> 未生效");
    return result == target ? 0 : 1;
}

/// <summary>
/// 逐条排查按键映射的写入通道。分别单独尝试 0x0B（易失，无会话）与
/// 0x09（持久，需会话），把每一步的原始结果与异常都打出来，
/// 用来确定这台固件到底认哪一条路，而不是靠猜。
/// </summary>
static int DebugKey(string[] arguments)
{
    if (arguments.Length < 5 ||
        !int.TryParse(arguments[1], out int slot) ||
        !byte.TryParse(arguments[2], System.Globalization.NumberStyles.HexNumber, null, out byte b0) ||
        !byte.TryParse(arguments[3], System.Globalization.NumberStyles.HexNumber, null, out byte b1) ||
        !byte.TryParse(arguments[4], System.Globalization.NumberStyles.HexNumber, null, out byte b2))
    {
        Console.WriteLine("用法：debug-key <槽位> <b0> <b1> <b2>");
        return 2;
    }

    using G3mController? controller = G3mController.Open();
    if (controller is null)
    {
        Console.WriteLine("未找到 G3M Pro 设备。");
        return 1;
    }

    DeviceSnapshot baseline = controller.ReadSnapshot();
    int slotCount = baseline.KeyMapping.SlotCount;
    int chunk = baseline.Header.ChunkSize;
    int length = slotCount * 3;

    Console.WriteLine($"槽位 {slot} 当前：{baseline.KeyMapping.GetSlot(slot).Describe()}");
    Console.WriteLine($"槽位数 {slotCount}，分块 {chunk}，映射表长度 {length} 字节");
    Console.WriteLine();

    byte[] payload = baseline.KeyMapping.ToArray();
    payload[slot * 3] = b0;
    payload[(slot * 3) + 1] = b1;
    payload[(slot * 3) + 2] = b2;

    // --- 通道一：0x0B 易失写入，不包会话 ---
    Console.WriteLine("--- 通道 0x0B（易失，无会话）---");
    try
    {
        using G3mDevice? device = G3mDevice.OpenFirst()!;
        device.EnsureOnline();
        device.WriteChunked(G3mCommands.WriteKeyMappingLive, payload, 0, chunk);
        Console.WriteLine("  写入被设备接受");

        byte[] after = device.ReadChunked(G3mCommands.ReadKeyMapping, length, 0, chunk);
        Console.WriteLine($"  读回槽位 {slot}：{new KeyAction(after[slot * 3], after[(slot * 3) + 1], after[(slot * 3) + 2]).Describe()}");
    }
    catch (G3mProtocolException exception)
    {
        Console.WriteLine($"  失败：{exception.Kind} - {exception.Message}");
    }

    Console.WriteLine();

    // --- 通道二：0x09 持久写入，包在会话里 ---
    Console.WriteLine("--- 通道 0x09（持久，会话内）---");
    try
    {
        using G3mDevice? device = G3mDevice.OpenFirst()!;
        device.EnsureOnline();
        device.BeginSession();
        try
        {
            byte[] header = device.ReadChunked(
                G3mCommands.ReadHeader, G3mCommands.HeaderLength, 0, G3mCommands.DefaultChunkSize);
            Console.WriteLine($"  设备头读回：{Convert.ToHexString(header)}");

            // 手动分块发送，把每个分块的原始响应都打出来。
            for (int copied = 0; copied < payload.Length; copied += chunk)
            {
                int amount = Math.Min(chunk, payload.Length - copied);
                var piece = new byte[amount];
                Array.Copy(payload, copied, piece, 0, amount);

                byte[] response = device.Transact(
                    G3mCommands.WriteKeyMappingPersistent, amount, copied, piece);

                Console.WriteLine(
                    $"  分块 offset={copied,3} len={amount,2} -> " +
                    $"响应头 {response[3]:X2} {response[4]:X2} {response[5]:X2} {response[6]:X2} " +
                    $"状态[7]={response[7]:X2} 数据[8..]={Convert.ToHexString(response[8..20])}");
            }
        }
        finally
        {
            try
            {
                device.EndSession();
                Console.WriteLine("  会话已结束");
            }
            catch (G3mProtocolException exception)
            {
                Console.WriteLine($"  结束会话失败：{exception.Message}");
            }
        }

        byte[] after = device.ReadChunked(G3mCommands.ReadKeyMapping, length, 0, chunk);
        Console.WriteLine($"  读回槽位 {slot}：{new KeyAction(after[slot * 3], after[(slot * 3) + 1], after[(slot * 3) + 2]).Describe()}");
    }
    catch (G3mProtocolException exception)
    {
        Console.WriteLine($"  失败：{exception.Kind} - {exception.Message}");
    }

    Console.WriteLine();

    // --- 最终状态 ---
    DeviceSnapshot final = controller.ReadSnapshot();
    KeyAction result = final.KeyMapping.GetSlot(slot);
    Console.WriteLine($"最终设备状态：槽位 {slot} = {result.Describe()} ({result.Byte0:X2} {result.Byte1:X2} {result.Byte2:X2})");
    Console.WriteLine(result == new KeyAction(b0, b1, b2)
        ? "结论：写入生效。"
        : "结论：写入未生效，两条通道都没能改动映射表。");

    return 0;
}

static int Usage()
{
    Console.WriteLine("""
        用法：g3m-probe <命令>

        只读命令：
          interfaces        列出匹配的 HID 接口
          battery           只读一次电量
          snapshot          完整打印设备头、配置、按键映射、电量
          keymap            只打印按键映射表

        写入命令（每次写入前自动备份，写入后逐字节读回校验）：
          write-identity    把当前配置原样写回，用来验证写入+校验链路本身
          set-dpi <档位> <DPI>        修改单个档位的 DPI，档位取 0-6
          set-key <槽位> <b0> <b1> <b2>  修改单个槽位的按键映射，字节为十六进制
          restore           用最近一次自动备份整体还原
        """);
    return 2;
}

/// <summary>
/// 把读到的配置原样写回。这是写入链路最干净的验证：
/// 内容一个字节都不变，如果连它都校验失败，说明校验策略有问题（例如固件会自行改动某些字节）。
/// </summary>
static int WriteIdentity()
{
    using G3mController? controller = G3mController.Open();
    if (controller is null)
    {
        Console.WriteLine("未找到 G3M Pro 设备。");
        return 1;
    }

    DeviceSnapshot before = controller.ReadSnapshot();
    Console.WriteLine("写入前配置：");
    Console.WriteLine(HexDump(before.Profile.ToArray()));

    try
    {
        MouseProfile readback = controller.WriteProfile(
            before.Profile.Clone(), before.ProfileIndex, before);

        bool identical = readback.AsSpan().SequenceEqual(before.Profile.AsSpan());
        Console.WriteLine();
        Console.WriteLine("写入后读回：");
        Console.WriteLine(HexDump(readback.ToArray()));
        Console.WriteLine();
        Console.WriteLine(identical
            ? "结果：原样写回成功，逐字节校验通过。写入链路可用。"
            : "结果：读回内容与写入内容不一致，需要检查校验策略。");

        if (!identical)
        {
            PrintDiff(before.Profile.ToArray(), readback.ToArray());
            return 1;
        }

        return 0;
    }
    catch (G3mProtocolException exception)
    {
        Console.WriteLine($"写入失败：{exception.Message}");
        return 1;
    }
}

static int SetDpi(string[] arguments)
{
    if (arguments.Length < 3 ||
        !int.TryParse(arguments[1], out int stage) ||
        !int.TryParse(arguments[2], out int dpi))
    {
        Console.WriteLine("用法：set-dpi <档位 0-6> <DPI>");
        return 2;
    }

    using G3mController? controller = G3mController.Open();
    if (controller is null)
    {
        Console.WriteLine("未找到 G3M Pro 设备。");
        return 1;
    }

    DeviceSnapshot before = controller.ReadSnapshot();
    if (stage < 0 || stage >= before.Header.StageCount)
    {
        Console.WriteLine($"档位 {stage} 超出设备支持的 0..{before.Header.StageCount - 1}");
        return 2;
    }

    int oldDpi = before.Profile.GetStageDpi(before.Header, stage);
    MouseProfile draft = before.Profile.Clone();
    draft.SetStageDpi(before.Header, stage, dpi);
    ushort encoded = DpiCodec.Encode(before.Header, dpi);

    Console.WriteLine($"档位 {stage + 1}：{oldDpi} -> {dpi} DPI（原始码值 {encoded}）");

    try
    {
        MouseProfile readback = controller.WriteProfile(draft, before.ProfileIndex, before);
        int actual = DpiCodec.Decode(before.Header, readback.GetStage(stage).RawDpi);
        Console.WriteLine($"写入成功，读回值：{actual} DPI");
        return 0;
    }
    catch (G3mProtocolException exception)
    {
        Console.WriteLine($"写入失败：{exception.Message}");
        return 1;
    }
}

static int SetKey(string[] arguments)
{
    if (arguments.Length < 5 ||
        !int.TryParse(arguments[1], out int slot) ||
        !byte.TryParse(arguments[2], System.Globalization.NumberStyles.HexNumber, null, out byte b0) ||
        !byte.TryParse(arguments[3], System.Globalization.NumberStyles.HexNumber, null, out byte b1) ||
        !byte.TryParse(arguments[4], System.Globalization.NumberStyles.HexNumber, null, out byte b2))
    {
        Console.WriteLine("用法：set-key <槽位> <b0> <b1> <b2>   例如：set-key 3 20 00 73");
        return 2;
    }

    using G3mController? controller = G3mController.Open();
    if (controller is null)
    {
        Console.WriteLine("未找到 G3M Pro 设备。");
        return 1;
    }

    DeviceSnapshot before = controller.ReadSnapshot();
    if (slot < 0 || slot >= before.KeyMapping.SlotCount)
    {
        Console.WriteLine($"槽位 {slot} 超出 0..{before.KeyMapping.SlotCount - 1}");
        return 2;
    }

    KeyAction old = before.KeyMapping.GetSlot(slot);
    var updated = new KeyAction(b0, b1, b2);
    Console.WriteLine($"槽位 {slot}：{old.Describe()} -> {updated.Describe()}");

    KeyMappingTable draft = before.KeyMapping;
    draft.SetSlot(slot, updated);

    try
    {
        KeyMappingTable readback = controller.WriteKeyMapping(draft, before);
        Console.WriteLine($"写入成功，读回：{readback.GetSlot(slot).Describe()}");
        return 0;
    }
    catch (G3mProtocolException exception)
    {
        Console.WriteLine($"写入失败：{exception.Message}");
        return 1;
    }
}

static int Restore()
{
    DeviceBackup? backup = DeviceBackupStore.LoadLatest();
    if (backup is null)
    {
        Console.WriteLine("没有找到可用的备份。");
        return 1;
    }

    Console.WriteLine($"使用备份：{backup.Summary}");

    using G3mController? controller = G3mController.Open();
    if (controller is null)
    {
        Console.WriteLine("未找到 G3M Pro 设备。");
        return 1;
    }

    try
    {
        DeviceSnapshot restored = controller.Restore(backup);
        Console.WriteLine("还原完成，读回校验通过。");
        Console.WriteLine($"当前档位 {restored.ActiveStage + 1}，回报率 {restored.PollingRateHz} Hz");
        return 0;
    }
    catch (G3mProtocolException exception)
    {
        Console.WriteLine($"还原失败：{exception.Message}");
        return 1;
    }
}

/// <summary>列出两份内容所有不同的字节偏移，用于诊断校验失败。</summary>
static void PrintDiff(byte[] expected, byte[] actual)
{
    Console.WriteLine();
    Console.WriteLine("差异字节：");
    for (int i = 0; i < Math.Min(expected.Length, actual.Length); i++)
    {
        if (expected[i] != actual[i])
        {
            Console.WriteLine($"  [{i,3}] 期望 {expected[i]:X2}，实际 {actual[i]:X2}");
        }
    }
}

static int ListInterfaces()
{
    Console.WriteLine("=== 全部 G3M Pro 厂商集合（usage page 0xFF1C / usage 0x0092）===");
    IReadOnlyList<HidInterfaceInfo> all = HidDeviceEnumerator.EnumerateG3m();

    if (all.Count == 0)
    {
        Console.WriteLine("未找到任何匹配的 HID 集合。");
        Console.WriteLine("请确认鼠标接收器已插好。");
        return 1;
    }

    foreach (HidInterfaceInfo info in all)
    {
        Console.WriteLine(
            $"  {info.ConnectionName}  PID=0x{info.ProductId:X4}  版本=0x{info.VersionNumber:X4}  " +
            $"in={info.InputReportByteLength}  out={info.OutputReportByteLength}");
        Console.WriteLine($"      {info.DevicePath}");
    }

    Console.WriteLine();
    Console.WriteLine($"共 {all.Count} 个接口，实际使用第一个（有线优先）。");
    return 0;
}

static int ReadBattery()
{
    BatteryReadResult result = G3mController.ReadBatteryOnce();

    if (!result.Success)
    {
        Console.WriteLine($"电量读取失败：{result.Error}");
        Console.WriteLine($"设备是否可枚举到：{(result.Present ? "是" : "否")}");
        return 1;
    }

    BatteryState state = result.State!;
    Console.WriteLine($"电量      : {state.Percent}%");
    Console.WriteLine($"充电状态  : {state.ChargingText}（原始状态字节 0x{state.RawFlag:X2}）");
    Console.WriteLine($"连接方式  : {state.Connection}");
    Console.WriteLine($"读取时刻  : {state.ObservedAt.LocalDateTime:HH:mm:ss}");
    return 0;
}

static int ReadSnapshot()
{
    using G3mController? controller = G3mController.Open();
    if (controller is null)
    {
        Console.WriteLine("未找到 G3M Pro 设备。");
        return 1;
    }

    Console.WriteLine($"=== 已连接：{controller.ConnectionName} ===");
    DeviceSnapshot snapshot = controller.ReadSnapshot();

    DeviceHeader header = snapshot.Header;
    Console.WriteLine();
    Console.WriteLine("--- 设备头 (34 字节) ---");
    Console.WriteLine(HexDump(header.Raw));
    Console.WriteLine($"  按键槽位 slots  = {header.KeySlotCount}");
    Console.WriteLine($"  分块大小 chunk  = {header.ChunkSize}");
    Console.WriteLine($"  传感器型号      = {header.SensorModel} (0x{header.SensorModel:X4})");
    Console.WriteLine($"  DPI 步进        = {header.DpiStep}");
    Console.WriteLine($"  DPI 档位数      = {header.StageCount}");
    Console.WriteLine($"  DPI 基准 base   = {header.DpiBase}");

    Console.WriteLine();
    Console.WriteLine("--- 板载配置 (84 字节) ---");
    Console.WriteLine(HexDump(snapshot.Profile.ToArray()));
    Console.WriteLine($"  配置编号        = {snapshot.ProfileNumber}");
    Console.WriteLine($"  回报率          = {snapshot.PollingRateHz} Hz (索引 {snapshot.Profile.PollingRateIndex})");
    Console.WriteLine($"  当前档位        = {snapshot.ActiveStage + 1}");
    Console.WriteLine($"  直线修正        = {(snapshot.Profile.LineCorrection ? "开" : "关")}");
    Console.WriteLine($"  波纹修正        = {(snapshot.Profile.WaveCorrection ? "开" : "关")}");
    Console.WriteLine($"  按键防抖        = {snapshot.Profile.DebounceMs} ms (索引 {snapshot.Profile.DebounceIndex})");
    Console.WriteLine($"  静默高度        = {snapshot.Profile.LiftHeightMm} mm (索引 {snapshot.Profile.LiftHeightIndex})");

    Console.WriteLine();
    Console.WriteLine("  --- DPI 档位 ---");
    for (int stage = 0; stage < G3mCommands.MaxDpiStages; stage++)
    {
        DpiStage record = snapshot.Profile.GetStage(stage);
        int dpi = snapshot.Profile.GetStageDpi(header, stage);
        ushort reencoded = DpiCodec.Encode(header, dpi);
        string roundTrip = reencoded == record.RawDpi ? "往返一致" : $"往返不一致 -> {reencoded}";
        string marker = stage == snapshot.ActiveStage ? " <- 当前" : "";
        Console.WriteLine(
            $"    档位{stage + 1}: {(record.Enabled ? "启用" : "禁用")}  " +
            $"raw=0x{record.RawDpi:X4}  DPI={dpi,5}  RGB={record.ColorHex}  {roundTrip}{marker}");
    }

    Console.WriteLine();
    Console.WriteLine($"--- 电量 ---");
    if (snapshot.Battery is { } battery)
    {
        Console.WriteLine($"  {battery.Percent}%  {battery.ChargingText}（原始状态字节 0x{battery.RawFlag:X2}）");
    }
    else
    {
        Console.WriteLine($"  读取失败：{snapshot.BatteryError}");
    }

    Console.WriteLine();
    Console.WriteLine($"--- 按键映射表（{snapshot.KeyMapping.SlotCount} 槽 × 3 字节）---");
    Console.WriteLine(HexDump(snapshot.KeyMapping.ToArray()));

    for (int slot = 0; slot < snapshot.KeyMapping.SlotCount; slot++)
    {
        KeyAction action = snapshot.KeyMapping.GetSlot(slot);
        Console.WriteLine($"  槽位{slot,3}: {action.Byte0:X2} {action.Byte1:X2} {action.Byte2:X2}  {action.Describe()}");
    }

    Console.WriteLine();
    Console.WriteLine($"出厂默认映射：{(snapshot.KeyMapping.IsFactoryDefault() ? "是" : "否（已被修改）")}");
    return 0;
}

static int ReadKeyMapping()
{
    using G3mController? controller = G3mController.Open();
    if (controller is null)
    {
        Console.WriteLine("未找到 G3M Pro 设备。");
        return 1;
    }

    DeviceSnapshot snapshot = controller.ReadSnapshot();
    Console.WriteLine($"按键映射表（{snapshot.KeyMapping.SlotCount} 槽）：");
    for (int slot = 0; slot < snapshot.KeyMapping.SlotCount; slot++)
    {
        KeyAction action = snapshot.KeyMapping.GetSlot(slot);
        Console.WriteLine($"  槽位{slot,3}: {action.Byte0:X2} {action.Byte1:X2} {action.Byte2:X2}  {action.Describe()}");
    }

    return 0;
}

static string HexDump(byte[] data, int width = 16)
{
    var builder = new StringBuilder();
    for (int start = 0; start < data.Length; start += width)
    {
        int count = Math.Min(width, data.Length - start);
        builder.Append($"  [{start,3}] ");
        for (int i = 0; i < count; i++)
        {
            builder.Append($"{data[start + i]:X2} ");
        }

        builder.AppendLine();
    }

    return builder.ToString().TrimEnd();
}
