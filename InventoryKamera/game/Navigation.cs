using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using WindowsInput;
using WindowsInput.Native;

namespace InventoryKamera
{
    public static class Navigation
	{
		private static NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		internal static InputSimulator sim = new InputSimulator();
		private static RECT WindowSize;
		private static RECT WindowPosition;
		private static Size AspectRatio;
		public static bool IsNormal { get; private set; }

        private static double delay = 1;

		public static VirtualKeyCode escapeKey = VirtualKeyCode.ESCAPE;
		public static VirtualKeyCode characterKey = VirtualKeyCode.VK_C;
		public static VirtualKeyCode inventoryKey = VirtualKeyCode.VK_B;
		public static VirtualKeyCode slotOneKey = VirtualKeyCode.VK_1;

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool GetClientRect(IntPtr hWnd, ref RECT Rect);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool ClientToScreen(IntPtr hWnd, ref RECT Rect);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool GetWindowRect(IntPtr hWnd, ref RECT Rect);

		[DllImport("user32.dll")]
		private static extern uint GetDpiForWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern IntPtr GetThreadDpiAwarenessContext();

		[DllImport("user32.dll")]
		private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr dpiContext);

		public static void Initialize()
		{
			var executables = Properties.Settings.Default.Executables;
			foreach (var processName in executables)
			{
				Logger.Debug("Checking for {0}.exe", processName);
				if (InitializeProcess(processName, out IntPtr handle))
				{
					// Get area and position. WindowPosition must be zeroed immediately before this
					// ClientToScreen call: it treats WindowPosition's current value as the client-space
					// point to convert, so if Initialize() runs twice without an intervening Reset()
					// (as it does: PreflightChecksPass() calls it, then the scan thread calls it again),
					// a stale already-converted screen coordinate gets converted a second time,
					// compounding the window's offset. This was invisible in fullscreen (origin ~(0,0),
					// doubling has no effect) but broke windowed mode outright (captured region shifted
					// by roughly the window's own screen offset).
					WindowPosition = new RECT();
					ClientToScreen(handle, ref WindowPosition);
					GetClientRect(handle, ref WindowSize);

					RECT windowRect = new RECT();
					GetWindowRect(handle, ref windowRect);
					uint gameDpi = GetDpiForWindow(handle);
					int ourAwareness = GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext());
					Logger.Debug("DPI diagnostics: our process awareness={0} (2=PerMonitorAware, 3=PerMonitorAwareV2), game window DPI={1}, GetWindowRect=({2},{3})-({4},{5})",
						ourAwareness, gameDpi, windowRect.Left, windowRect.Top, windowRect.Right, windowRect.Bottom);

					try
					{
						AspectRatio = GetAspectRatio();
					}
					catch (DivideByZeroException)
					{
						throw new NotImplementedException("Genshin window could not be focused. Please make sure the game is visible.");
					}
					catch (Exception)
					{
						throw;
					}

					Logger.Debug("Found {0}.exe", processName);
					Logger.Debug("Window location ({0}x{1}): x={2}, y={3}", WindowSize.Width, WindowSize.Height, WindowPosition.Left, WindowPosition.Top);
					return;
				}
				Logger.Debug("Could not find {0}.exe", processName);
			}

