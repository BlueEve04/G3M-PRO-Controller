namespace G3M.Core.Keys;

/// <summary>按键记录首字节表达的动作类型。这些取值都来自真机实测的映射表。</summary>
public enum KeyActionKind : byte
{
    /// <summary>空槽位，未分配任何功能。</summary>
    Empty = 0x00,

    /// <summary>映射到某个鼠标按键。</summary>
    MouseButton = 0x10,

    /// <summary>厂商特殊功能，例如 DPI 循环。</summary>
    Special = 0x13,

    /// <summary>发送一次键盘报文。</summary>
    Keyboard = 0x20,

    /// <summary>本程序尚未识别的编码，原样保留、不做解释。</summary>
    Unknown = 0xFF,
}

/// <summary>
/// 鼠标按键目标。数值直接就是记录里的按键位，与标准 HID 鼠标报告的按键位一致。
/// </summary>
/// <remarks>
/// 后两个按键的记录第二字节为 <c>0x01</c>，前三个为 <c>0x00</c>——这是本机设备上
/// 实测到的固定差异（槽位 3、4 分别是 <c>10 10 01</c> 与 <c>10 08 01</c>），
/// 推测是"扩展按键"标记。写入时按同样规则还原。
/// </remarks>
public enum MouseButtonTarget : byte
{
    /// <summary>左键（HID 按键 1）。</summary>
    Left = 0x01,

    /// <summary>右键（HID 按键 2）。</summary>
    Right = 0x02,

    /// <summary>中键 / 滚轮按下（HID 按键 3）。</summary>
    Middle = 0x04,

    /// <summary>后退（HID 按键 4）。</summary>
    Back = 0x08,

    /// <summary>前进（HID 按键 5）。</summary>
    Forward = 0x10,
}

/// <summary>
/// 一条按键映射记录，就是板载表里的 3 个字节。保持原始字节形态，
/// 因此无法识别的厂商编码也能被原样读回和写回，不会在编辑过程中丢失。
/// </summary>
/// <param name="Byte0">动作类型。</param>
/// <param name="Byte1">参数一：鼠标按键位 / 特殊功能组 / 键盘修饰键掩码。</param>
/// <param name="Byte2">参数二：扩展标记 / 特殊功能码 / HID 键码。</param>
public readonly record struct KeyAction(byte Byte0, byte Byte1, byte Byte2)
{
    /// <summary>未分配。</summary>
    public static readonly KeyAction Empty = new(0x00, 0x00, 0x00);

    /// <summary>DPI 循环，原厂默认分配给 DPI 键。</summary>
    public static readonly KeyAction DpiCycle = new(0x13, 0x03, 0x00);

    /// <summary>动作类型。</summary>
    public KeyActionKind Kind => Byte0 switch
    {
        0x00 => KeyActionKind.Empty,
        0x10 => KeyActionKind.MouseButton,
        0x13 => KeyActionKind.Special,
        0x20 => KeyActionKind.Keyboard,
        _ => KeyActionKind.Unknown,
    };

    /// <summary>是否为空槽位。</summary>
    public bool IsEmpty => Byte0 == 0x00 && Byte1 == 0x00 && Byte2 == 0x00;

    /// <summary>构造一个鼠标按键映射。</summary>
    public static KeyAction Mouse(MouseButtonTarget target)
    {
        byte marker = (byte)target >= (byte)MouseButtonTarget.Back ? (byte)0x01 : (byte)0x00;
        return new KeyAction(0x10, (byte)target, marker);
    }

    /// <summary>构造一次键盘按键，可带修饰键。</summary>
    public static KeyAction Keyboard(byte modifiers, byte keyCode) => new(0x20, modifiers, keyCode);

    /// <summary>构造一次键盘按键，不带修饰键。</summary>
    public static KeyAction Keyboard(byte keyCode) => new(0x20, 0x00, keyCode);

    /// <summary>该映射对应的鼠标按键；不是鼠标按键时返回 null。</summary>
    public MouseButtonTarget? AsMouseButton() =>
        Kind == KeyActionKind.MouseButton && Enum.IsDefined((MouseButtonTarget)Byte1)
            ? (MouseButtonTarget)Byte1
            : null;

    /// <summary>给用户看的中文描述。未知编码会明确显示原始字节，而不是猜测。</summary>
    public string Describe() => Kind switch
    {
        KeyActionKind.Empty => "未分配",
        KeyActionKind.MouseButton => AsMouseButton() switch
        {
            MouseButtonTarget.Left => "鼠标左键",
            MouseButtonTarget.Right => "鼠标右键",
            MouseButtonTarget.Middle => "鼠标中键",
            MouseButtonTarget.Back => "鼠标后退",
            MouseButtonTarget.Forward => "鼠标前进",
            _ => $"鼠标按键 (0x{Byte1:X2})",
        },
        KeyActionKind.Special => this == DpiCycle
            ? "DPI 循环"
            : $"厂商功能 (组 0x{Byte1:X2} 码 0x{Byte2:X2})",
        KeyActionKind.Keyboard => HidKeyCode.Describe(Byte1, Byte2),
        _ => $"未知编码 ({Byte0:X2} {Byte1:X2} {Byte2:X2})",
    };

    public override string ToString() => Describe();
}
