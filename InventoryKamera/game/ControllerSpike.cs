using System;
using System.Threading;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace InventoryKamera.game
{
    /// <summary>
    /// Throwaway feasibility spike for Phase 3 §6c (scan-input revamp) -- confirms whether a
    /// ViGEmBus-backed virtual Xbox 360 controller is usable on this machine before committing to
    /// building the real controller-driven navigation on top of it. Not wired into the scan
    /// pipeline; remove once the feasibility question is answered.
    /// </summary>
    public static class ControllerSpike
    {
        /// <summary>
        /// Plugs in a virtual Xbox 360 controller, waits <paramref name="alttabSeconds"/> seconds
        /// (so the caller can alt-tab into the target game/window -- the virtual device only means
        /// anything to whichever window has focus when the input arrives), nudges the left stick
        /// and taps the A button, then unplugs. Returns a human-readable result so the caller can
        /// show it to the user without needing to know anything about ViGEm's exception types.
        /// </summary>
        public static string TapAButton(int alttabSeconds = 4)
        {
            try
            {
                using var client = new ViGEmClient();
                IXbox360Controller controller = client.CreateXbox360Controller();
                controller.Connect();

                // Give the user time to alt-tab into the target window -- some games only switch
                // to "controller scheme" on sustained analog input, not just a single button press,
                // so we nudge the left stick too, not just tap a face button.
                Thread.Sleep(alttabSeconds * 1000);

                controller.SetAxisValue(Xbox360Axis.LeftThumbX, short.MaxValue / 2);
                controller.SetButtonState(Xbox360Button.A, true);
                Thread.Sleep(400);
                controller.SetButtonState(Xbox360Button.A, false);
                controller.SetAxisValue(Xbox360Axis.LeftThumbX, 0);

                controller.Disconnect();
                return "Success: virtual Xbox 360 controller connected, nudged the left stick, and sent an A button press.";
            }
            catch (VigemBusNotFoundException)
            {
                return "Failed: ViGEmBus driver is not installed. Install it from " +
                       "https://github.com/ViGEm/ViGEmBus/releases and try again.";
            }
            catch (Exception ex)
            {
                return $"Failed: {ex.GetType().Name}: {ex.Message}";
            }
        }
    }
}
