using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace InventoryKamera
{
    /// <summary>
    /// The original capture backend: a GDI BitBlt of the desktop (<see cref="Graphics.CopyFromScreen"/>)
    /// at the window's screen coordinates. Kept as the default (safest, best-tested) backend and as
    /// the fallback / A-B baseline for <see cref="WgcScreenCapture"/>. Does not exclude overlays and
    /// is affected by HDR tone-mapping — see [[hdr-overlay-root-cause]].
    /// </summary>
    internal sealed class GdiScreenCapture : IScreenCapture
    {
        public Bitmap CaptureWindow(IntPtr windowHandle, RECT clientScreenPosition, Size clientSize, PixelFormat format)
        {
            var bmp = new Bitmap(clientSize.Width, clientSize.Height, format);
            using (Graphics gfxBmp = Graphics.FromImage(bmp))
            {
                gfxBmp.CopyFromScreen(clientScreenPosition.Left, clientScreenPosition.Top, 0, 0, bmp.Size);
            }
            return bmp;
        }

        public Bitmap CaptureRegion(IntPtr windowHandle, RECT clientScreenPosition, RECT region, PixelFormat format)
        {
            var bmp = new Bitmap(region.Width, region.Height, format);
            using (Graphics gfxBmp = Graphics.FromImage(bmp))
            {
                gfxBmp.CopyFromScreen(clientScreenPosition.Left + region.Left, clientScreenPosition.Top + region.Top, 0, 0, bmp.Size);
            }
            return bmp;
        }
    }
}
