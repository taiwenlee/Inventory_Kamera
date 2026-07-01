using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace InventoryKamera
{
    /// <summary>
    /// Captures the game window's own frames via Windows.Graphics.Capture instead of photographing
    /// the desktop. Unlike <see cref="GdiScreenCapture"/>, this excludes overlays composited over the
    /// window and captures the window's true rendered content regardless of HDR — see
    /// [[hdr-overlay-root-cause]] for the root-cause writeup and [[wgc-interop-patterns]] for the
    /// interop details this implementation relies on (manual vtable dispatch for
    /// IGraphicsCaptureItemInterop, the correct IGraphicsCaptureItem IID, Vortice's NativePointer,
    /// and the message-pump requirement below).
    ///
    /// WGC frames arrive on whichever thread created the capture session, and only if that thread
    /// pumps Win32 messages (there's no DispatcherQueue on a plain Win32/WinForms thread the way
    /// there is in a UWP app). Since Navigation.CaptureRegion/CaptureWindow are called synchronously,
    /// frequently, and from a scan thread that isn't pumping messages itself, this class runs its own
    /// dedicated background thread that owns the D3D11 device, the capture session, and a Win32
    /// message loop for the session's lifetime. FrameArrived continuously refreshes a cached "latest
    /// frame" bitmap under a lock; CaptureRegion/CaptureWindow just read and crop that cache rather
    /// than synchronously waiting for a fresh frame on every call.
    /// </summary>
    internal sealed class WgcScreenCapture : IScreenCapture, IDisposable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        // GetWindowRect includes an invisible resize-border margin Windows 10/11 adds around windows
        // for hit-testing, which isn't part of what's actually rendered/composited -- and therefore
        // doesn't match what WGC captures (confirmed empirically: a captured frame came back several
        // pixels smaller per side than GetWindowRect reported). DwmGetWindowAttribute with
        // DWMWA_EXTENDED_FRAME_BOUNDS gives the true visible bounds instead. See [[wgc-interop-patterns]].
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint msgFilterMin, uint msgFilterMax, uint removeMsg);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG msg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG msg);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX, ptY;
        }

        [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            void CreateForWindow([In] IntPtr window, [In] ref Guid iid, out IntPtr result);
            void CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid, out IntPtr result);
        }

        [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDirect3DDxgiInterfaceAccess
        {
            IntPtr GetInterface([In] ref Guid iid);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateForWindowDelegate(IntPtr thisPtr, IntPtr window, ref Guid iid, out IntPtr result);

        // IGraphicsCaptureItem's interface GUID -- NOT typeof(GraphicsCaptureItem).GUID (that's the
        // CLR runtime class type and is all-zeros). The type is internal in the generated projection,
        // so this has to be a literal. See [[wgc-interop-patterns]].
        private static readonly Guid IGraphicsCaptureItemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

        private IntPtr _windowHandle;
        private Thread _pumpThread;
        private volatile bool _stopRequested;

        private readonly object _frameLock = new object();
        private Bitmap _latestFrame;
        private Exception _initError;
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);

        public Bitmap CaptureWindow(IntPtr windowHandle, RECT clientScreenPosition, Size clientSize, PixelFormat format)
        {
            EnsureStarted(windowHandle);
            return CaptureFromCache(windowHandle, clientScreenPosition, new RECT(0, 0, clientSize.Width, clientSize.Height), format);
        }

        public Bitmap CaptureRegion(IntPtr windowHandle, RECT clientScreenPosition, RECT region, PixelFormat format)
        {
            EnsureStarted(windowHandle);
            return CaptureFromCache(windowHandle, clientScreenPosition, region, format);
        }

        private Bitmap CaptureFromCache(IntPtr windowHandle, RECT clientScreenPosition, RECT region, PixelFormat format)
        {
            Bitmap frame;
            lock (_frameLock)
            {
                if (_latestFrame == null)
                {
                    throw new InvalidOperationException(
                        _initError != null
                            ? $"Windows.Graphics.Capture failed to start: {_initError.Message}"
                            : "Windows.Graphics.Capture has not produced a frame yet.");
                }
                frame = _latestFrame;

                // Crop from window-local coordinates (WGC captures the full window, title bar and
                // borders included) to the requested client-relative region. GetWindowRect gives the
                // full window's screen bounds; clientScreenPosition is the client area's screen
                // origin (already tracked by Navigation via ClientToScreen) -- the difference is the
                // client area's offset within the captured frame.
                DwmGetWindowAttribute(windowHandle, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT windowRect, Marshal.SizeOf<RECT>());
                int clientOffsetX = clientScreenPosition.Left - windowRect.Left;
                int clientOffsetY = clientScreenPosition.Top - windowRect.Top;

                var cropRect = new Rectangle(
                    clientOffsetX + region.Left,
                    clientOffsetY + region.Top,
                    region.Width,
                    region.Height);

                // Clamp to the captured frame's bounds in case of a transient size mismatch (e.g. the
                // window was resized between the last frame and this call).
                cropRect.Intersect(new Rectangle(0, 0, frame.Width, frame.Height));
                if (cropRect.Width <= 0 || cropRect.Height <= 0)
                    throw new InvalidOperationException("Requested capture region is outside the captured window frame.");

                using (var cropped = frame.Clone(cropRect, frame.PixelFormat))
                {
                    return format == cropped.PixelFormat ? (Bitmap)cropped.Clone() : cropped.Clone(new Rectangle(0, 0, cropped.Width, cropped.Height), format);
                }
            }
        }

        private void EnsureStarted(IntPtr windowHandle)
        {
            if (_pumpThread != null && _windowHandle == windowHandle && _pumpThread.IsAlive) return;

            Stop();

            _windowHandle = windowHandle;
            _stopRequested = false;
            _initError = null;
            _ready.Reset();

            _pumpThread = new Thread(() => RunCaptureThread(windowHandle))
            {
                IsBackground = true,
                Name = "WgcScreenCapture"
            };
            _pumpThread.SetApartmentState(ApartmentState.STA);
            _pumpThread.Start();

            if (!_ready.Wait(TimeSpan.FromSeconds(5)))
                throw new InvalidOperationException("Windows.Graphics.Capture did not start within 5 seconds.");
            if (_initError != null)
                throw new InvalidOperationException("Windows.Graphics.Capture failed to start.", _initError);
        }

        private void RunCaptureThread(IntPtr windowHandle)
        {
            ID3D11Device d3dDevice = null;
            IDXGIDevice dxgiDevice = null;
            Direct3D11CaptureFramePool framePool = null;
            GraphicsCaptureSession session = null;

            try
            {
                var activationFactoryRef = WinRT.ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
                var interopIid = typeof(IGraphicsCaptureItemInterop).GUID;
                int qiHr = Marshal.QueryInterface(activationFactoryRef.ThisPtr, ref interopIid, out IntPtr interopPtr);
                if (qiHr != 0 || interopPtr == IntPtr.Zero)
                    throw new InvalidOperationException($"QueryInterface(IGraphicsCaptureItemInterop) failed: 0x{qiHr:X8}");

                IntPtr vtbl = Marshal.ReadIntPtr(interopPtr, 0);
                IntPtr createForWindowSlot = Marshal.ReadIntPtr(vtbl, 3 * IntPtr.Size);
                var createForWindow = Marshal.GetDelegateForFunctionPointer<CreateForWindowDelegate>(createForWindowSlot);

                var itemIid = IGraphicsCaptureItemIid;
                int createHr = createForWindow(interopPtr, windowHandle, ref itemIid, out IntPtr itemPtr);
                if (createHr != 0 || itemPtr == IntPtr.Zero)
                    throw new InvalidOperationException($"CreateForWindow failed: 0x{createHr:X8}");

                var item = GraphicsCaptureItem.FromAbi(itemPtr);

                D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                    null, out d3dDevice);
                dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>();

                int devHr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr winrtDevicePtr);
                if (devHr != 0)
                    throw new InvalidOperationException($"CreateDirect3D11DeviceFromDXGIDevice failed: 0x{devHr:X8}");
                var winrtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(winrtDevicePtr);

                framePool = Direct3D11CaptureFramePool.Create(
                    winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
                session = framePool.CreateCaptureSession(item);

                framePool.FrameArrived += (sender, args) => OnFrameArrived(sender, d3dDevice);

                session.StartCapture();
                _ready.Set();

                // Message pump: FrameArrived is delivered via a message posted to this thread.
                while (!_stopRequested)
                {
                    while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, 1 /* PM_REMOVE */))
                    {
                        TranslateMessage(ref msg);
                        DispatchMessage(ref msg);
                    }
                    Thread.Sleep(2);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "WgcScreenCapture failed to initialize");
                _initError = ex;
                _ready.Set();
            }
            finally
            {
                session?.Dispose();
                framePool?.Dispose();
                dxgiDevice?.Dispose();
                d3dDevice?.Dispose();
            }
        }

        private unsafe void OnFrameArrived(Direct3D11CaptureFramePool sender, ID3D11Device d3dDevice)
        {
            using var frame = sender.TryGetNextFrame();
            if (frame == null) return;

            var access = frame.Surface.As<IDirect3DDxgiInterfaceAccess>();
            var texGuid = typeof(ID3D11Texture2D).GUID;
            IntPtr texPtr = access.GetInterface(ref texGuid);
            using var sourceTexture = new ID3D11Texture2D(texPtr);
            var desc = sourceTexture.Description;

            var stagingDesc = desc;
            stagingDesc.Usage = ResourceUsage.Staging;
            stagingDesc.BindFlags = BindFlags.None;
            stagingDesc.CPUAccessFlags = CpuAccessFlags.Read;
            stagingDesc.MiscFlags = ResourceOptionFlags.None;
            using var staging = d3dDevice.CreateTexture2D(stagingDesc);

            using var context = d3dDevice.ImmediateContext;
            context.CopyResource(staging, sourceTexture);

            var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                var bmp = new Bitmap(desc.Width, desc.Height, PixelFormat.Format32bppArgb);
                var bmpData = bmp.LockBits(new Rectangle(0, 0, desc.Width, desc.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte* src = (byte*)mapped.DataPointer;
                    byte* dst = (byte*)bmpData.Scan0;
                    int rowBytes = desc.Width * 4;
                    for (int y = 0; y < desc.Height; y++)
                    {
                        Buffer.MemoryCopy(src + y * mapped.RowPitch, dst + y * bmpData.Stride, rowBytes, rowBytes);
                    }
                }
                finally
                {
                    bmp.UnlockBits(bmpData);
                }

                lock (_frameLock)
                {
                    _latestFrame?.Dispose();
                    _latestFrame = bmp;
                }
            }
            finally
            {
                context.Unmap(staging, 0);
            }
        }

        private void Stop()
        {
            if (_pumpThread == null) return;

            _stopRequested = true;
            _pumpThread.Join(TimeSpan.FromSeconds(2));
            _pumpThread = null;

            lock (_frameLock)
            {
                _latestFrame?.Dispose();
                _latestFrame = null;
            }
        }

        public void Dispose() => Stop();
    }
}
