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
            SetDwmColorAttribute(form.Handle, DWMWA_CAPTION_COLOR, Background);
            SetDwmColorAttribute(form.Handle, DWMWA_BORDER_COLOR, Accent);
            SetDwmColorAttribute(form.Handle, DWMWA_TEXT_COLOR, TitleTextColor);
        }

        private static void SetDwmColorAttribute(System.IntPtr hwnd, int attribute, Color color)
        {
            int colorRef = color.R | (color.G << 8) | (color.B << 16);
            DwmSetWindowAttribute(hwnd, attribute, ref colorRef, sizeof(int));
        }
    }
}
