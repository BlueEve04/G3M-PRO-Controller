using G3M.Core.Keys;
using G3M.Core.Protocol;

namespace G3M.Core.Model;

/// <summary>
/// 板载按键映射表，每槽 3 字节。本机设备实测为 42 槽（<c>header[5] == 0x2A</c>），
/// 前 6 槽对应 6 个物理按键，其余为空槽。
/// </summary>
public sealed class KeyMappingTable
{
    /// <summary>协议允许的最大槽位数，用于拦截设备返回的异常值。</summary>
    public const int MaxSlots = 170;

    private readonly byte[] _raw;

    /// <summary>用一段连续字节构造映射表，长度必须是 3 的倍数。</summary>
    public KeyMappingTable(byte[] raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length == 0 || raw.Length % G3mCommands.KeySlotSize != 0)
        {
            throw new ArgumentException(
                $"按键映射表长度必须是 3 的正数倍，实际 {raw.Length}", nameof(raw));
        }

        int slots = raw.Length / G3mCommands.KeySlotSize;
        if (slots > MaxSlots)
        {
            throw new ArgumentException($"按键槽位数 {slots} 超过协议上限 {MaxSlots}", nameof(raw));
        }

        _raw = (byte[])raw.Clone();
    }

    /// <summary>槽位数量。</summary>
    public int SlotCount => _raw.Length / G3mCommands.KeySlotSize;

    /// <summary>导出副本，可直接交给写入路径。</summary>
    public byte[] ToArray() => (byte[])_raw.Clone();

    /// <summary>读取只读视图，用于比对。</summary>
    public ReadOnlySpan<byte> AsSpan() => _raw;

    /// <summary>读取指定槽位。</summary>
    public KeyAction GetSlot(int index)
    {
        int offset = SlotOffset(index);
        return new KeyAction(_raw[offset], _raw[offset + 1], _raw[offset + 2]);
    }

    /// <summary>覆写指定槽位。</summary>
    public void SetSlot(int index, KeyAction action)
    {
        int offset = SlotOffset(index);
        _raw[offset] = action.Byte0;
        _raw[offset + 1] = action.Byte1;
        _raw[offset + 2] = action.Byte2;
    }

    /// <summary>找出所有等于给定动作的槽位索引，用于识别原厂分配（例如哪个槽是 DPI 循环）。</summary>
    public IReadOnlyList<int> FindSlots(KeyAction action)
    {
        var matches = new List<int>();
        for (int i = 0; i < SlotCount; i++)
        {
            if (GetSlot(i) == action)
            {
                matches.Add(i);
            }
        }

        return matches;
    }

    /// <summary>是否为出厂默认的表（前 6 槽为原厂分配，其余为空）。</summary>
    public bool IsFactoryDefault()
    {
        KeyAction[] expected =
        [
            KeyAction.Mouse(MouseButtonTarget.Left),
            KeyAction.Mouse(MouseButtonTarget.Middle),
            KeyAction.Mouse(MouseButtonTarget.Right),
            KeyAction.Mouse(MouseButtonTarget.Forward),
            KeyAction.Mouse(MouseButtonTarget.Back),
        ];

        if (SlotCount < expected.Length)
        {
            return false;
        }

        for (int i = 0; i < expected.Length; i++)
        {
            if (GetSlot(i) != expected[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>与另一份映射表是否逐字节一致。</summary>
    public bool ContentEquals(KeyMappingTable other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return _raw.AsSpan().SequenceEqual(other._raw);
    }

    private int SlotOffset(int index)
    {
        if (index < 0 || index >= SlotCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index), index, $"槽位索引必须在 0 到 {SlotCount - 1} 之间");
        }

        return index * G3mCommands.KeySlotSize;
    }
}
