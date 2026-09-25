using System.Text.Json;
using System.Text.Json.Serialization;
using G3M.Core.Model;

namespace G3M.Core.Storage;

/// <summary>
/// 一次写入前的设备配置快照备份。同时保存板载配置和按键映射表，
/// 因为"修改鼠标功能"最坏的情况是把某个键映射没了，必须能整体还原。
/// </summary>
/// <param name="SavedAt">备份时刻。</param>
/// <param name="ConnectionName">备份时的连接方式，仅用于展示。</param>
/// <param name="ProfileIndex">配置编号。</param>
/// <param name="ProfileBytes">84 字节板载配置。</param>
/// <param name="KeyMappingBytes">按键映射表原始字节。</param>
public sealed record DeviceBackup(
    DateTimeOffset SavedAt,
    string ConnectionName,
    byte ProfileIndex,
    byte[] ProfileBytes,
    byte[] KeyMappingBytes)
{
    /// <summary>还原成板载配置模型。</summary>
    public MouseProfile ToProfile() => new(ProfileBytes);

    /// <summary>还原成按键映射表模型。</summary>
    public KeyMappingTable ToKeyMapping() => new(KeyMappingBytes);

    /// <summary>供界面展示的摘要。</summary>
    public string Summary =>
        $"{SavedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} · {ConnectionName} · " +
        $"配置 {ProfileBytes.Length} 字节 · 按键 {KeyMappingBytes.Length / 3} 槽";
}

/// <summary>备份的磁盘表示。字节数组用 base64 存放，文件本身是可直接查看的 JSON。</summary>
internal sealed class DeviceBackupDocument
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("savedAt")]
    public DateTimeOffset SavedAt { get; set; }

    [JsonPropertyName("connection")]
    public string Connection { get; set; } = "";

    [JsonPropertyName("profileIndex")]
    public int ProfileIndex { get; set; }

    [JsonPropertyName("profile")]
    public string Profile { get; set; } = "";

    [JsonPropertyName("keyMapping")]
    public string KeyMapping { get; set; } = "";
}

/// <summary>备份的读写。任何失败都不抛异常，只返回 null —— 备份失败不应该让整个写流程崩溃。</summary>
public static class DeviceBackupStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    /// <summary>保存一份备份，同时更新 <c>latest.json</c>。返回落盘的路径；失败返回 null。</summary>
    public static string? Save(DeviceBackup backup)
    {
        ArgumentNullException.ThrowIfNull(backup);

        try
        {
            AppPaths.EnsureCreated();

            var document = new DeviceBackupDocument
            {
                SavedAt = backup.SavedAt,
                Connection = backup.ConnectionName,
                ProfileIndex = backup.ProfileIndex,
                Profile = Convert.ToBase64String(backup.ProfileBytes),
                KeyMapping = Convert.ToBase64String(backup.KeyMappingBytes),
            };

            string json = JsonSerializer.Serialize(document, Options);
            string stamp = backup.SavedAt.LocalDateTime.ToString("yyyyMMdd-HHmmss");
            string target = Path.Combine(AppPaths.BackupDirectory, $"backup-{stamp}.json");

            File.WriteAllText(target, json);
            File.WriteAllText(AppPaths.LatestBackupFile, json);
            PruneOldBackups();
            return target;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or JsonException or FormatException)
        {
            return null;
        }
    }

    /// <summary>读取最近一次备份；不存在或损坏时返回 null。</summary>
    public static DeviceBackup? LoadLatest()
    {
        if (!File.Exists(AppPaths.LatestBackupFile))
        {
            return null;
        }

        try
        {
            return Parse(File.ReadAllText(AppPaths.LatestBackupFile));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or JsonException or FormatException)
        {
            return null;
        }
    }

    private static DeviceBackup? Parse(string json)
    {
        DeviceBackupDocument? document = JsonSerializer.Deserialize<DeviceBackupDocument>(json, Options);
        if (document is null || document.Version != DeviceBackupDocument.CurrentVersion)
        {
            return null;
        }

        byte[] profile = Convert.FromBase64String(document.Profile);
        byte[] keyMapping = Convert.FromBase64String(document.KeyMapping);

        if (profile.Length != MouseProfile.Length ||
            keyMapping.Length == 0 ||
            keyMapping.Length % Protocol.G3mCommands.KeySlotSize != 0)
        {
            return null;
        }

        return new DeviceBackup(
            document.SavedAt, document.Connection, (byte)document.ProfileIndex, profile, keyMapping);
    }

    /// <summary>只保留最近 30 份带时间戳的备份，避免长期使用后无限增长。</summary>
    private static void PruneOldBackups(int keep = 30)
    {
        try
        {
            var files = Directory.GetFiles(AppPaths.BackupDirectory, "backup-*.json")
                .OrderByDescending(static path => path, StringComparer.Ordinal)
                .Skip(keep)
                .ToList();

            foreach (string file in files)
            {
                File.Delete(file);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 清理失败无关紧要。
        }
    }
}
