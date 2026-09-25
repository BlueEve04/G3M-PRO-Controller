using G3M.Core.Storage;
using G3M.Settings.Services;
using Microsoft.UI.Xaml;

namespace G3M.Settings;

/// <summary>应用入口。</summary>
public partial class App : Application
{
    private MainWindow? _window;

    public App() => InitializeComponent();

    /// <summary>全局共享的设备状态与操作上下文。</summary>
    public static SettingsContext Context { get; } = new();

    /// <summary>界面偏好。在 <see cref="OnLaunched"/> 里从磁盘载入。</summary>
    public static AppSettings Settings { get; private set; } = new();

    /// <summary>
    /// 当前实际生效的背景材质。用户期望的材质若不被系统支持会退回普通灰色，
    /// 界面据此显示真实状态，而不是显示一个并未生效的选择。
    /// </summary>
    /// <remarks>
    /// 由 <see cref="MainWindow.ApplyBackdrop"/> 写入。不在这里提供应用入口是因为
    /// 主窗口构造函数执行期间 <see cref="MainWindow"/> 还是 null，走静态入口会拿不到窗口。
    /// </remarks>
    public static BackdropKind EffectiveBackdrop { get; internal set; } = BackdropKind.Mica;

    /// <summary>主窗口。窗口尚未创建时为 null。</summary>
    public static MainWindow? MainWindow => (Current as App)?._window;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Settings = AppSettings.Load();

        _window = new MainWindow();
        _window.Activate();

        // 托盘可以通过 --page 直接打开指定页面。
        string? page = ReadPageArgument(args.Arguments);
        if (page is not null)
        {
            _window.NavigateTo(page);
        }
    }

    /// <summary>
    /// 关闭主窗口后终结进程。WinUI 3 在非打包模式下不会因为最后一个窗口关闭而自动退出，
    /// 因此显式调用 Exit，并挂一个兜底的强制退出，避免进程变成后台残留。
    /// </summary>
    public static void Shutdown()
    {
        Current.Exit();

        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            Environment.Exit(0);
        });
    }

    /// <summary>
    /// 从启动参数里取出 <c>--page</c> 的值。
    /// 非打包应用的 <see cref="LaunchActivatedEventArgs.Arguments"/> 是空的，
    /// 真实命令行只能从进程参数拿。
    /// </summary>
    private static string? ReadPageArgument(string? launchArguments)
    {
        string[] arguments = string.IsNullOrWhiteSpace(launchArguments)
            ? Environment.GetCommandLineArgs()
            : [.. launchArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)];

        for (int i = 0; i < arguments.Length - 1; i++)
        {
            if (string.Equals(arguments[i], "--page", StringComparison.OrdinalIgnoreCase))
            {
                return arguments[i + 1].Trim('"');
            }
        }

        return null;
    }
}
