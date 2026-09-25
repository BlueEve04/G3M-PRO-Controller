using System.Windows.Forms;

namespace G3M.Tray;

internal static class Program
{
    /// <summary>
    /// 托盘程序的入口。这里刻意不创建任何 <see cref="Form"/>：
    /// 常驻内存只留给 NotifyIcon、一个 5 秒定时器和一份电量历史。
    /// </summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var singleInstance = SingleInstance.Acquire("G3M.Tray.SingleInstance");
        if (!singleInstance.IsOwner)
        {
            // 已经有一个托盘实例在跑了，第二个直接退出，避免出现两个图标。
            return;
        }

        using var context = new TrayApplicationContext();
        Application.Run(context);
    }
}