			throw new NullReferenceException("Cannot find Genshin Impact process");
		}

		public static void Reset()
		{
			WindowSize = new RECT();
			WindowPosition = new RECT();
			AspectRatio = new Size();
			sim = new InputSimulator();
		}

		#region Window Capturing

		/// <summary>
		/// Max real window height before capture output gets downscaled. Above this, every capture
		/// (grid detection AND the OCR crops taken from real-coordinate regions) works with fewer
		/// pixels -- Kirsch edge detection, blob connected-component labeling, per-pixel filters, and
		/// Tesseract itself are all O(pixel count), and at 4K a region that's e.g. 5% of screen width
		/// is proportionally 4x more pixels than the same UI text at 1080p for no recognition benefit.
		/// 1.0 (no downscaling) at or below this height.
		/// </summary>
		private const int MaxCaptureHeight = 1080;

		/// <summary>
		/// Ratio applied to every <see cref="CaptureWindow"/>/<see cref="CaptureRegion(RECT, PixelFormat)"/>
		/// result relative to the real window. Every consumer either crops proportionally within an
		/// already-captured bitmap or computes its region from <see cref="GetWidth"/>/
		/// <see cref="GetHeight"/> directly, both of which stay correct automatically since capture
		/// position (not just size) is still sourced from the real screen.
		/// </summary>
		public static double CaptureScale => Math.Min(1.0, MaxCaptureHeight / (double)GetHeight());

		// Scale per-call, based on the captured bitmap's own height, not CaptureScale (which reflects
		// the whole real window). Most CaptureRegion crops are small UI-element regions (nameplate,
		// stat lines, item counts) that stay well under MaxCaptureHeight even at 4K -- resizing those
		// costs more (bitmap allocation + a high-quality bicubic blit) than it saves (they're already
		// too small for downscaling to meaningfully cut per-pixel filter/OCR time), and doing it on
		// every one of the many small captures a scan makes measurably slowed scans down. Only
		// genuinely large captures -- chiefly the whole-window CaptureWindow() used for grid detection,
		// whose raw height always equals GetHeight() and therefore exactly matches CaptureScale -- are
		// worth downscaling.
		private static Bitmap DownscaleCapture(Bitmap source)
		{
			if (source.Height <= MaxCaptureHeight) return source;

			double scale = MaxCaptureHeight / (double)source.Height;
			int width = (int)(source.Width * scale);
			int height = MaxCaptureHeight;
			var downscaled = new Bitmap(width, height, source.PixelFormat);
			using (var g = Graphics.FromImage(downscaled))
			{
				// Bilinear, not HighQualityBicubic -- this runs on every large capture in the scan's
				// hottest paths (once per item, not once per page), and bicubic's extra quality buys
				// nothing OCR/detection care about at this downscale ratio while costing meaningfully
				// more CPU per call.
				g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
				g.DrawImage(source, 0, 0, width, height);
			}
			source.Dispose();
			return downscaled;
		}

		public static Bitmap CaptureWindow(PixelFormat format = PixelFormat.Format24bppRgb)
		{
			Bitmap bmp = new Bitmap(GetWidth(), GetHeight(), format);
			using (Graphics gfxBmp = Graphics.FromImage(bmp))
			{
				Logger.Debug("CaptureWindow: capturing {0}x{1} from screen position ({2},{3})",
					bmp.Width, bmp.Height, GetPosition().Left, GetPosition().Top);
				gfxBmp.CopyFromScreen(GetPosition().Left, GetPosition().Top, 0, 0, bmp.Size);

				var uidRegion = new RECT(
					Left: (int)( 1070 / 1280.0 * bmp.Width ),
					Top: (int)( 695 / 720.0 * bmp.Height ),
					Right: bmp.Width,
					Bottom: bmp.Height);
				gfxBmp.FillRectangle(new SolidBrush(Color.Black), uidRegion);
			}
			return DownscaleCapture(bmp);
		}

		public static Bitmap CaptureRegion(RECT region, PixelFormat format = PixelFormat.Format24bppRgb)
		{
			Bitmap bmp = new Bitmap(region.Width, region.Height, format);
			using (Graphics gfxBmp = Graphics.FromImage(bmp))
			{
				gfxBmp.CopyFromScreen(GetPosition().Left + region.Left, GetPosition().Top + region.Top, 0, 0, bmp.Size);
			}
			return DownscaleCapture(bmp);
		}

		public static Bitmap CaptureRegion(int x, int y, int width, int height, PixelFormat format = PixelFormat.Format24bppRgb)
		{
			return CaptureRegion(new Rectangle(x, y, width, height), format);
		}

		#endregion Window Capturing

		#region Image Displaying

		public static void DisplayBitmap(Bitmap bm, string text = "Image")
		{
			Form form = new Form();
			
			int padding = 5;

			form.StartPosition = FormStartPosition.Manual;
			form.Location = Screen.PrimaryScreen.WorkingArea.Location;
			form.Size = new Size(bm.Width + 5*padding, bm.Height + 10*padding);
			form.Text = text;
			form.BackColor = Color.Black;

			PictureBox pb = new PictureBox
			{
				Dock = DockStyle.Fill,
				Image = bm,
				Padding = new Padding(5),
				//Size = new Size(bm.Width + 2*padding, bm.Height + 2*padding),
			};

			form.Controls.Add(pb);
			Application.Run(form);
			
		}

		#endregion Image Displaying

		#region Window Size Accessing

		public static Size GetSize()
		{
			return WindowSize.Size;
		}

		public static Size GetAspectRatio()
		{
			if (!AspectRatio.IsEmpty) return AspectRatio;

			if (WindowSize.Width == 0) throw new DivideByZeroException("Genshin's window width cannot be 0");
			if (WindowSize.Height == 0) throw new DivideByZeroException("Genshin's window height cannot be 0");
			int x = WindowSize.Width/GCD(WindowSize.Width, WindowSize.Height);
			int y = WindowSize.Height/GCD(WindowSize.Width, WindowSize.Height);
			var size = new Size(x, y);
			
			IsNormal = size == new Size(16, 9);

			return size;
		}

		private static int GCD(int a, int b)
		{
			int r;
			while (b != 0)
			{
				r = a % b;
				a = b;
				b = r;
			}
			return a;
		}

		public static int GetWidth()
		{
			return WindowSize.Width;
		}

		public static int GetHeight()
		{
			return WindowSize.Height;
		}

		public static RECT GetPosition()
		{
			return WindowPosition;
		}

		#endregion Window Size Accessing

		#region Window Focusing

		[DllImport("user32.dll")]
		private static extern int SetForegroundWindow(IntPtr hwnd);

		[DllImport("user32.dll")]
		private static extern bool GetWindowPlacement(IntPtr hWnd, ref WindowPlacement lpwndpl);
		
		[DllImport("user32.dll")]
		private static extern bool SetWindowPlacement(IntPtr hWnd, ref WindowPlacement lpwndpl);
		
		private struct WindowPlacement
		{
			public int length;
			public int flags;
			public ShowWindowEnum showCmd;
			public Point ptMinPosition;
			public Point ptMaxPosition;
			public Rectangle rcNormalPosition;
		}
		
		private enum ShowWindowEnum
		{
			Hide = 0,
			ShowNormal = 1, ShowMinimized = 2, ShowMaximized = 3,
			Maximize = 3, ShowNormalNoActivate = 4, Show = 5,
			Minimize = 6, ShowMinNoActivate = 7, ShowNoActivate = 8,
			Restore = 9, ShowDefault = 10, ForceMinimized = 11
		};

		public static bool InitializeProcess(string processName, out IntPtr handle)
		{
			handle = IntPtr.Zero;
			// Get process
			using (Process genshin = Process.GetProcessesByName(processName).FirstOrDefault())
			{
				// check if the process is running
				if (genshin != null)
				{
					handle = genshin.MainWindowHandle;
					
					var windowPlacement = new WindowPlacement{length = Marshal.SizeOf(typeof(WindowPlacement))};
					GetWindowPlacement(handle, ref windowPlacement);

					// Check if minimized
					if (windowPlacement.showCmd == ShowWindowEnum.ShowMinimized)
					{
						windowPlacement.showCmd = ShowWindowEnum.ShowNormal;
						SetWindowPlacement(handle, ref windowPlacement);
					}

					// Bring game to front
					SetForegroundWindow(handle);
					return true;
				}
			}
			return false;
		}

		#endregion Window Focusing


        #region Delays

        public static void SystemWait(Speed speed = Speed.Normal)
		{
			double value;
			switch (speed)
			{
				case Speed.Fastest:
					value = 10;
					break;

				case Speed.Faster:
					value = 75;
					break;

				case Speed.Fast:
					value = 100;
					break;

				case Speed.Normal:
					value = 500;
					break;

				case Speed.Slow:
					value = 750;
					break;

				case Speed.Slower:
					value = 1000;
					break;

				case Speed.Slowest:
					value = 2000;
					break;

				case Speed.CharacterUI:
					value = 2000;
					break;

				case Speed.ArtifactIgnore:
					value = 80;
					break;

				case Speed.UI:
					value = 2000;
					break;

				case Speed.InventoryScroll:
					value = 10;
					break;

				case Speed.SelectNextInventoryItem:
					value = 200;
					break;

				default:
					value = 1000;
					break;
			}
			value *= delay;

			Wait(((int)value));
		}

		public static void SystemWait(float ms)
        {
			Wait((int)(ms * delay));
        }

		public static void Wait(int ms = 1000)
		{
			Thread.Sleep(ms);
		}

		public static void SetDelay(double _delay)
		{
			delay = _delay;
		}

		public static double GetDelay()
		{
			return delay;
		}

        internal static void ClearArtifactFilters()
        {
            var x = (IsNormal ? 0.0875 : 0.0868) * GetWidth();
			var y = (IsNormal ? 0.9389 : 0.9444) * GetHeight();

			for (var i = 0; i < 2; ++i)
			{
				Click((int)x, (int)y);
				SystemWait(Speed.Normal);
			}
			sim.Keyboard.KeyPress(escapeKey);
			SystemWait(Speed.Fast);
        }

		internal static void ChangeArtifactSortObtained()
		{
            var x = 0.6437 * GetWidth();
            var y = (IsNormal ? 0.1278 : 0.1150) * GetHeight();

            Click((int)x, (int)y);

            SystemWait(Speed.Slow);
        }

        public enum Speed
		{
			Slowest,
			Slower,
			Slow,
			Normal,
			Fast,
			Faster,
			Fastest,
			UI,
			ArtifactIgnore,
			SelectNextInventoryItem,
			InventoryScroll,
			CharacterUI,
		}

		#endregion Delays
	}
}