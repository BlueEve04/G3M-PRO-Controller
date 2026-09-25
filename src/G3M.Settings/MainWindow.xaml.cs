using G3M.Core.Storage;
using G3M.Settings.Services;
using G3M.Settings.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace G3M.Settings;

/// <summary>设置程序主窗口：一个导航框架加一条底部状态栏。</summary>
public sealed partial class MainWindow : Window
{
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();

        Title = "G3M Controller";

        // 背景材质由用户设置在「设置」页里选，这里按存档应用一次。
        ApplyBackdrop(App.Settings.Backdrop);

        Closed += (_, _) => App.Shutdown();

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
