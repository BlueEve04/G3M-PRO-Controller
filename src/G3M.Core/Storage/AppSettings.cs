using System.Text.Json;
using System.Text.Json.Serialization;

namespace G3M.Core.Storage;

/// <summary>窗口背景材质。</summary>
public enum BackdropKind
{
    /// <summary>云母：跟随桌面壁纸的柔和半透明材质。</summary>
    Mica = 0,

    /// <summary>亚克力：比云母更强的模糊与噪点，能透出窗口后面的内容。</summary>
    Acrylic = 1,

    /// <summary>普通灰色：不透明的主题背景色，兼容性最好。</summary>
    Solid = 2,
}

/// <summary>界面深浅色主题。</summary>
public enum AppTheme
{
    /// <summary>跟随 Windows 的浅色/深色设置。</summary>
    System = 0,

    /// <summary>始终使用浅色。</summary>
    Light = 1,

    /// <summary>始终使用深色。</summary>
    Dark = 2,
}

/// <summary>应用程序自身的界面偏好，与鼠标配置无关。</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    /// <summary>窗口背景材质，默认云母。</summary>
    public BackdropKind Backdrop { get; set; } = BackdropKind.Mica;

    /// <summary>深浅色主题，默认跟随系统。标题栏与窗口内容一起跟随。</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>设置文件路径：<c>%LOCALAPPDATA%\G3M Controller\settings.json</c>。</summary>
    public static string FilePath => Path.Combine(AppPaths.RootDirectory, "settings.json");

    /// <summary>
    /// 载入设置。文件不存在或损坏时返回默认值而不是抛异常——
    /// 界面偏好没读到不该阻止程序启动。
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(FilePath);
            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, Options);

            if (settings is null || !Enum.IsDefined(settings.Backdrop) || !Enum.IsDefined(settings.Theme))
            {
                return new AppSettings();
            }

            return settings;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or JsonException)
        {
            return new AppSettings();
        }
    }

    /// <summary>写入设置。使用临时文件加原子替换，避免读到半截文件。</summary>
    public bool Save()
    {
        try
        {
            AppPaths.EnsureCreated();

            string json = JsonSerializer.Serialize(this, Options);
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, json);

            if (File.Exists(FilePath))
            {
                File.Replace(temporary, FilePath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, FilePath);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
