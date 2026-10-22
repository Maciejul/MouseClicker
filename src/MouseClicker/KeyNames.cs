using System.Runtime.InteropServices;
using System.Text;

namespace MouseClicker;

/// <summary>Display names for Windows virtual-key codes (the numbers stored in the settings file).</summary>
internal static class KeyNames
{
    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameTextW(int lParam, StringBuilder buffer, int size);

    private static readonly Dictionary<int, string> Named = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x10] = "Shift", [0x11] = "Ctrl", [0x12] = "Alt",
        [0x13] = "Pause", [0x14] = "Caps Lock", [0x1B] = "Esc", [0x20] = "Space", [0x21] = "Page Up", [0x22] = "Page Down",
        [0x23] = "End", [0x24] = "Home", [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
        [0x2C] = "Print Screen", [0x2D] = "Insert", [0x2E] = "Delete", [0x5B] = "Left Win", [0x5C] = "Right Win",
        [0x90] = "Num Lock", [0x91] = "Scroll Lock", [0xA0] = "Left Shift", [0xA1] = "Right Shift",
        [0xA2] = "Left Ctrl", [0xA3] = "Right Ctrl", [0xA4] = "Left Alt", [0xA5] = "Right Alt",
        [0x6A] = "Num *", [0x6B] = "Num +", [0x6D] = "Num -", [0x6E] = "Num .", [0x6F] = "Num /",
        [0x01] = "Left button", [0x02] = "Right button", [0x04] = "Middle button", [0x05] = "X1 button", [0x06] = "X2 button",
    };

    public static string Name(int vk)
    {
        if (Named.TryGetValue(vk, out string? named)) return named;
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x60 and <= 0x69) return "Num " + (vk - 0x60);
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);

        try
        {
            uint scan = MapVirtualKeyW((uint)vk, 0);
            var text = new StringBuilder(64);
            if (scan != 0 && GetKeyNameTextW((int)(scan << 16), text, text.Capacity) > 0) return text.ToString();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Fall through to the code below.
        }

        return $"Key {vk}";
    }
}
