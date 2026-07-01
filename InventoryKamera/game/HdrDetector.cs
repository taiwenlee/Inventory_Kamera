using Vortice.DXGI;

namespace InventoryKamera
{
    /// <summary>
    /// Detects whether Windows HDR ("Use HDR" in Display settings) is enabled on any attached
    /// display. Pre-flight check for the GDI capture backend: GDI's CopyFromScreen reads back an SDR
    /// tone-mapped approximation of an HDR desktop, which shifts every pixel value against the app's
    /// hard-coded SDR calibration (grayscale/threshold constants, rarity colour matching, etc.) and
    /// silently corrupts scans. See [[hdr-overlay-root-cause]].
    ///
    /// Uses DXGI output enumeration (already a dependency for WgcScreenCapture) rather than the raw
    /// Win32 QueryDisplayConfig/DisplayConfigGetDeviceInfo APIs: those need several deeply nested,
    /// easy-to-mis-layout structs (DISPLAYCONFIG_PATH_INFO, DISPLAYCONFIG_MODE_INFO's 64-byte union,
    /// etc.) hand-declared via P/Invoke, and a struct-size mistake there is a native memory-corruption
    /// crash, not a catchable .NET exception -- confirmed by trial (an early version of this file
    /// crashed the test host outright). DXGI's IDXGIOutput6.Description1.ColorSpace is strongly typed
    /// through Vortice, with no hand-rolled layout risk.
    /// </summary>
    internal static class HdrDetector
    {
        /// <summary>
        /// True if any attached display currently has HDR (DXGI's "advanced color" / HDR10 colour
        /// space) active. Fails safe (returns false, never throws) since this is a best-effort
        /// pre-flight warning, not a hard requirement.
        /// </summary>
        internal static bool IsHdrEnabledOnAnyDisplay()
        {
            try
            {
                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

                for (int adapterIndex = 0; factory.EnumAdapters1(adapterIndex, out IDXGIAdapter1 adapter).Success; adapterIndex++)
                {
                    using (adapter)
                    {
                        for (int outputIndex = 0; adapter.EnumOutputs(outputIndex, out IDXGIOutput output).Success; outputIndex++)
                        {
                            using (output)
                            {
                                using var output6 = output.QueryInterfaceOrNull<IDXGIOutput6>();
                                if (output6 == null) continue;

                                var colorSpace = output6.Description1.ColorSpace;
                                if (colorSpace == ColorSpaceType.RgbFullG2084NoneP2020)
                                    return true;
                            }
                        }
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
