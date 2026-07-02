using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace InventoryKamera.ui
{
    /// <summary>
    /// Shared color palette and helpers for the app's flat, Claude-Desktop-inspired visual style
    /// (warm cream background, terracotta accent, pill-shaped buttons).
    /// </summary>
    public static class UiTheme
    {
        public static readonly Color Background = Color.FromArgb(245, 244, 237);
        public static readonly Color Accent = Color.FromArgb(204, 120, 92);
        public static readonly Color BorderColor = Color.FromArgb(222, 216, 205);
        private static readonly Color TitleTextColor = Color.FromArgb(61, 56, 51);
        private static readonly Color TextColor = Color.FromArgb(33, 31, 28);
        private static readonly Color SurfaceColor = Color.White;

        private static readonly Color DarkBackground = Color.FromArgb(32, 31, 29);
        private static readonly Color DarkBorderColor = Color.FromArgb(72, 69, 64);
        private static readonly Color DarkTitleTextColor = Color.FromArgb(230, 227, 220);
        private static readonly Color DarkTextColor = Color.FromArgb(214, 210, 202);
        private static readonly Color DarkSurfaceColor = Color.FromArgb(45, 44, 41);

        /// <summary>
        /// Live setting read, not a snapshot -- matches <c>ScanSettings</c>' reasoning
        /// (Phase 2 §2.3): callers that check this mid-session (e.g. right after the user toggles the
        /// Dark Mode menu item) must see the change immediately.
        /// </summary>
        public static bool IsDarkMode => Properties.Settings.Default.DarkMode;

        public static Color CurrentBackground => IsDarkMode ? DarkBackground : Background;
        public static Color CurrentBorderColor => IsDarkMode ? DarkBorderColor : BorderColor;
        public static Color CurrentTitleTextColor => IsDarkMode ? DarkTitleTextColor : TitleTextColor;
        public static Color CurrentTextColor => IsDarkMode ? DarkTextColor : TextColor;

        /// <summary>Background for "sunken" input surfaces (TextBox/NumericUpDown/ComboBox) -- white in light mode, a lighter panel shade in dark mode (not the same as the page background, so inputs stay visually distinct).</summary>
        public static Color CurrentSurfaceColor => IsDarkMode ? DarkSurfaceColor : SurfaceColor;

        /// <summary>Clips a control to a rounded-rectangle region, giving it pill-shaped corners.</summary>
        public static void RoundCorners(Control control, int radius)
        {
            var rect = new Rectangle(0, 0, control.Width, control.Height);
            int d = radius * 2;

            using var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            control.Region = new Region(path);
        }

        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(System.IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        /// <summary>
        /// Tints the native title bar and window border to match the warm palette. Requires
        /// Windows 11 22H2+ (DWMWA_CAPTION_COLOR/BORDER_COLOR/TEXT_COLOR); DwmSetWindowAttribute
        /// just returns a failure HRESULT on older Windows, so this silently no-ops there and the
        /// window keeps its default native chrome.
        /// </summary>
        public static void ApplyWindowChromeTint(Form form)
        {
            SetDwmColorAttribute(form.Handle, DWMWA_CAPTION_COLOR, CurrentBackground);
            SetDwmColorAttribute(form.Handle, DWMWA_BORDER_COLOR, Accent);
            SetDwmColorAttribute(form.Handle, DWMWA_TEXT_COLOR, CurrentTitleTextColor);
        }

        private static void SetDwmColorAttribute(System.IntPtr hwnd, int attribute, Color color)
        {
            int colorRef = color.R | (color.G << 8) | (color.B << 16);
            DwmSetWindowAttribute(hwnd, attribute, ref colorRef, sizeof(int));
        }

        /// <summary>
        /// Recursively applies the current (light/dark, per <see cref="IsDarkMode"/>) palette to
        /// every control under <paramref name="root"/>, plus the native title bar. Call once after
        /// <c>InitializeComponent()</c> in a form's constructor, and again whenever the user toggles
        /// Dark Mode while that form is already open. Deliberately does not touch controls that carry
        /// their own semantic/state-driven color (e.g. <c>ProgramStatus_Label</c>'s green/red,
        /// <c>SettingsForm</c>'s duplicate-name-warning yellow textboxes) -- those set their own
        /// <c>ForeColor</c>/<c>BackColor</c> from application logic, and re-run after this on their
        /// own event, so a blanket overwrite here would just be immediately clobbered anyway. Accent
        /// buttons (round-cornered, <see cref="Accent"/>-colored) are skipped for the same reason:
        /// their color is deliberate, not a default that theming should touch.
        /// </summary>
        public static void ApplyTheme(Form form)
        {
            form.BackColor = CurrentBackground;
            form.ForeColor = CurrentTextColor;
            ApplyThemeRecursive(form.Controls);
            // Accessing form.Handle (inside ApplyWindowChromeTint) force-creates it if it doesn't
            // exist yet -- matches the original constructor-time ApplyWindowChromeTint call this
            // replaced, which relied on exactly that side effect to tint the title bar before Show().
            ApplyWindowChromeTint(form);
        }

        /// <summary>
        /// Controls with a deliberate, hand-picked color baked into the Designer that must survive
        /// theming untouched regardless of light/dark mode -- e.g. <c>ScannerOutput_Panel</c>'s dark
        /// preview-card background with light text on top of it (a fixed-dark "photo frame" look, not
        /// meant to flip with the app's own theme), <c>ErrorLog_TextBox</c>'s red-on-default styling,
        /// or <c>StartScan_Button</c>'s blue call-to-action color. A first pass at this (2026-07-05)
        /// blanket-recolored every control by type with no exclusions and broke every one of these:
        /// the dark preview panel's white-on-dark labels went black-on-now-light, the red error log
        /// text went theme-default (nearly black on a light-mode error background), and the menu strip
        /// wasn't touched by this pass at all (see <see cref="ApplyMenuStripTheme"/> for that one).
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string> ExcludedFromTheming = new System.Collections.Generic.HashSet<string>
        {
            "ScannerOutput_Panel",
            "WeaponArtifact_Label",
            "WeaponArtifactOutput_TextBox_Label",
            "CharacterOutput_TextBox_Label",
            "Character_Label",
            "ErrorLog_TextBox",
            "ErrorLog_Label",
            "ErrorReport_Label",
            "ProgramStatus_Label",
            "StartScan_Button",
        };

        private static void ApplyThemeRecursive(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                if (!ExcludedFromTheming.Contains(control.Name))
                {
                    switch (control)
                    {
                        case FlatGroupBox groupBox:
                            groupBox.BackColor = CurrentBackground;
                            groupBox.ForeColor = CurrentTitleTextColor;
                            break;

                        case GroupBox groupBox:
                            groupBox.BackColor = CurrentBackground;
                            groupBox.ForeColor = CurrentTitleTextColor;
                            break;

                        case TextBox textBox:
                            textBox.BackColor = CurrentSurfaceColor;
                            textBox.ForeColor = CurrentTextColor;
                            break;

                        case NumericUpDown numericUpDown:
                            numericUpDown.BackColor = CurrentSurfaceColor;
                            numericUpDown.ForeColor = CurrentTextColor;
                            break;

                        case ComboBox comboBox:
                            comboBox.BackColor = CurrentSurfaceColor;
                            comboBox.ForeColor = CurrentTextColor;
                            break;

                        // Accent-colored action buttons (identified by BackColor already set to
                        // Accent at Designer time) keep their own color in both themes; everything
                        // else (plain buttons like Close/Cancel) gets a themed surface + border.
                        case Button button when button.BackColor != Accent:
                            button.BackColor = CurrentSurfaceColor;
                            button.ForeColor = CurrentTextColor;
                            button.FlatAppearance.BorderColor = CurrentBorderColor;
                            break;

                        case CheckBox checkBox:
                            checkBox.ForeColor = CurrentTextColor;
                            break;

                        case Panel panel:
                            panel.BackColor = CurrentBackground;
                            break;

                        case MenuStrip menuStrip:
                            ApplyMenuStripTheme(menuStrip);
                            break;

                        case Label label when !(label is LinkLabel):
                            label.ForeColor = CurrentTextColor;
                            break;
                    }
                }

                if (control.HasChildren) ApplyThemeRecursive(control.Controls);
            }
        }

        /// <summary>
        /// MenuStrip/ToolStripMenuItem aren't part of the regular <see cref="Control"/> tree walked
        /// by <see cref="ApplyThemeRecursive"/> in the way that matters for rendering -- ToolStrip
        /// controls render through a <see cref="ToolStripRenderer"/>, not per-control BackColor/
        /// ForeColor, so a top-level MenuStrip and (critically) its dropdown menus stay white
        /// regardless of the form's own theme unless a custom renderer is installed.
        /// </summary>
        private static void ApplyMenuStripTheme(MenuStrip menuStrip)
        {
            menuStrip.Renderer = new ToolStripProfessionalRenderer(new MenuColorTable());
            menuStrip.BackColor = CurrentBackground;
            menuStrip.ForeColor = CurrentTextColor;
            ApplyMenuItemsTheme(menuStrip.Items);
        }

        private static void ApplyMenuItemsTheme(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                item.ForeColor = CurrentTextColor;
                if (item is ToolStripDropDownItem dropDown)
                {
                    dropDown.DropDown.BackColor = CurrentBackground;
                    dropDown.DropDown.ForeColor = CurrentTextColor;
                    ApplyMenuItemsTheme(dropDown.DropDownItems);
                }
            }
        }

        private sealed class MenuColorTable : ProfessionalColorTable
        {
            public override Color MenuStripGradientBegin => CurrentBackground;
            public override Color MenuStripGradientEnd => CurrentBackground;
            public override Color ToolStripDropDownBackground => CurrentBackground;
            public override Color ImageMarginGradientBegin => CurrentBackground;
            public override Color ImageMarginGradientMiddle => CurrentBackground;
            public override Color ImageMarginGradientEnd => CurrentBackground;
            public override Color MenuItemSelected => Accent;
            public override Color MenuItemSelectedGradientBegin => Accent;
            public override Color MenuItemSelectedGradientEnd => Accent;
            public override Color MenuItemBorder => Accent;
            public override Color MenuBorder => CurrentBorderColor;
            public override Color SeparatorDark => CurrentBorderColor;
            public override Color SeparatorLight => CurrentBorderColor;
        }
    }
}
