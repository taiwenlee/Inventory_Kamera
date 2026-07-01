using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace InventoryKamera
{
    /// <summary>
    /// Captures pixels from the game window. <see cref="Navigation"/> delegates to an
    /// implementation of this instead of calling GDI directly, so the capture backend can be swapped
    /// (see <see cref="GdiScreenCapture"/>, <see cref="WgcScreenCapture"/>) without touching any of
    /// the scraper code that consumes captured bitmaps. See [[hdr-overlay-root-cause]] for why this
    /// exists: GDI's CopyFromScreen photographs the desktop, so overlays composited over the game are
    /// captured verbatim and HDR is tone-mapped to SDR before capture, shifting every pixel value
    /// against the app's hard-coded SDR calibration.
    /// </summary>
    internal interface IScreenCapture
    {
        /// <summary>Capture the full client area of a window.</summary>
        /// <param name="windowHandle">The game window's HWND.</param>
        /// <param name="clientScreenPosition">The client area's top-left corner, in screen coordinates.</param>
        /// <param name="clientSize">The client area's size.</param>
        Bitmap CaptureWindow(IntPtr windowHandle, RECT clientScreenPosition, Size clientSize, PixelFormat format);

        /// <summary>Capture a sub-region of a window's client area.</summary>
        /// <param name="windowHandle">The game window's HWND.</param>
        /// <param name="clientScreenPosition">The client area's top-left corner, in screen coordinates.</param>
        /// <param name="region">The region to capture, relative to the client area's top-left.</param>
        Bitmap CaptureRegion(IntPtr windowHandle, RECT clientScreenPosition, RECT region, PixelFormat format);
    }
}
