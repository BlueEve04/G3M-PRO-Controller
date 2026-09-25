using G3M.Core.Keys;
using G3M.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace G3M.Settings.Views;

/// <summary>
/// 按键映射页（只读）。读取完全可用，写入在 320F:706E 固件上无效，
/// 因此这里只展示当前分配，不做编辑。
/// </summary>
/// <remarks>
/// 槽位含义不靠序号猜：直接把设备上读到的当前映射显示出来，
/// 用户凭"这个键现在是什么功能"就能对上号，无论固件槽位顺序如何都不会认错。
/// </remarks>
public sealed partial class ButtonsPage : Page
{
    public ButtonsPage()
    {
        InitializeComponent();

        App.Context.Changed += OnContextChanged;
        Unloaded += (_, _) => App.Context.Changed -= OnContextChanged;

        Render();
    }

    private void OnContextChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(Render);

    private void Render()
    {
        DeviceSnapshot? snapshot = App.Context.Snapshot;

        SlotRows.Children.Clear();
        EmptySlotRows.Children.Clear();

        if (snapshot is null)
        {
            Hint.Text = "连接设备后可用";
            EmptySlotsHeader.Text = "未分配的槽位";
            EmptySlotsExpander.Visibility = Visibility.Collapsed;
            return;
        }

        KeyMappingTable mapping = snapshot.KeyMapping;
        Hint.Text = $"设备共 {mapping.SlotCount} 个槽位；下面列出当前已分配功能的按键";

        int assigned = 0;
        int empty = 0;

        for (int slot = 0; slot < mapping.SlotCount; slot++)
        {
            KeyAction action = mapping.GetSlot(slot);
            if (action.IsEmpty)
            {
                empty++;
                EmptySlotRows.Children.Add(BuildRow(slot, action, dim: true));
            }
            else
            {
                assigned++;
                SlotRows.Children.Add(BuildRow(slot, action, dim: false));
            }
        }

        if (assigned == 0)
        {
            SlotRows.Children.Add(new TextBlock
            {
                Text = "这台设备当前没有已分配的按键。",
                Opacity = 0.7,
            });
        }

        EmptySlotsHeader.Text = $"未分配的槽位（{empty} 个）";
        EmptySlotsExpander.Visibility = empty > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Grid BuildRow(int slot, KeyAction action, bool dim)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = $"槽位 {slot}",
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = dim ? 0.5 : 0.7,
        };
        Grid.SetColumn(label, 0);

        var description = new TextBlock
        {
            Text = action.Describe(),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = dim ? 0.5 : 1.0,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(description, 1);

        var bytes = new TextBlock
        {
            Text = $"{action.Byte0:X2} {action.Byte1:X2} {action.Byte2:X2}",
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.45,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
        };
        Grid.SetColumn(bytes, 2);

        row.Children.Add(label);
        row.Children.Add(description);
        row.Children.Add(bytes);
        return row;
    }
}
