using G3M.Core.History;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace G3M.Settings.Controls;

/// <summary>
/// 电量曲线。只用内置的 <see cref="Polyline"/> 与 <see cref="Line"/> 自绘，
/// 不引入任何图表库。
/// </summary>
/// <remarks>
/// 两个已知的 WinUI 陷阱在这里被显式处理：
/// <list type="number">
/// <item><see cref="Canvas"/> 放在 Grid 里不会自动拉伸，必须在 SizeChanged 里显式设定尺寸；</item>
/// <item><see cref="Polyline.Points"/> 不可观测，绑定不会刷新，只能在代码里重建。</item>
/// </list>
/// 另外：出现观测断点的位置会把曲线拆成多条，绝不跨过没有数据的区间连线。
/// </remarks>
public sealed class BatteryChart : Grid
{
    private const double LeftMargin = 44;
    private const double RightMargin = 12;
    private const double TopMargin = 10;
    private const double BottomMargin = 26;

    private readonly Canvas _plot = new();
    private readonly Border _readout;
    private readonly TextBlock _readoutText = new() { FontSize = 12 };

    private IReadOnlyList<BatteryBucket> _buckets = [];
    private DateTimeOffset _from;
    private DateTimeOffset _to;

    public BatteryChart()
    {
        _readoutText.Foreground = new SolidColorBrush(Colors.White);
        _readout = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 32, 32, 32)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Child = _readoutText,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        Children.Add(_plot);
        Children.Add(_readout);

