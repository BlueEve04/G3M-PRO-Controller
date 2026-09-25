using G3M.Core.Protocol;

namespace G3M.Core.Model;

/// <summary>
/// 84 字节板载配置。只有已确认含义的字段提供读写属性，
/// 其余字节（灯光参数、厂商保留位等）原样保留，写回时不会被改动。
/// </summary>
public sealed class MouseProfile
{
    /// <summary>配置长度。</summary>
    public const int Length = G3mCommands.ProfileLength;

    private const int StageRecordBase = 14;
    private const int StageRecordSize = 9;
    private const int StageEnabledOffset = 0;
    private const int StageRawDpiOffset = 2;
    private const int StageColorOffset = 6;

    private const int PollingRateOffset = 11;
    private const int ActiveStageOffset = 12;
    private const int LineCorrectionOffset = 78;
    private const int DebounceOffset = 79;
    private const int LiftHeightOffset = 80;
    private const int WaveCorrectionOffset = 82;

    /// <summary>按键防抖可选档位，索引与板载值一致。</summary>
    public static readonly int[] DebounceOptionsMs = [4, 6, 8, 10, 12];

    /// <summary>静默高度可选档位，索引与板载值一致。</summary>
    public static readonly int[] LiftHeightOptionsMm = [1, 2, 3];

    private readonly byte[] _raw;

    /// <summary>用一段 84 字节数据构造配置。传入内容会被复制，不会被就地修改。</summary>
    public MouseProfile(byte[] raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length != Length)
        {
            throw new ArgumentException($"板载配置必须为 {Length} 字节，实际 {raw.Length}", nameof(raw));
        }

        _raw = (byte[])raw.Clone();
    }

    /// <summary>构造一份全零配置，主要用于测试和占位。</summary>
    public MouseProfile() : this(new byte[Length])
    {
    }

    /// <summary>导出当前配置的副本，可直接交给写入路径。</summary>
    public byte[] ToArray() => (byte[])_raw.Clone();

    /// <summary>
    /// 复制一份独立的配置。编辑前必须调用——<see cref="MouseProfile"/> 是引用类型，
    /// 直接改快照里的实例会污染原始读回内容，导致后续的写入前备份备份到已被修改的值。
    /// </summary>
    public MouseProfile Clone() => new(_raw);

    /// <summary>读取只读视图，用于比对。</summary>
    public ReadOnlySpan<byte> AsSpan() => _raw;

    /// <summary><c>[11]</c> 回报率档位索引，收敛到 0..4。</summary>
    public int PollingRateIndex
    {
        get => Math.Clamp(_raw[PollingRateOffset], 0, G3mCommands.PollingRates.Length - 1);
        set => _raw[PollingRateOffset] =
            (byte)Math.Clamp(value, 0, G3mCommands.PollingRates.Length - 1);
    }

    /// <summary>当前回报率，单位赫兹。</summary>
    public int PollingRateHz => G3mCommands.PollingRates[PollingRateIndex];

    /// <summary><c>[12]</c> 当前生效的 DPI 档位索引，收敛到 0..6。</summary>
    public int ActiveStage
    {
        get => Math.Clamp((int)_raw[ActiveStageOffset], 0, G3mCommands.MaxDpiStages - 1);
        set => _raw[ActiveStageOffset] =
            (byte)Math.Clamp(value, 0, G3mCommands.MaxDpiStages - 1);
    }

    /// <summary><c>[78]</c> 直线修正开关。</summary>
    public bool LineCorrection
    {
        get => _raw[LineCorrectionOffset] != 0;
        set => _raw[LineCorrectionOffset] = (byte)(value ? 1 : 0);
    }

    /// <summary><c>[82]</c> 波纹修正开关。</summary>
    public bool WaveCorrection
    {
        get => _raw[WaveCorrectionOffset] != 0;
        set => _raw[WaveCorrectionOffset] = (byte)(value ? 1 : 0);
    }

    /// <summary><c>[79]</c> 按键防抖档位索引，收敛到 0..4。</summary>
    public int DebounceIndex
    {
        get => Math.Clamp(_raw[DebounceOffset], 0, DebounceOptionsMs.Length - 1);
        set => _raw[DebounceOffset] = (byte)Math.Clamp(value, 0, DebounceOptionsMs.Length - 1);
    }

    /// <summary>当前按键防抖时间，单位毫秒。</summary>
    public int DebounceMs => DebounceOptionsMs[DebounceIndex];

    /// <summary><c>[80]</c> 静默高度档位索引，收敛到 0..2。</summary>
    public int LiftHeightIndex
    {
        get => Math.Clamp(_raw[LiftHeightOffset], 0, LiftHeightOptionsMm.Length - 1);
        set => _raw[LiftHeightOffset] = (byte)Math.Clamp(value, 0, LiftHeightOptionsMm.Length - 1);
    }

    /// <summary>当前静默高度，单位毫米。</summary>
    public int LiftHeightMm => LiftHeightOptionsMm[LiftHeightIndex];

    /// <summary>读取指定档位的板载记录。</summary>
    public DpiStage GetStage(int index)
    {
        int offset = StageOffset(index);
        return new DpiStage(
            Enabled: _raw[offset + StageEnabledOffset] != 0,
            RawDpi: (ushort)(_raw[offset + StageRawDpiOffset] |
                             (_raw[offset + StageRawDpiOffset + 1] << 8)),
            Red: _raw[offset + StageColorOffset],
            Green: _raw[offset + StageColorOffset + 1],
            Blue: _raw[offset + StageColorOffset + 2]);
    }

    /// <summary>覆写指定档位的板载记录。</summary>
    public void SetStage(int index, DpiStage stage)
    {
        int offset = StageOffset(index);
        _raw[offset + StageEnabledOffset] = (byte)(stage.Enabled ? 1 : 0);
        _raw[offset + StageRawDpiOffset] = (byte)(stage.RawDpi & 0xFF);
        _raw[offset + StageRawDpiOffset + 1] = (byte)(stage.RawDpi >> 8);
        _raw[offset + StageColorOffset] = stage.Red;
        _raw[offset + StageColorOffset + 1] = stage.Green;
        _raw[offset + StageColorOffset + 2] = stage.Blue;
    }

    /// <summary>读取指定档位的实际 DPI。</summary>
    public int GetStageDpi(DeviceHeader header, int index) =>
        DpiCodec.Decode(header, GetStage(index).RawDpi);

    /// <summary>按实际 DPI 覆写指定档位，其余字段保持不变。</summary>
    public void SetStageDpi(DeviceHeader header, int index, int dpi)
    {
        DpiStage stage = GetStage(index);
        SetStage(index, stage with { RawDpi = DpiCodec.Encode(header, dpi) });
    }

    /// <summary>板载配置与另一份内容是否逐字节一致。</summary>
    public bool ContentEquals(MouseProfile other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return _raw.AsSpan().SequenceEqual(other._raw);
    }

    private static int StageOffset(int index)
    {
        if (index is < 0 || index >= G3mCommands.MaxDpiStages)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index), index, $"DPI 档位索引必须在 0 到 {G3mCommands.MaxDpiStages - 1} 之间");
        }

        return StageRecordBase + (index * StageRecordSize);
    }
}
