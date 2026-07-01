using System.Linq;
using InventoryKamera;
using Vortice.DXGI;
using Xunit;
using Xunit.Abstractions;

namespace InventoryKamera.Tests
{
    /// <summary>
    /// Smoke test for HdrDetector against whatever displays are actually attached to the machine
    /// running the tests. There's no way to assert a specific expected value (HDR on/off depends on
    /// the machine's real display settings, which this test doesn't control), so this only verifies
    /// the Win32 QueryDisplayConfig/DisplayConfigGetDeviceInfo interop doesn't throw and returns a
    /// value -- correctness of "yes/no" was cross-checked manually against this development machine's
    /// actual Display Settings > HDR toggle while writing this. See [[hdr-overlay-root-cause]].
    /// </summary>
    public class HdrDetectorTests
    {
        private readonly ITestOutputHelper output;

        public HdrDetectorTests(ITestOutputHelper output) => this.output = output;

        [Fact]
        public void IsHdrEnabledOnAnyDisplay_DoesNotThrow_AndReportsAValue()
        {
            bool result = HdrDetector.IsHdrEnabledOnAnyDisplay();
            output.WriteLine($"HDR enabled on any display: {result}");
            // No assertion on the value itself -- it reflects this machine's real display settings.
        }

        [Fact]
        public void RawDxgiOutputEnumeration_ReportsPlausibleColorSpaces()
        {
            // Diagnostic: dumps every attached output's raw DXGI colour space, so the boolean above
            // can be cross-checked against something concrete rather than trusted blindly.
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            int outputsSeen = 0;

            for (int a = 0; factory.EnumAdapters1(a, out IDXGIAdapter1 adapter).Success; a++)
            {
                using (adapter)
                {
                    output.WriteLine($"Adapter {a}: {adapter.Description1.Description}");
                    for (int o = 0; adapter.EnumOutputs(o, out IDXGIOutput dxgiOutput).Success; o++)
                    {
                        using (dxgiOutput)
                        {
                            using var output6 = dxgiOutput.QueryInterfaceOrNull<IDXGIOutput6>();
                            if (output6 == null)
                            {
                                output.WriteLine($"  Output {o}: no IDXGIOutput6 support");
                                continue;
                            }
                            var desc = output6.Description1;
                            output.WriteLine($"  Output {o}: {desc.DeviceName}, ColorSpace={desc.ColorSpace}, BitsPerColor={desc.BitsPerColor}");
                            outputsSeen++;
                        }
                    }
                }
            }

            Assert.True(outputsSeen > 0, "Expected at least one DXGI output on a machine with an interactive desktop session.");
        }
    }
}
