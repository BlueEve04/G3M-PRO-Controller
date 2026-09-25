using G3M.Core.Storage;
using G3M.Settings.Services;
using G3M.Settings.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace G3M.Settings;

/// <summary>设置程序主窗口：自绘标题栏、导航框架、底部状态栏。</summary>
public sealed partial class MainWindow : Window
{
    private readonly ThemeService _themeService;
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();

        Title = "G3M Controller";

        // 内容延伸进标题栏：材质能透上来，标题栏按钮颜色也能精确跟随主题。
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppTitleBar.Height = ResolveCaptionHeight();

        _themeService = new ThemeService(RootGrid, AppWindow.TitleBar);
        _themeService.Apply(App.Settings.Theme);

        // 背景材质由用户设置在「设置」页里选，这里按存档应用一次。
        ApplyBackdrop(App.Settings.Backdrop);

        Closed += (_, _) =>
        {
            _themeService.Dispose();
            App.Shutdown();
        };

        // 窗口尺寸必须在窗口真正显示之后设置，构造阶段调用不会生效。
        Activated += OnFirstActivated;

        App.Context.Changed += (_, _) => DispatcherQueue.TryEnqueue(UpdateStatus);

        Navigation.SelectedItem = Navigation.MenuItems[0];
        _initialized = true;
        NavigateTo("overview");
    }

    /// <summary>
    /// 应用背景材质，并记录实际生效的那一种。请求的材质若不被系统支持会退回普通灰色，
    /// 设置页据此显示真实状态。
    /// </summary>
    public BackdropKind ApplyBackdrop(BackdropKind requested)
    {
        BackdropKind effective = BackdropService.Apply(this, RootGrid, requested);
        App.EffectiveBackdrop = effective;
        return effective;
    }

    /// <summary>应用深浅色主题，标题栏、内容与背景材质一起跟随。</summary>
    public void ApplyTheme(AppTheme theme)
    {
        _themeService.Apply(theme);

        // 切换主题后必须重建背景材质：云母/亚克力的深浅色在实例创建时就确定了，
        // 沿用旧实例的话浅色主题下背景会仍然是深色，与内容对不上。
        ApplyBackdrop(App.Settings.Backdrop);
    }

    /// <summary>
    /// 取系统标题栏高度，让自绘标题栏与系统观感一致（会随 DPI 与用户设置变化）。
    /// 取不到时退回 36。
    /// </summary>
    private double ResolveCaptionHeight()
    {
        try
        {
            double height = AppWindow.TitleBar.Height;
            return height > 0 ? height : 36;
        }
        catch (Exception exception) when (exception is NotSupportedException or ArgumentException)
        {
            return 36;
        }
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;

        // Activated 触发时窗口尚未完成首次布局，此时直接 Resize 会被随后的默认布局覆盖。
        // 排到队列末尾再设，尺寸才落得下来。
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 800)));

        _ = App.Context.RefreshAsync();
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!_initialized)
        {
            return;
        }

        if (args.IsSettingsSelected)
        {
            NavigateTo("settings");
            return;
        }

        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            NavigateTo(tag);
        }
    }

    /// <summary>导航到指定页面。托盘通过命令行传入的页面名也走这里。</summary>
    public void NavigateTo(string tag)
    {
        Type pageType = tag switch
        {
            "performance" => typeof(PerformancePage),
            "advanced" => typeof(AdvancedPage),
            "buttons" => typeof(ButtonsPage),
            "battery" => typeof(BatteryPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(OverviewPage),
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }

        if (tag == "settings")
        {
            Navigation.SelectedItem = Navigation.SettingsItem;
            return;
        }

        foreach (object menuItem in Navigation.MenuItems)
        {
            if (menuItem is NavigationViewItem item && (string?)item.Tag == tag)
            {
                Navigation.SelectedItem = item;
                break;
            }
        }
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e) =>
        await App.Context.RefreshAsync();

    private void UpdateStatus()
    {
        StatusText.Text = App.Context.StatusText;
        BusyRing.IsActive = App.Context.IsBusy;
        RefreshButton.IsEnabled = !App.Context.IsBusy;
    }
}
