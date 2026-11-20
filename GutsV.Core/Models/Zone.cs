namespace GutsV.Core.Models;

public enum Zone
{
    Left = 0,
    Center = 1,
    Right = 2,
    All = 3,
    // Added for Acer Nitro / Clevo boards (see feedback/PROTOCOL.md, PROTOCOL_ZONE4.md)
    Zone4 = 4,    // 4th keyboard section (4-zone boards, KBType 7/23)
    Logo = 5,     // LOGO LED
    LightBar = 6  // light bar (3-part boards: code 0xF3; KBType 23: zone 5)
}
