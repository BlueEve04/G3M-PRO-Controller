using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace G3M.Tray;

/// <summary>
/// 按电量动态绘制托盘图标，并缓存结果。
/// </summary>
/// <remarks>
/// GDI 句柄是有配额的，长时间运行的程序泄漏 HICON 最终会导致整个桌面绘制异常，
/// 因此这里遵守三条纪律：
/// <list type="number">
/// <item><c>Bitmap.GetHicon()</c> 得到的句柄由调用方负责，必须 <c>DestroyIcon</c>；</item>
/// <item><c>Icon.FromHandle</c> 不接管所有权，克隆一份再销毁原句柄；</item>
/// <item>替换下来的旧图标延迟一拍再释放，避免外壳仍在引用它。</item>
/// </list>
/// 另外按 10% 分档缓存，轮询不会每次都重新绘制。
/// </remarks>
internal sealed class BatteryIconRenderer : IDisposable
{
    private const int BucketSize = 10;
    private const int MaxCacheEntries = 48;

    private readonly Dictionary<CacheKey, Icon> _cache = [];
    private readonly Queue<CacheKey> _order = new();
    private bool _disposed;

    /// <summary>按电量和状态取图标。同一分档直接命中缓存。</summary>
    public Icon Get(int percent, bool charging, bool hasError, int sizePixels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int bucket = hasError ? -1 : Math.Clamp(percent / BucketSize, 0, 10);
        var key = new CacheKey(bucket, charging, hasError, sizePixels);

        if (_cache.TryGetValue(key, out Icon? cached))
        {
            return cached;
        }

        Icon icon = Render(bucket, charging, hasError, sizePixels);
        _cache[key] = icon;
        _order.Enqueue(key);

        while (_order.Count > MaxCacheEntries)
        {
            CacheKey evicted = _order.Dequeue();
            if (_cache.Remove(evicted, out Icon? stale))
            {
                stale.Dispose();
            }
        }

        return icon;
    }

    private static Icon Render(int bucket, bool charging, bool hasError, int sizePixels)
    {
        using var bitmap = new Bitmap(sizePixels, sizePixels, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            Draw(graphics, sizePixels, bucket, charging, hasError);
        }

        // GetHicon 返回的句柄归调用方所有，Clone 之后必须销毁，否则每次重绘都会漏一个 GDI 句柄。
        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void Draw(
        Graphics graphics, int size, int bucket, bool charging, bool hasError)
    {
        float scale = size / 16f;

        // 电池外壳：留出右侧正极凸点的位置。
        float bodyLeft = 1f * scale;
        float bodyTop = 3.5f * scale;
        float bodyRight = size - (4f * scale);
        float bodyBottom = size - (3.5f * scale);
        float nubLeft = bodyRight + (0.8f * scale);
        float nubRight = size - (1.2f * scale);
        float nubTop = (size / 2f) - (2f * scale);
        float nubBottom = (size / 2f) + (2f * scale);

        Color outline = hasError ? Color.FromArgb(160, 160, 160) : Color.FromArgb(70, 70, 70);
        using var outlinePen = new Pen(outline, Math.Max(1f, 1.1f * scale));

        // 正极凸点
        using (var nubBrush = new SolidBrush(outline))
        {
            graphics.FillRectangle(nubBrush, nubLeft, nubTop, nubRight - nubLeft, nubBottom - nubTop);
        }

        // 外壳
        var body = new RectangleF(bodyLeft, bodyTop, bodyRight - bodyLeft, bodyBottom - bodyTop);
        using (var background = new SolidBrush(Color.FromArgb(232, 232, 232)))
        {
            graphics.FillRectangle(background, body);
        }

        graphics.DrawRectangle(outlinePen, bodyLeft, bodyTop, body.Width, body.Height);

        // 电量填充
        if (!hasError && bucket > 0)
        {
            float ratio = bucket / 10f;
            float inset = 1.4f * scale;
            float fullWidth = body.Width - (inset * 2);
            float fillWidth = Math.Max(1f, fullWidth * ratio);
            float fillHeight = body.Height - (inset * 2);

            Color fill = bucket switch
            {
                >= 5 => Color.FromArgb(76, 175, 80),
                >= 2 => Color.FromArgb(255, 193, 7),
                _ => Color.FromArgb(244, 67, 54),
            };

            using var fillBrush = new SolidBrush(fill);
            graphics.FillRectangle(
                fillBrush, body.Left + inset, body.Top + inset, fillWidth, fillHeight);
        }

        if (charging)
        {
            DrawBolt(graphics, size, scale);
        }

        if (hasError)
        {
            DrawCross(graphics, size, scale);
        }
    }

    private static void DrawBolt(Graphics graphics, int size, float scale)
    {
        // 白色闪电，带一圈深色描边保证在浅色填充上也看得清。
        PointF[] bolt =
        [
            new(size * 0.54f, size * 0.20f),
            new(size * 0.36f, size * 0.54f),
            new(size * 0.47f, size * 0.54f),
            new(size * 0.42f, size * 0.82f),
            new(size * 0.62f, size * 0.44f),
            new(size * 0.50f, size * 0.44f),
        ];

        using var shadow = new Pen(Color.FromArgb(150, 0, 0, 0), Math.Max(1.6f, 2.2f * scale))
        {
            LineJoin = LineJoin.Round,
        };
        using var stroke = new Pen(Color.White, Math.Max(1f, 1.3f * scale))
        {
            LineJoin = LineJoin.Round,
        };

        graphics.DrawPolygon(shadow, bolt);
        graphics.DrawPolygon(stroke, bolt);
    }

    private static void DrawCross(Graphics graphics, int size, float scale)
    {
        using var pen = new Pen(Color.FromArgb(220, 60, 60), Math.Max(1.4f, 1.8f * scale))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        graphics.DrawLine(pen, size * 0.22f, size * 0.30f, size * 0.78f, size * 0.78f);
        graphics.DrawLine(pen, size * 0.78f, size * 0.30f, size * 0.22f, size * 0.78f);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Icon icon in _cache.Values)
        {
            icon.Dispose();
        }

        _cache.Clear();
        _order.Clear();
    }

    private readonly record struct CacheKey(
        int Bucket, bool Charging, bool HasError, int SizePixels);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
