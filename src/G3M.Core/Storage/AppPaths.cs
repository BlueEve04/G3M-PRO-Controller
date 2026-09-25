namespace G3M.Core.Storage;

/// <summary>程序用到的所有本地路径。目录都放在当前用户的本地缓存下，不需要管理员权限。</summary>
public static class AppPaths
{
    /// <summary>数据根目录：<c>%LOCALAPPDATA%\G3M Controller</c>。</summary>
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "G3M Controller");

    /// <summary>电量历史文件。</summary>
    public static string HistoryFile => Path.Combine(RootDirectory, "history.json");

    /// <summary>备份目录。</summary>
    public static string BackupDirectory => Path.Combine(RootDirectory, "Backups");

    /// <summary>最近一次备份的路径，用于"一键还原"。</summary>
    public static string LatestBackupFile => Path.Combine(BackupDirectory, "latest.json");

    /// <summary>确保数据目录存在。可重复调用。</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(BackupDirectory);
    }
}
