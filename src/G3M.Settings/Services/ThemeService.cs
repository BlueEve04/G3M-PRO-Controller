using G3M.Core.Storage;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace G3M.Settings.Services;

/// <summary>应用深浅色主题，并让自绘标题栏一起跟随。</summary>
/// <remarks>
/// 窗口内容的主题靠设置根元素的 <see cref="FrameworkElement.RequestedTheme"/>，
/// 它能自动向下传播，选「跟随系统」时也会由框架实时更新。
/// 但标题栏上的最小化/最大化/关闭按钮是系统按我们给出的颜色画的，不会自己变，
/// 所以「跟随系统」模式下必须监听系统配色变化并重新上色。
/// </remarks>
public sealed class ThemeService : IDisposable
{
    /// <summary>深色下按钮的前景色（略降不透明度，接近系统观感）。</summary>
    private static readonly Color DarkForeground = Color.FromArgb(255, 255, 255, 255);
    private static readonly Color DarkInactiveForeground = Color.FromArgb(140, 255, 255, 255);
    private static readonly Color DarkHoverBackground = Color.FromArgb(32, 255, 255, 255);
    private static readonly Color DarkPressedBackground = Color.FromArgb(48, 255, 255, 255);

    private static readonly Color LightForeground = Color.FromArgb(255, 0, 0, 0);
    private static readonly Color LightInactiveForeground = Color.FromArgb(140, 0, 0, 0);
    private static readonly Color LightHoverBackground = Color.FromArgb(32, 0, 0, 0);
    private static readonly Color LightPressedBackground = Color.FromArgb(48, 0, 0, 0);

    private readonly FrameworkElement _root;
    private readonly AppWindowTitleBar _titleBar;
    private readonly UISettings _uiSettings = new();

    private AppTheme _theme = AppTheme.System;
    private bool _disposed;

    public ThemeService(FrameworkElement root, AppWindowTitleBar titleBar)
    {
        _root = root;
        _titleBar = titleBar;

        // 系统深浅色变化时，这个回调在线程池线程上触发，必须切回 UI 线程再改控件。
        _uiSettings.ColorValuesChanged += OnSystemColorsChanged;
    }

    /// <summary>应用主题。标题栏与窗口内容一起跟随。</summary>
    public void Apply(AppTheme theme)
    {
        _theme = theme;

        _root.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        PaintCaptionButtons(IsDark());
    }

    /// <summary>当前是否应当呈现深色。跟随系统时读系统背景色判断。</summary>
    private bool IsDark()
    {
        if (_theme == AppTheme.Dark)
        {
            return true;
        }

        if (_theme == AppTheme.Light)
        {
            return false;
        }

        Color background = _uiSettings.GetColorValue(UIColorType.Background);
        // 系统背景色接近黑即视为深色模式。用亮度而不是精确相等，
        // 因为不同主题包给出的值不完全一致。
        double luminance = ((0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B)) / 255;
        return luminance < 0.5;
    }

    private void PaintCaptionButtons(bool dark)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        // 标题栏已经延伸进内容区，系统不再画背景，因此按钮背景保持透明，
        // 让底下的云母/亚克力材质透上来。
        _titleBar.ButtonBackgroundColor = Colors.Transparent;
        _titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        _titleBar.ButtonForegroundColor = dark ? DarkForeground : LightForeground;
        _titleBar.ButtonInactiveForegroundColor = dark ? DarkInactiveForeground : LightInactiveForeground;
        _titleBar.ButtonHoverForegroundColor = dark ? DarkForeground : LightForeground;
        _titleBar.ButtonPressedForegroundColor = dark ? DarkForeground : LightForeground;

        _titleBar.ButtonHoverBackgroundColor = dark ? DarkHoverBackground : LightHoverBackground;
        _titleBar.ButtonPressedBackgroundColor = dark ? DarkPressedBackground : LightPressedBackground;
    }

    private void OnSystemColorsChanged(UISettings sender, object args)
    {
        if (_disposed || _theme != AppTheme.System)
        {
            return;
        }

        _root.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_disposed)
            {
                PaintCaptionButtons(IsDark());
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _uiSettings.ColorValuesChanged -= OnSystemColorsChanged;
    }
}
