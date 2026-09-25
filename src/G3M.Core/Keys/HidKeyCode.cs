namespace G3M.Core.Keys;

/// <summary>
/// USB HID 键盘用途页（0x07）的键码表，以及键盘报文里的修饰键位。
/// 鼠标的 3 字节按键记录用 <c>0x20 修饰键 键码</c> 表达一次键盘按键，
/// 修饰键字节的位定义就是标准 HID 键盘报告的定义。
/// </summary>
public static class HidKeyCode
{
    public const byte ModifierLeftControl = 0x01;
    public const byte ModifierLeftShift = 0x02;
    public const byte ModifierLeftAlt = 0x04;
    public const byte ModifierLeftGui = 0x08;
    public const byte ModifierRightControl = 0x10;
    public const byte ModifierRightShift = 0x20;
    public const byte ModifierRightAlt = 0x40;
    public const byte ModifierRightGui = 0x80;

    /// <summary>键码到中文名称的映射。仅包含有实际意义、可直接映射的键码。</summary>
    private static readonly Dictionary<byte, string> Names = BuildNames();

    /// <summary>修饰键位到中文名称的映射。</summary>
    private static readonly (byte Mask, string Name)[] ModifierNames =
    [
        (ModifierLeftControl, "左Ctrl"),
        (ModifierLeftShift, "左Shift"),
        (ModifierLeftAlt, "左Alt"),
        (ModifierLeftGui, "左Win"),
        (ModifierRightControl, "右Ctrl"),
        (ModifierRightShift, "右Shift"),
        (ModifierRightAlt, "右Alt"),
        (ModifierRightGui, "右Win"),
    ];

    /// <summary>可用于映射的键码列表，按 UI 展示顺序排列。</summary>
    public static IReadOnlyList<byte> MappableKeyCodes => [.. Names.Keys.Order()];

    /// <summary>取键码的中文名称；未知键码回退为十六进制形式。</summary>
    public static string GetName(byte keyCode) =>
        Names.TryGetValue(keyCode, out string? name) ? name : $"未知键 (0x{keyCode:X2})";

    /// <summary>该键码是否有已知名称。</summary>
    public static bool IsKnown(byte keyCode) => Names.ContainsKey(keyCode);

    /// <summary>把修饰键位掩码展开成中文名称列表。</summary>
    public static IReadOnlyList<string> DescribeModifiers(byte modifiers)
    {
        if (modifiers == 0)
        {
            return [];
        }

        var result = new List<string>();
        foreach ((byte mask, string name) in ModifierNames)
        {
            if ((modifiers & mask) != 0)
            {
                result.Add(name);
            }
        }

        return result;
    }

    /// <summary>把一次按键描述成可展示的字符串，例如 “Ctrl + Shift + A”。</summary>
    public static string Describe(byte modifiers, byte keyCode)
    {
        var parts = new List<string>(DescribeModifiers(modifiers));

        if (keyCode != 0)
        {
            parts.Add(GetName(keyCode));
        }

        return parts.Count == 0 ? "无" : string.Join(" + ", parts);
    }

    private static Dictionary<byte, string> BuildNames()
    {
        var names = new Dictionary<byte, string>();

        // 字母 A-Z
        for (byte i = 0; i < 26; i++)
        {
            names[(byte)(0x04 + i)] = ((char)('A' + i)).ToString();
        }

        // 数字 1-9, 0
        string[] digits = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "0"];
        for (byte i = 0; i < digits.Length; i++)
        {
            names[(byte)(0x1E + i)] = digits[i];
        }

        names[0x28] = "回车";
        names[0x29] = "Esc";
        names[0x2A] = "退格";
        names[0x2B] = "Tab";
        names[0x2C] = "空格";
        names[0x2D] = "- _";
        names[0x2E] = "= +";
        names[0x2F] = "[ {";
        names[0x30] = "] }";
        names[0x31] = "\\ |";
        names[0x32] = "非US # ~";
        names[0x33] = "; :";
        names[0x34] = "' \"";
        names[0x35] = "` ~";
        names[0x36] = ", <";
        names[0x37] = ". >";
        names[0x38] = "/ ?";
        names[0x39] = "大写锁定";

        // F1-F12
        for (byte i = 0; i < 12; i++)
        {
            names[(byte)(0x3A + i)] = $"F{i + 1}";
        }

        names[0x46] = "Print Screen";
        names[0x47] = "Scroll Lock";
        names[0x48] = "Pause";
        names[0x49] = "Insert";
        names[0x4A] = "Home";
        names[0x4B] = "Page Up";
        names[0x4C] = "Delete";
        names[0x4D] = "End";
        names[0x4E] = "Page Down";
        names[0x4F] = "方向键 右";
        names[0x50] = "方向键 左";
        names[0x51] = "方向键 下";
        names[0x52] = "方向键 上";
        names[0x53] = "Num Lock";
        names[0x54] = "小键盘 /";
        names[0x55] = "小键盘 *";
        names[0x56] = "小键盘 -";
        names[0x57] = "小键盘 +";
        names[0x58] = "小键盘 回车";

        // 小键盘 1-9, 0
        for (byte i = 0; i < 9; i++)
        {
            names[(byte)(0x59 + i)] = $"小键盘 {i + 1}";
        }

        names[0x62] = "小键盘 0";
        names[0x63] = "小键盘 .";
        names[0x64] = "非US \\ |";
        names[0x65] = "菜单键";
        names[0x67] = "小键盘 =";

        // F13-F24
        for (byte i = 0; i < 12; i++)
        {
            names[(byte)(0x68 + i)] = $"F{i + 13}";
        }

        names[0x74] = "Execute";
        names[0x75] = "Help";
        names[0x77] = "Select";
        names[0x78] = "Stop";
        names[0x79] = "Again";
        names[0x7A] = "撤销";
        names[0x7B] = "剪切";
        names[0x7C] = "复制";
        names[0x7D] = "粘贴";
        names[0x7E] = "查找";
        names[0x7F] = "静音";
        names[0x80] = "音量 +";
        names[0x81] = "音量 -";

        names[0x85] = "小键盘 ,";

        // 修饰键本身也可以作为独立键码
        names[0xE0] = "左 Ctrl";
        names[0xE1] = "左 Shift";
        names[0xE2] = "左 Alt";
        names[0xE3] = "左 Win";
        names[0xE4] = "右 Ctrl";
        names[0xE5] = "右 Shift";
        names[0xE6] = "右 Alt";
        names[0xE7] = "右 Win";

        return names;
    }
}
