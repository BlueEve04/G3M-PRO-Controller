using G3M.Core.Storage;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace G3M.Settings.Services;

/// <summary>把用户选择的背景材质应用到窗口上。</summary>
/// <remarks>
/// 两种半透明材质都要求窗口内容不画不透明背景，否则材质会被整块盖住；
/// 反过来说，选择"普通灰色"时必须给根元素补上不透明背景，否则窗口会变成全透明。
/// </remarks>
public static class BackdropService
{
    /// <summary>该材质在当前系统上是否可用。</summary>
    public static bool IsSupported(BackdropKind kind) => kind switch
    {
        BackdropKind.Mica => MicaController.IsSupported(),
        BackdropKind.Acrylic => DesktopAcrylicController.IsSupported(),
        BackdropKind.Solid => true,
        _ => false,
    };

    /// <summary>
    /// 应用材质。返回实际生效的材质——请求的材质不受支持时会退回普通灰色，
    /// 界面据此把选择框拨到真正生效的那一项，而不是显示一个假的选中状态。
    /// </summary>
    public static BackdropKind Apply(Window window, Panel root, BackdropKind requested)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(root);

        BackdropKind effective = IsSupported(requested) ? requested : BackdropKind.Solid;

        switch (effective)
        {
            case BackdropKind.Mica:
                window.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
                root.Background = null;
                break;

            case BackdropKind.Acrylic:
                window.SystemBackdrop = new DesktopAcrylicBackdrop();
                root.Background = null;
                break;

            default:
                // 没有材质时必须有实底，否则窗口内容无处着色。
                window.SystemBackdrop = null;
                root.Background = ResolveSolidBrush();
                break;
        }

        return effective;
    }

    private static Brush ResolveSolidBrush()
    {
        if (Application.Current.Resources.TryGetValue(
                "ApplicationPageBackgroundThemeBrush", out object? brush) && brush is Brush found)
        {
            return found;
        }

        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 32, 32, 32));
    }
}
