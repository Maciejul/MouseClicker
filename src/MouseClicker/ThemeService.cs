using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace MouseClicker;

/// <summary>
/// Light and dark colours. The window's brushes are app resources referenced with DynamicResource, so switching replaces the
/// brushes and every control repaints. Dark layers by lightness: window, then card, then field and button.
/// </summary>
internal static class ThemeService
{
    // (light, dark) per brush key. White text sits on the accent and success fills, so those stay the same in both.
    private static readonly (string Key, string Light, string Dark)[] Tokens =
    {
        ("WindowBg", "#F4F6F9", "#1B1D22"),
        ("CardBg", "#FFFFFF", "#24272D"),
        ("CardBorder", "#E2E6EC", "#3E434C"),
        ("TextBrush", "#1F2937", "#DCDFE4"),
        ("MutedBrush", "#6B7280", "#9CA4AF"),
        ("AccentBrush", "#2563EB", "#2563EB"),
        ("AccentTextBrush", "#2563EB", "#60A5FA"),
        ("AccentHoverBrush", "#1D4ED8", "#3B72F0"),
        ("AccentPressedBrush", "#1E40AF", "#1E4FD0"),
        ("ControlBrush", "#FFFFFF", "#2E323A"),
        ("ControlHoverBrush", "#EEF2FF", "#2B3A57"),
        ("ControlPressedBrush", "#E0E7FF", "#34476B"),
        ("FieldBrush", "#F1F3F5", "#2E323A"),
        ("FieldBorderBrush", "#AEB6C2", "#586070"),
        ("SuccessBrush", "#16A34A", "#16A34A"),
        ("SuccessHoverBrush", "#15803D", "#15803D"),
        ("SuccessPressedBrush", "#166534", "#166534"),
    };

    public static bool Dark { get; private set; }

    /// <summary>Replaces the colour brushes for the chosen palette. Call before the window exists, or any time after.</summary>
    public static void Apply(bool dark)
    {
        Dark = dark;
        var resources = System.Windows.Application.Current.Resources;
        foreach (var (key, light, darkColor) in Tokens)
        {
            var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(dark ? darkColor : light));
            brush.Freeze();
            resources[key] = brush;
        }
    }

    // ---- the native bits: title bar and the tray menu

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Dark or light title bar for a window that has a handle.</summary>
    public static void ApplyTitleBar(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        int on = Dark ? 1 : 0;
        try
        {
            // 20 on current Windows 10/11, 19 on early 2019 builds.
            if (DwmSetWindowAttribute(handle, 20, ref on, sizeof(int)) != 0) _ = DwmSetWindowAttribute(handle, 19, ref on, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // An old Windows without dark title bars keeps the light one.
        }
    }

    public static void StyleMenu(Forms.ContextMenuStrip menu)
    {
        menu.Renderer = Dark ? new DarkRenderer() : new Forms.ToolStripProfessionalRenderer();
        menu.BackColor = Dark ? Drawing.Color.FromArgb(36, 39, 45) : Drawing.SystemColors.Control;
        menu.ForeColor = Dark ? Drawing.Color.FromArgb(220, 223, 228) : Drawing.SystemColors.ControlText;
        foreach (Forms.ToolStripItem item in menu.Items) item.ForeColor = menu.ForeColor;
    }

    private sealed class DarkRenderer : Forms.ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkTable()) { }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Drawing.Color.FromArgb(220, 223, 228);
            base.OnRenderItemText(e);
        }
    }

    private sealed class DarkTable : Forms.ProfessionalColorTable
    {
        private static readonly Drawing.Color Back = Drawing.Color.FromArgb(36, 39, 45);
        private static readonly Drawing.Color Hover = Drawing.Color.FromArgb(48, 52, 60);
        private static readonly Drawing.Color Line = Drawing.Color.FromArgb(62, 67, 76);

        public DarkTable() { UseSystemColors = false; }

        public override Drawing.Color ToolStripDropDownBackground => Back;
        public override Drawing.Color MenuItemSelected => Hover;
        public override Drawing.Color MenuItemBorder => Line;
        public override Drawing.Color MenuBorder => Line;
        public override Drawing.Color ImageMarginGradientBegin => Back;
        public override Drawing.Color ImageMarginGradientMiddle => Back;
        public override Drawing.Color ImageMarginGradientEnd => Back;
        public override Drawing.Color SeparatorDark => Line;
        public override Drawing.Color SeparatorLight => Line;
        public override Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override Drawing.Color MenuItemPressedGradientBegin => Hover;
        public override Drawing.Color MenuItemPressedGradientEnd => Hover;
    }
}
