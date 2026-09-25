using G3M.Core.Model;
using G3M.Core.Protocol;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace G3M.Settings.Views;

/// <summary>性能页：7 个 DPI 档位的数值、启用状态与灯光颜色，以及回报率。</summary>
public sealed partial class PerformancePage : Page
{
    private readonly List<CheckBox> _enabledBoxes = [];
    private readonly List<NumberBox> _dpiBoxes = [];
    private readonly List<Button> _colorButtons = [];

    /// <summary>编辑中的草稿。点"应用"之前不会写进鼠标。</summary>
    private MouseProfile? _draft;

    public PerformancePage()
    {
        InitializeComponent();

        App.Context.Changed += OnContextChanged;
        Unloaded += (_, _) => App.Context.Changed -= OnContextChanged;

        Rebuild();
    }

    private void OnContextChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(Rebuild);

    /// <summary>重建整套控件。快照变化时调用，草稿会被重置为设备当前值。</summary>
    private void Rebuild()
    {
        DeviceSnapshot? snapshot = App.Context.Snapshot;
        bool connected = snapshot is not null;

        ApplyDpiButton.IsEnabled = connected && !App.Context.IsBusy;
        ResetDpiButton.IsEnabled = connected && !App.Context.IsBusy;

        if (snapshot is null)
        {
            _draft = null;
            StageRows.Children.Clear();
            _enabledBoxes.Clear();
            _dpiBoxes.Clear();
            _colorButtons.Clear();
            RateButtons.Children.Clear();
            DpiHint.Text = "连接设备后可用";
            return;
        }

        _draft = snapshot.Profile.Clone();
        DpiHint.Text = $"步进 {snapshot.Header.DpiStep} · 范围 50 - 26000 DPI";

        BuildStageRows(snapshot);
        BuildRateButtons(snapshot);
    }

    private void BuildStageRows(DeviceSnapshot snapshot)
    {
        StageRows.Children.Clear();
        _enabledBoxes.Clear();
        _dpiBoxes.Clear();
        _colorButtons.Clear();

        for (int stage = 0; stage < G3mCommands.MaxDpiStages; stage++)
        {
            bool supported = stage < snapshot.Header.StageCount;
            DpiStage record = snapshot.Profile.GetStage(stage);
            bool isActive = stage == snapshot.ActiveStage;

            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var check = new CheckBox
            {
                IsChecked = record.Enabled,
                IsEnabled = supported && !App.Context.IsBusy,
                MinWidth = 0,
                Tag = stage,
            };
            check.Checked += OnStageEnabledChanged;
            check.Unchecked += OnStageEnabledChanged;
            Grid.SetColumn(check, 0);

            var label = new TextBlock
            {
                Text = $"档位 {stage + 1}",
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = supported ? 1.0 : 0.4,
            };
            Grid.SetColumn(label, 1);

            var dpiBox = new NumberBox
            {
                Value = snapshot.Profile.GetStageDpi(snapshot.Header, stage),
                Minimum = DpiCodec.MinDpi,
                Maximum = DpiCodec.MaxDpi,
                SmallChange = snapshot.Header.DpiStep,
                LargeChange = snapshot.Header.DpiStep * 10,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                IsEnabled = supported && !App.Context.IsBusy,
                Tag = stage,
                Width = 168,
            };
            Grid.SetColumn(dpiBox, 2);

            var unit = new TextBlock
            {
                Text = "DPI",
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.6,
            };
            Grid.SetColumn(unit, 3);

            var colorButton = new Button
            {
                Width = 34,
                Height = 28,
                Padding = new Thickness(0),
                Tag = stage,
                IsEnabled = supported && !App.Context.IsBusy,
                Background = new SolidColorBrush(Color.FromArgb(
                    255, record.Red, record.Green, record.Blue)),
                Content = new Border
                {
                    Width = 18,
                    Height = 18,
                    CornerRadius = new CornerRadius(3),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Colors.Black) { Opacity = 0.25 },
                },
            };
            colorButton.Click += OnColorClicked;
            ToolTipService.SetToolTip(colorButton, $"档位 {stage + 1} 灯光颜色");
            Grid.SetColumn(colorButton, 4);

            var useButton = new Button
            {
                Content = isActive ? "当前档位" : "设为当前",
                IsEnabled = supported && !isActive && !App.Context.IsBusy,
                Tag = stage,
                MinWidth = 96,
            };
            useButton.Click += OnUseStageClicked;
            Grid.SetColumn(useButton, 5);

            row.Children.Add(check);
            row.Children.Add(label);
            row.Children.Add(dpiBox);
            row.Children.Add(unit);
            row.Children.Add(colorButton);
            row.Children.Add(useButton);

            StageRows.Children.Add(row);
            _enabledBoxes.Add(check);
            _dpiBoxes.Add(dpiBox);
            _colorButtons.Add(colorButton);
        }
    }

    private void BuildRateButtons(DeviceSnapshot snapshot)
    {
        RateButtons.Children.Clear();

        for (int index = 0; index < G3mCommands.PollingRates.Length; index++)
        {
            bool isActive = index == snapshot.Profile.PollingRateIndex;

            var button = new Button
            {
                Content = isActive ? $"{G3mCommands.PollingRates[index]} Hz · 当前" : $"{G3mCommands.PollingRates[index]} Hz",
                Tag = index,
                IsEnabled = !isActive && !App.Context.IsBusy,
            };

            if (isActive)
            {
                button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            }

            button.Click += async (sender, _) =>
            {
                if (sender is Button { Tag: int rate })
                {
                    await App.Context.SetPollingRateAsync(rate);
                }
            };

            RateButtons.Children.Add(button);
        }
    }

    private void OnStageEnabledChanged(object sender, RoutedEventArgs e) => MarkDirty();

    private void OnColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int stage } button || _draft is null)
        {
            return;
        }

        DpiStage record = _draft.GetStage(stage);
        var picker = new ColorPicker
        {
            Color = Color.FromArgb(255, record.Red, record.Green, record.Blue),
            IsAlphaEnabled = false,
            IsColorChannelTextInputVisible = true,
            IsHexInputVisible = true,
        };

        var flyout = new Flyout { Content = picker };
        picker.ColorChanged += (_, args) =>
        {
            _draft?.SetStage(stage, record with
            {
                Red = args.NewColor.R,
                Green = args.NewColor.G,
                Blue = args.NewColor.B,
            });

            button.Background = new SolidColorBrush(args.NewColor);
            MarkDirty();
        };

        flyout.ShowAt(button);
    }

    private async void OnUseStageClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int stage })
        {
            await App.Context.SetActiveStageAsync(stage);
        }
    }

    private void MarkDirty() => DpiHint.Text = "有未应用的修改，点「应用 DPI 配置」写入鼠标";

    private async void OnApplyDpiClicked(object sender, RoutedEventArgs e)
    {
        if (_draft is null || App.Context.Snapshot is not { } snapshot)
        {
            return;
        }

        // 把界面上的启用状态与数值收进草稿，未启用的档位不改动其 DPI 值。
        for (int stage = 0; stage < G3mCommands.MaxDpiStages; stage++)
        {
            bool enabled = _enabledBoxes[stage].IsChecked == true;
            _draft.SetStage(stage, _draft.GetStage(stage) with { Enabled = enabled });

            double value = _dpiBoxes[stage].Value;
            if (!double.IsNaN(value))
            {
                _draft.SetStageDpi(snapshot.Header, stage, (int)Math.Round(value));
            }
        }

        await App.Context.WriteProfileAsync(_draft);
    }

    private void OnResetDpiClicked(object sender, RoutedEventArgs e) => Rebuild();
}
