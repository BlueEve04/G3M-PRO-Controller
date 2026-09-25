using System.Diagnostics;
using System.Reflection;
using G3M.Core.Storage;
using G3M.Settings.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace G3M.Settings.Views;

/// <summary>应用程序设置：窗口背景材质、数据位置、版本信息。</summary>
public sealed partial class SettingsPage : Page
{
    /// <summary>切换选择时会触发 SelectionChanged，用标志位挡住程序化赋值。</summary>
    private bool _suppressSelectionChanged;

    public SettingsPage()
    {
        InitializeComponent();
        Load();
    }

    private void Load()
    {
        _suppressSelectionChanged = true;
        try
        {
            ThemeSelector.SelectedIndex = App.Settings.Theme switch
            {
                AppTheme.Light => 1,
                AppTheme.Dark => 2,
                _ => 0,
            };

            // 系统不支持的材质直接置灰，而不是让用户选了之后没反应。
            MicaOption.IsEnabled = BackdropService.IsSupported(BackdropKind.Mica);
            AcrylicOption.IsEnabled = BackdropService.IsSupported(BackdropKind.Acrylic);
            SolidOption.IsEnabled = true;

            // 用实际生效的材质而不是存档里的期望值：期望值若不被系统支持，
            // 显示它会让用户以为已经生效。
            BackdropSelector.SelectedIndex = App.EffectiveBackdrop switch
            {
                BackdropKind.Acrylic => 1,
                BackdropKind.Solid => 2,
                _ => 0,
            };
        }
        finally
        {
            _suppressSelectionChanged = false;
        }

        UpdateThemeHint();
        UpdateHint();

        BackupPathText.Text = AppPaths.BackupDirectory;
        HistoryPathText.Text = AppPaths.HistoryFile;
        SettingsPathText.Text = AppSettings.FilePath;

        Version version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        VersionText.Text = $"G3M Controller {version.ToString(3)}　·　.NET {Environment.Version.ToString(2)}";
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged)
        {
            return;
        }

        AppTheme theme = ThemeSelector.SelectedIndex switch
        {
            1 => AppTheme.Light,
            2 => AppTheme.Dark,
            _ => AppTheme.System,
        };

        App.MainWindow?.ApplyTheme(theme);
        App.Settings.Theme = theme;
        App.Settings.Save();
        UpdateThemeHint();
    }

    private void UpdateThemeHint()
    {
        ThemeHint.Text = App.Settings.Theme switch
        {
            AppTheme.Light => "始终使用浅色，不随 Windows 设置变化。",
            AppTheme.Dark => "始终使用深色，不随 Windows 设置变化。",
            _ => "跟随 Windows 的浅色/深色设置，系统主题变化时窗口与标题栏会一起切换。",
        };
    }

    private async void OnBackdropChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged)
        {
            return;
        }

        BackdropKind requested = BackdropSelector.SelectedIndex switch
        {
            1 => BackdropKind.Acrylic,
            2 => BackdropKind.Solid,
            _ => BackdropKind.Mica,
        };

        BackdropKind effective = App.MainWindow?.ApplyBackdrop(requested) ?? requested;

        // 请求的材质不受支持时，把选择拨回真正生效的那一项，
        // 否则界面会显示一个假的选中状态。
        if (effective != requested)
        {
            _suppressSelectionChanged = true;
            try
            {
                BackdropSelector.SelectedIndex = effective switch
                {
                    BackdropKind.Acrylic => 1,
                    BackdropKind.Solid => 2,
                    _ => 0,
                };
            }
            finally
            {
                _suppressSelectionChanged = false;
            }

            await ShowUnsupportedAsync(requested);
        }

        App.Settings.Backdrop = effective;
        App.Settings.Save();
        UpdateHint();
    }

    private void UpdateHint()
    {
        string description = App.EffectiveBackdrop switch
        {
            BackdropKind.Mica => "云母会跟随桌面壁纸取色，窗口失焦时依然保持材质。",
            BackdropKind.Acrylic => "亚克力模糊更强、带噪点，能透出窗口后面的内容。",
            _ => "普通灰色使用系统主题背景色，不透明，兼容性最好。",
        };

        // 存档里的期望值与实际生效值不一致，说明系统不支持它，需要明说，
        // 否则用户会以为选择没保存成功。
        if (App.EffectiveBackdrop != App.Settings.Backdrop)
        {
            string wanted = App.Settings.Backdrop == BackdropKind.Mica ? "云母" : "亚克力";
            description += $"\n\n已保存的选择是「{wanted}」，但当前系统不支持，实际使用普通灰色。";
        }

        BackdropHint.Text = description;
    }

    private async Task ShowUnsupportedAsync(BackdropKind requested)
    {
        string name = requested == BackdropKind.Mica ? "云母" : "亚克力";

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "该材质不可用",
            Content = $"当前系统或显卡驱动不支持{name}材质，已改用普通灰色。\n\n"
                      + "云母与亚克力需要 Windows 11 且未在系统设置中关闭透明效果。",
            CloseButtonText = "知道了",
        };

        await dialog.ShowAsync();
    }

    private void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            AppPaths.EnsureCreated();
            Process.Start(new ProcessStartInfo(AppPaths.RootDirectory) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                                              or IOException or UnauthorizedAccessException)
        {
            // 打不开资源管理器不值得打断用户，忽略即可。
        }
    }
}
