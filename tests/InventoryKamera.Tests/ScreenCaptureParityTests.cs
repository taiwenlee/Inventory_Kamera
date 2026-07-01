using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using InventoryKamera;
using Xunit;

namespace InventoryKamera.Tests
{
    /// <summary>
    /// Verifies WgcScreenCapture against a real window with a known pixel pattern. There's no live
    /// game session available in this environment (or in CI), so this spins up a real, controllable
    /// WinForms window with known colour blocks at known coordinates and checks the captured pixels
    /// against that ground truth directly.
    ///
    /// This was originally written as a GDI-vs-WGC A/B comparison (comparing the two backends'
    /// output against each other, per the modernization plan's original design), but GDI's
    /// CopyFromScreen proved unreliable for this specific kind of window (spawned by a background
    /// test process, not foreground-focused) on the machine these tests were developed on -- it
    /// captured unrelated desktop content instead of the window, while WGC captured the window
    /// perfectly and repeatably. Comparing against a known ground truth instead of a second
    /// possibly-unreliable capture method is a strictly stronger test anyway: it still validates
    /// window enumeration, the D3D11 readback pipeline, and the client-area crop math (see
    /// [[wgc-interop-patterns]]) without needing the actual game.
    ///
    /// It does NOT validate the HDR/overlay-exclusion behaviour itself, which needs a real HDR
    /// display / a real overlay and a live game session to observe.
    ///
    /// Skipped outside an interactive desktop session (e.g. a headless CI agent with no display),
    /// since window capture needs one.
    /// </summary>
    public class ScreenCaptureParityTests
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        static ScreenCaptureParityTests()
        {
            // The real app calls this in Program.cs before anything else; without it, a process gets
            // a DPI-virtualized (bitmap-scaled) view of the screen on a scaled display, which makes
            // window coordinates and GDI's CopyFromScreen disagree -- discovered the hard way when
            // this test's GDI capture came back solid white on this machine's 150%-scaled display.
            SetProcessDPIAware();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(IntPtr hWnd, ref RECT rect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ClientToScreen(IntPtr hWnd, ref RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        private static bool HasInteractiveDesktop()
        {
            try
            {
                return GetDesktopWindow() != IntPtr.Zero && GetWindowRect(GetDesktopWindow(), out _);
            }
            catch
            {
                return false;
            }
        }

        [Fact]
        public void WgcCapture_MatchesKnownTestPattern_OnARealWindow()
        {
            if (!HasInteractiveDesktop())
            {
                return; // headless environment; window capture needs a real desktop session.
            }

            using var form = new Form
            {
                Text = "ScreenCaptureParityTests target",
                StartPosition = FormStartPosition.Manual,
                Location = new Point(80, 80),
                Size = new Size(400, 300),
                FormBorderStyle = FormBorderStyle.FixedSingle,
            };
            // Known colour blocks at known client-relative coordinates -- the ground truth.
            var redRect = new Rectangle(10, 10, 100, 80);
            var greenRect = new Rectangle(120, 10, 100, 80);
            var blueRect = new Rectangle(230, 10, 100, 80);
            form.Paint += (s, e) =>
            {
                e.Graphics.Clear(Color.Black);
                e.Graphics.FillRectangle(Brushes.Red, redRect);
                e.Graphics.FillRectangle(Brushes.Lime, greenRect);
                e.Graphics.FillRectangle(Brushes.Blue, blueRect);
            };
            form.Show();
            Application.DoEvents();
            Thread.Sleep(300);
            Application.DoEvents();

            IntPtr hwnd = form.Handle;
            RECT clientPosition = default;
            RECT clientSize = default;
            ClientToScreen(hwnd, ref clientPosition);
            GetClientRect(hwnd, ref clientSize);
            var size = new Size(clientSize.Width, clientSize.Height);

            using var wgc = new WgcScreenCapture();
            Bitmap wgcFrame = null;

            // WGC's frame cache needs a moment to receive its first frame; retry briefly.
            Exception lastError = null;
            for (int attempt = 0; attempt < 20 && wgcFrame == null; attempt++)
            {
                try
                {
                    wgcFrame = wgc.CaptureWindow(hwnd, clientPosition, size, PixelFormat.Format32bppArgb);
                }
                catch (InvalidOperationException ex)
                {
                    lastError = ex;
                    Thread.Sleep(150);
                }
            }

            Assert.True(wgcFrame != null, $"WgcScreenCapture never produced a frame: {lastError}");

            using (wgcFrame)
            {
                Assert.Equal(size.Width, wgcFrame.Width);
                Assert.Equal(size.Height, wgcFrame.Height);

                AssertBlockColor(wgcFrame, redRect, Color.Red);
                AssertBlockColor(wgcFrame, greenRect, Color.Lime);
                AssertBlockColor(wgcFrame, blueRect, Color.Blue);

                // Background should be black outside the colour blocks.
                Color background = wgcFrame.GetPixel(10, 150);
                Assert.True(background.R < 20 && background.G < 20 && background.B < 20,
                    $"Expected black background, got {background}");
            }

            form.Close();
        }

        private static void AssertBlockColor(Bitmap frame, Rectangle block, Color expected)
        {
            Color center = frame.GetPixel(block.X + block.Width / 2, block.Y + block.Height / 2);
            Assert.True(
                Math.Abs(center.R - expected.R) < 10 && Math.Abs(center.G - expected.G) < 10 && Math.Abs(center.B - expected.B) < 10,
                $"Expected ~{expected} at block center, got {center}");
        }
    }
}
