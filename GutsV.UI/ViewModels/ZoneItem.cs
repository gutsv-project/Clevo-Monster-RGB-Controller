using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using CommunityToolkit.Mvvm.ComponentModel;
using GutsV.Core.Models;

namespace GutsV.UI.ViewModels;

/// <summary>One lighting zone as shown in the UI: selectable, with its own preview color.</summary>
public partial class ZoneItem : ObservableObject
{
    public ZoneItem(ZoneSlot slot)
    {
        Zone = slot.Zone;
        DisplayName = slot.DisplayName;
        IsKeyboardSection = slot.IsKeyboardSection;
        KeyColumns = slot.KeyColumns;
        Keys = Enumerable.Range(0, 5 * slot.KeyColumns).ToList();
    }

    public Zone Zone { get; }
    public string DisplayName { get; }
    // true: a column block of the keyboard picture; false: Light Bar / Logo indicator
    public bool IsKeyboardSection { get; }
    public int KeyColumns { get; }
    // Width of the block in the 600 px preview: same key width in every section (15 columns total)
    public double PreviewWidth => KeyColumns * 36;
    // One entry per drawn key (5 rows x KeyColumns)
    public IReadOnlyList<int> Keys { get; }

    [ObservableProperty]
    private bool _isSelected = true;

    // Color last sent to the keyboard for this zone (static apply or animation frame)
    public Color AppliedColor { get; set; } = Colors.Gray;

    // Color drawn in the preview
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayBrush))]
    private Color _displayColor = Colors.Gray;

    public SolidColorBrush DisplayBrush => new(DisplayColor);
}
