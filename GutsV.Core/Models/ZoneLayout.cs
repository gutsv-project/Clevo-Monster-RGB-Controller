namespace GutsV.Core.Models;

/// <summary>How a zone is presented: Acer Control Center naming and preview layout.</summary>
public readonly record struct ZoneSlot(Zone Zone, string DisplayName, bool IsKeyboardSection, int KeyColumns);

public static class ZoneLayout
{
    public const int KeyboardColumns = 15;

    /// <summary>
    /// Zones shown for a keyboard (Section 1-4 / Left-Center-Right, Light Bar, Logo LED).
    /// A keyboard with a single zone gets one "Keyboard" entry mapped to Zone.All.
    /// Keyboard sections split the 15 preview columns left to right.
    /// </summary>
    public static List<ZoneSlot> Describe(IReadOnlyList<Zone> driverZones)
    {
        var sections = driverZones.Where(z => z is Zone.Left or Zone.Center or Zone.Right or Zone.Zone4).ToList();
        var slots = new List<ZoneSlot>();
        if (sections.Count <= 1)
        {
            slots.Add(new ZoneSlot(Zone.All, "Keyboard", true, KeyboardColumns));
        }
        else
        {
            bool fourZone = sections.Contains(Zone.Zone4);
            int baseCols = KeyboardColumns / sections.Count;
            int extra = KeyboardColumns - baseCols * sections.Count; // leftmost sections get the remainder
            for (int i = 0; i < sections.Count; i++)
            {
                string name = fourZone ? $"Section {i + 1}" : sections[i] switch
                {
                    Zone.Left => "Left",
                    Zone.Center => "Center",
                    _ => "Right"
                };
                slots.Add(new ZoneSlot(sections[i], name, true, baseCols + (i < extra ? 1 : 0)));
            }
        }
        if (driverZones.Contains(Zone.LightBar)) slots.Add(new ZoneSlot(Zone.LightBar, "Light Bar", false, 0));
        if (driverZones.Contains(Zone.Logo)) slots.Add(new ZoneSlot(Zone.Logo, "Logo LED", false, 0));
        return slots;
    }

    /// <summary>
    /// Zones of the layout that a frame / write for <paramref name="target"/> changes.
    /// Zone.All covers every zone; on a single-zone keyboard Left also drives the whole keyboard.
    /// </summary>
    public static IEnumerable<Zone> Affected(IReadOnlyList<ZoneSlot> layout, Zone target)
    {
        bool single = layout.Count > 0 && layout[0].Zone == Zone.All;
        foreach (var s in layout)
        {
            if (target == Zone.All || s.Zone == target || (single && s.Zone == Zone.All && target == Zone.Left))
                yield return s.Zone;
        }
    }
}
