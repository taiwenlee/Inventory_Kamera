using System;
using System.Threading;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using WindowsInput;

namespace InventoryKamera.game
{
    /// <summary>
    /// ViGEmBus-backed virtual Xbox 360 controller for Phase 3 §6c's controller-driven navigation.
    /// Unlike <see cref="ControllerSpike"/> (throwaway, connect-nudge-disconnect for a one-shot
    /// feasibility test), this holds the virtual device connected for the lifetime of the instance --
    /// reconnecting per action would add latency to every single navigation step across a scan.
    /// </summary>
    public sealed class GameController : IDisposable
    {
        private readonly ViGEmClient client;
        private readonly IXbox360Controller controller;
        private readonly InputSimulator inputSimulator = new InputSimulator();

        /// <summary>
        /// Human-readable failure reason if construction fails for a known cause (missing ViGEmBus
        /// driver); null if construction succeeded. Constructing rather than throwing on the expected
        /// "driver not installed" case keeps callers from needing a try/catch just to check
        /// availability -- see <see cref="ControllerSpike"/>'s equivalent handling.
        /// </summary>
        public string FailureReason { get; }

        public bool IsAvailable => FailureReason == null;

        public GameController()
        {
            try
            {
                client = new ViGEmClient();
                controller = client.CreateXbox360Controller();
                controller.Connect();
                // Windows/the game need a moment to actually enumerate the new virtual device after
                // Connect() returns -- sending input immediately risks the very first inputs being
                // dropped before Genshin has picked the controller up at all.
                Thread.Sleep(500);
            }
            catch (VigemBusNotFoundException)
            {
                FailureReason = "ViGEmBus driver is not installed. Install it from " +
                                 "https://github.com/ViGEm/ViGEmBus/releases and try again.";
            }
            catch (Exception ex)
            {
                FailureReason = $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        /// <summary>
        /// Genshin switches its UI prompt scheme (mouse/keyboard vs. controller icons) based on which
        /// input device it last saw activity from. A stick nudge alone was enough to flip the visible
        /// prompt icons but not enough for a subsequent Start press to actually open the menu
        /// (live-tested 2026-07-05) -- also tapping a face button matches the original feasibility
        /// spike's known-working sequence (stick nudge + A press). Requires the game to be focused and
        /// unpaused; there's no way to verify that from here, so callers must ensure it themselves.
        /// Caveat worth revisiting: A is Genshin's default interact/attack button, so calling this
        /// while free-roaming (not already in a menu) risks an unwanted in-game action -- a D-pad tap
        /// might be a safer scheme-switch trigger once one is confirmed to work equally well.
        /// </summary>
        public void EnterControllerMode()
        {
            RequireAvailable();
            controller.SetAxisValue(Xbox360Axis.LeftThumbX, short.MaxValue / 2);
            Thread.Sleep(150);
            controller.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
            Thread.Sleep(150);
            TapButton(Xbox360Button.A);
        }

        /// <summary>Opens Genshin's Esc-equivalent pause/menu screen (Start button).</summary>
        public void OpenMenu() => TapButton(Xbox360Button.Start);

        /// <summary>
        /// One discrete grid-navigation step in <paramref name="direction"/>: pushes the left stick
        /// fully in that direction, holds briefly, then releases back to center. Direction/timing
        /// unverified beyond the scheme-switch nudge this mirrors -- if steps get missed or
        /// double-counted in practice, tune <paramref name="holdMs"/>/<paramref name="settleMs"/>
        /// here rather than in every call site.
        /// </summary>
        public void MoveStep(MenuDirection direction, int holdMs = 150, int settleMs = 150)
        {
            RequireAvailable();
            switch (direction)
            {
                case MenuDirection.Up:
                    controller.SetAxisValue(Xbox360Axis.LeftThumbY, short.MaxValue);
                    break;
                case MenuDirection.Down:
                    controller.SetAxisValue(Xbox360Axis.LeftThumbY, short.MinValue);
                    break;
                case MenuDirection.Left:
                    controller.SetAxisValue(Xbox360Axis.LeftThumbX, short.MinValue);
                    break;
                case MenuDirection.Right:
                    controller.SetAxisValue(Xbox360Axis.LeftThumbX, short.MaxValue);
                    break;
            }
            Thread.Sleep(holdMs);
            controller.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
            controller.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
            Thread.Sleep(settleMs);
        }

        /// <summary>
        /// Moves <paramref name="steps"/> grid cells in <paramref name="direction"/> via repeated
        /// <see cref="MoveStep"/> calls -- horizontal and vertical moves are always issued as
        /// separate straight-line legs (never diagonally), matching how a 2D menu grid like Genshin's
        /// pause-menu tab bar is navigated one axis at a time. <paramref name="holdMs"/>/
        /// <paramref name="settleMs"/> default to <see cref="MoveStep"/>'s own defaults so existing
        /// callers are unaffected; pass explicit values to scale timing (e.g. via
        /// <c>InventoryScraper.ScaledControllerDelay</c>) without touching every call site.
        /// </summary>
        public void Move(MenuDirection direction, int steps, int holdMs = 150, int settleMs = 150)
        {
            for (int i = 0; i < steps; i++) MoveStep(direction, holdMs, settleMs);
        }

        public enum MenuDirection { Up, Down, Left, Right }

        public void TapButton(Xbox360Button button, int holdMs = 150)
        {
            RequireAvailable();
            controller.SetButtonState(button, true);
            Thread.Sleep(holdMs);
            controller.SetButtonState(button, false);
        }

        /// <summary>
        /// Repeatedly taps A (Genshin's back/cancel button -- swapped from standard Xbox convention,
        /// confirmed via live testing 2026-07-05: A cancels/backs out, B confirms/selects) to back
        /// out of however many menus deep navigation might have ended up -- a safety net for when a
        /// navigation sequence goes wrong (missed step, unexpected menu, etc.) and leaves the game in
        /// an unknown nested-menu state. Over-pressing past the top level is harmless (Genshin just
        /// ignores it once back at the main game/menu root), so this deliberately errs toward
        /// pressing more times than should be needed rather than trying to track exact menu depth.
        /// </summary>
        public void MashBack(int times = 6, int delayMs = 300)
        {
            RequireAvailable();
            for (int i = 0; i < times; i++)
            {
                TapButton(Xbox360Button.A);
                Thread.Sleep(delayMs);
            }
        }

        /// <summary>
        /// Switches Genshin back to keyboard/mouse control before the virtual controller disconnects.
        /// Disconnecting while Genshin still expects controller input (e.g. while a controller-driven
        /// menu is open) surfaces a blocking "controller disconnected, reconnect or exit" prompt
        /// in-game instead of gracefully falling back -- live-tested 2026-07-05. Backs out of any
        /// nested menus first (<see cref="MashBack"/>) as a safety net, then a net-zero mouse nudge
        /// is enough real KBM input for Genshin to switch its prompt scheme back without actually
        /// moving the in-game camera/cursor anywhere. <see cref="Dispose"/> calls this automatically;
        /// exposed separately for callers that want to hand control back to the user without fully
        /// tearing down the virtual device (e.g. pausing mid-scan).
        /// </summary>
        public void ExitControllerMode()
        {
            if (!IsAvailable) return;
            MashBack();
            inputSimulator.Mouse.MoveMouseBy(1, 0);
            Thread.Sleep(50);
            inputSimulator.Mouse.MoveMouseBy(-1, 0);
        }

        private void RequireAvailable()
        {
            if (!IsAvailable) throw new InvalidOperationException($"Controller is not available: {FailureReason}");
        }

        public void Dispose()
        {
            if (!IsAvailable) return;
            ExitControllerMode();
            Thread.Sleep(100);
            controller.Disconnect();
            client.Dispose();
        }
    }
}