        MinHeight = 220;
        SizeChanged += (_, _) => Redraw();
        PointerMoved += OnPointerMoved;
        PointerExited += (_, _) => _readout.Visibility = Visibility.Collapsed;
    }

    /// <summary>渲染一段区间的电量曲线。</summary>
    public void Render(IReadOnlyList<BatteryBucket> buckets, DateTimeOffset from, DateTimeOffset to)
    {
        _buckets = buckets;
        _from = from;
        _to = to;
        Redraw();
    }

    private void Redraw()
    {
        _plot.Children.Clear();

        double width = ActualWidth;
        double height = ActualHeight;

        // Canvas 不会随 Grid 拉伸，必须自己设定尺寸。
        _plot.Width = width;
        _plot.Height = height;

        if (width <= LeftMargin + RightMargin || height <= TopMargin + BottomMargin || _buckets.Count == 0)
        {
            return;
        }

        double plotWidth = width - LeftMargin - RightMargin;
        double plotHeight = height - TopMargin - BottomMargin;

        DrawGridLines(plotWidth, plotHeight);
        DrawBands(plotWidth, plotHeight);
        DrawAverageLine(plotWidth, plotHeight);
        DrawTimeAxis(plotWidth, plotHeight);
    }

    private double XFor(int index, double plotWidth) =>
        LeftMargin + (plotWidth * index / Math.Max(1, _buckets.Count - 1));

    private double YFor(int percent, double plotHeight) =>
        TopMargin + (plotHeight * (100 - Math.Clamp(percent, 0, 100)) / 100.0);

    private void DrawGridLines(double plotWidth, double plotHeight)
    {
        var brush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));

        foreach (int percent in new[] { 0, 25, 50, 75, 100 })
        {
            double y = YFor(percent, plotHeight);

            _plot.Children.Add(new Line
            {
                X1 = LeftMargin,
                Y1 = y,
                X2 = LeftMargin + plotWidth,
                Y2 = y,
                Stroke = brush,
                StrokeThickness = 1,
            });

            var label = new TextBlock
            {
                Text = percent.ToString(),
                FontSize = 11,
                Opacity = 0.6,
            };
            Canvas.SetLeft(label, 8);
            Canvas.SetTop(label, y - 8);
            _plot.Children.Add(label);
        }
    }

    /// <summary>把连续有数据的区间画成一条最低-最高的色带，视觉上比单线更有厚度。</summary>
    private void DrawBands(double plotWidth, double plotHeight)
    {
        var fill = new SolidColorBrush(Color.FromArgb(38, 0, 120, 212));
        int start = -1;

        for (int index = 0; index <= _buckets.Count; index++)
        {
            bool usable = index < _buckets.Count && _buckets[index].HasData && !_buckets[index].HasGap;

            if (usable && start < 0)
            {
                start = index;
            }

            if (usable || start < 0)
            {
                continue;
            }

            AddBand(start, index - 1);
            start = -1;
        }

        void AddBand(int first, int last)
        {
            if (last <= first)
            {
                return;
            }

            var figure = new PathFigure { IsClosed = true, IsFilled = true };

            figure.Segments.Add(new PolyLineSegment
            {
                Points = [.. Enumerable.Range(first, last - first + 1)
                    .Select(i => new Point(XFor(i, plotWidth), YFor(_buckets[i].Max, plotHeight)))],
            });

            figure.Segments.Add(new PolyLineSegment
            {
                Points = [.. Enumerable.Range(first, last - first + 1).Reverse()
                    .Select(i => new Point(XFor(i, plotWidth), YFor(_buckets[i].Min, plotHeight)))],
            });

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            // Path 需要限定命名空间：System.IO.Path 与本类型同名。
            _plot.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Fill = fill });
        }
    }

    private void DrawAverageLine(double plotWidth, double plotHeight)
    {
        var stroke = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212));
        int start = -1;

        for (int index = 0; index <= _buckets.Count; index++)
        {
            bool usable = index < _buckets.Count && _buckets[index].HasData && !_buckets[index].HasGap;

            if (usable && start < 0)
            {
                start = index;
            }

            if (usable || start < 0)
            {
                continue;
            }

            AddSegment(start, index - 1);
            start = -1;
        }

        void AddSegment(int first, int last)
        {
            if (last < first)
            {
                return;
            }

            var line = new Polyline
            {
                Stroke = stroke,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
            };

            for (int i = first; i <= last; i++)
            {
                line.Points.Add(new Point(XFor(i, plotWidth), YFor(_buckets[i].Average, plotHeight)));
            }

            _plot.Children.Add(line);
        }
    }

    private void DrawTimeAxis(double plotWidth, double plotHeight)
    {
        const int tickCount = 5;
        TimeSpan span = _to - _from;

        for (int tick = 0; tick <= tickCount; tick++)
        {
            DateTimeOffset moment = _from + (span * tick / tickCount);
            double x = LeftMargin + (plotWidth * tick / tickCount);

            var label = new TextBlock
            {
                Text = span.TotalHours <= 25
                    ? moment.LocalDateTime.ToString("HH:mm")
                    : moment.LocalDateTime.ToString("MM-dd HH:mm"),
                FontSize = 11,
                Opacity = 0.6,
            };

            Canvas.SetLeft(label, Math.Clamp(x - 20, 0, Math.Max(0, plotWidth - 20)));
            Canvas.SetTop(label, TopMargin + plotHeight + 6);
            _plot.Children.Add(label);
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_buckets.Count == 0)
        {
            return;
        }

        double plotWidth = ActualWidth - LeftMargin - RightMargin;
        if (plotWidth <= 0)
        {
            return;
        }

        Point position = e.GetCurrentPoint(this).Position;
        double ratio = (position.X - LeftMargin) / plotWidth;
        int index = (int)Math.Round(ratio * (_buckets.Count - 1));

        if (index < 0 || index >= _buckets.Count || !_buckets[index].HasData)
        {
            _readout.Visibility = Visibility.Collapsed;
            return;
        }

        BatteryBucket bucket = _buckets[index];
        _readoutText.Text =
            $"{bucket.Start.LocalDateTime:MM-dd HH:mm}   {bucket.Average}%  " +
            $"(最低 {bucket.Min} / 最高 {bucket.Max})";

        _readout.Visibility = Visibility.Visible;
        _readout.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tooltipWidth = _readout.DesiredSize.Width;

        Canvas.SetLeft(_readout, 0);
        Canvas.SetTop(_readout, 0);

        _readout.Margin = new Thickness(
            Math.Clamp(position.X - (tooltipWidth / 2), 0, Math.Max(0, ActualWidth - tooltipWidth)),
            Math.Max(0, position.Y - 40),
            0,
            0);
    }
}
