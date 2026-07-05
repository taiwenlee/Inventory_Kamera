using System.Threading;
using System.Windows.Forms;

namespace InventoryKamera.game
{
    /// <summary>
    /// Ad-hoc manual test routines for Phase 3 §6c's controller-driven navigation, triggered from
    /// Options-menu items in <c>MainForm</c>. Deliberately kept out of <c>MainForm.cs</c>/
    /// <c>MainForm.Designer.cs</c> -- editing those repeatedly risks tripping the WinForms Designer
    /// regeneration bug documented in MODERNIZATION_PLAN.md §3.0 (stripped <c>global::</c>
    /// qualifiers, rebound settings to a throwaway instance), and none of this logic needs the
    /// Designer surface at all. <c>MainForm</c>'s Click handler just calls into this one-liner.
    /// Trimmed (2026-07-05) down to only the panic button -- every other granular per-primitive test
    /// method (menu nav, tab detection/switching, weapon name/details reads, advance-step check) was
    /// removed once its only caller (a Debug-menu item) was removed and the real
    /// <c>WeaponScraper.ScanWeaponsViaController</c>/<c>ArtifactScraper.ScanArtifactsViaController</c>
    /// superseded the need for them.
    /// </summary>
    internal static class ControllerNavigationTests
    {
        private const int AltTabSeconds = 4;

        // Manual escape hatch: connects, mashes A (back/cancel) to back out of however many menus
        // deep things are stuck, then gracefully exits controller mode -- for recovering from a bad
        // test run without needing to alt-tab and press Esc/click through it by hand.
        public static void RunMashBackTest()
        {
            MessageBox.Show(
                $"After clicking OK, you have {AltTabSeconds} seconds to switch to Genshin (Alt+Tab). " +
                "A will be pressed repeatedly to back out of any open menus.",
                "Controller Panic Button", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Thread.Sleep(AltTabSeconds * 1000);

            using (var controller = new GameController())
            {
                if (!controller.IsAvailable)
                {
                    MessageBox.Show(controller.FailureReason, "Controller Panic Button", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                controller.MashBack();
            }

            MessageBox.Show("Sent: repeated A presses. Check that Genshin is back at the main game/menu root.",
                "Controller Panic Button", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
