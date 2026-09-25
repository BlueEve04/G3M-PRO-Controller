using Microsoft.Win32;

namespace G3M.Tray;

/// <summary>
/// 开机自启。写当前用户的 Run 键，不需要管理员权限。
/// </summary>
/// <remarks>
/// 值名刻意使用 <c>G3MController</c> 而不是 <c>G3MBattery</c>——
/// 后者已被同机上的 Go 版托盘程序占用，两者会互相覆盖。
/// 注册表是这项设置的唯一真相来源，配置文件不重复保存。
/// </remarks>
internal static class StartupRegistry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "G3MController";

    /// <summary>被替代的旧程序使用的值名。</summary>
    public const string LegacyValueName = "G3MBattery";

    /// <summary>当前是否已设置为开机自启。</summary>
    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    /// <summary>开或关开机自启。返回是否操作成功。</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{ExecutablePath}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                              or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// 启动时校正：程序被移动过目录的话，注册表里记的还是旧路径，需要重写。
    /// </summary>
    public static void Reconcile()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        if (key?.GetValue(ValueName) is not string stored || stored.Length == 0)
        {
            return;
        }

        string expected = $"\"{ExecutablePath}\"";
        if (!string.Equals(stored, expected, StringComparison.OrdinalIgnoreCase))
        {
            SetEnabled(true);
        }
    }

    /// <summary>检测同机上是否还挂着旧版 Go 托盘程序的开机自启项。</summary>
    public static string? DescribeLegacyEntry()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(LegacyValueName) as string;
    }

    /// <summary>移除旧版程序的开机自启项。返回是否确实删掉了。</summary>
    public static bool RemoveLegacyEntry()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(LegacyValueName) is not string)
            {
                return false;
            }

            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                              or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    private static string ExecutablePath =>
        Environment.ProcessPath ?? Application.ExecutablePath;
}
