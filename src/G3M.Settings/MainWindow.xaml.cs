using G3M.Settings.Services;
using G3M.Settings.Views;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace G3M.Settings;

/// <summary>设置程序主窗口：一个状态条加一个导航框架。</summary>
public sealed partial class MainWindow : Window
{
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();

        Title = "G3M Controller";
        ConfigureBackdrop();

        Closed += (_, _) => App.Shutdown();

        // 窗口尺寸必须在窗口真正显示之后设置，构造阶段调用不会生效。
        Activated += OnFirstActivated;

        App.Context.Changed += (_, _) => DispatcherQueue.TryEnqueue(UpdateStatus);

        Navigation.SelectedItem = Navigation.MenuItems[0];
        _initialized = true;
        NavigateTo("overview");
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

    private void ConfigureBackdrop()
    {
        // 云母在 Win11 上可用；不可用时依次退回亚克力与纯色。
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        }
        else if (DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!_initialized || args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        NavigateTo(tag);
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
            _ => typeof(OverviewPage),
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
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
