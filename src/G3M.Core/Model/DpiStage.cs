namespace G3M.Core.Model;

/// <summary>一个 DPI 档位的板载记录（9 字节）。</summary>
/// <param name="Enabled">该档位是否参与 DPI 循环。</param>
/// <param name="RawDpi">未经换算的原始码值，实际 DPI 需经 <see cref="DpiCodec.Decode"/> 换算。</param>
/// <param name="Red">灯光红色分量。</param>
/// <param name="Green">灯光绿色分量。</param>
/// <param name="Blue">灯光蓝色分量。</param>
public readonly record struct DpiStage(
    bool Enabled, ushort RawDpi, byte Red, byte Green, byte Blue)
{
    /// <summary>该档位的灯光颜色。</summary>
    public (byte Red, byte Green, byte Blue) Color => (Red, Green, Blue);

    /// <summary>用于展示的十六进制颜色串。</summary>
    public string ColorHex => $"#{Red:X2}{Green:X2}{Blue:X2}";
}
