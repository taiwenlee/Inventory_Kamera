using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using InventoryKamera;

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
        private const int TestCharacterScanCount = 8;

        /// <summary>
        /// Forwards every setting to a real <see cref="ScanSettings"/> except
        /// <see cref="NumOfCharToScan"/>, which is pinned to <see cref="TestCharacterScanCount"/>.
        /// <see cref="ScanSettings"/> is sealed and reads its value live from
        /// <c>Properties.Settings.Default</c>, so this exists purely to cap
        /// <see cref="RunControllerCharacterScanTest"/>'s scan length for a quick test run without
        /// touching (and needing to restore) the user's actual persisted setting.
        /// </summary>
        private sealed class FixedCharacterCountScanSettings : IScanSettings
        {
            private readonly ScanSettings inner = new ScanSettings();

            public bool ScanWeapons => inner.ScanWeapons;
            public bool ScanArtifacts => inner.ScanArtifacts;
            public bool ScanCharacters => inner.ScanCharacters;
            public bool ScanCharDevItems => inner.ScanCharDevItems;
            public bool ScanMaterials => inner.ScanMaterials;
            public int ScannerDelay => inner.ScannerDelay;
            public decimal MinimumWeaponRarity => inner.MinimumWeaponRarity;
            public decimal MinimumArtifactRarity => inner.MinimumArtifactRarity;
            public decimal MinimumWeaponLevel => inner.MinimumWeaponLevel;
            public decimal MinimumArtifactLevel => inner.MinimumArtifactLevel;
            public bool LogScreenshots => inner.LogScreenshots;
            public int OcrConfidenceThreshold => inner.OcrConfidenceThreshold;
            public string TravelerName => inner.TravelerName;
            public string WandererName => inner.WandererName;
            public int SortByObtained => inner.SortByObtained;
            public int NumOfCharToScan => TestCharacterScanCount;
            public string Manequin1Name => inner.Manequin1Name;
            public string Manequin2Name => inner.Manequin2Name;
        }

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

        /// <summary>
        /// Manual live test for <see cref="CharacterScraper.ScanCharactersViaController"/> (Phase 3
        /// §6c, added 2026-07-05, not yet live-verified) -- the sub-tab-batched controller character
        /// scan. Reuses <paramref name="progressReporter"/> (the real <c>ScanViewModel</c> from
        /// <c>MainForm</c>) so scanned name/level/talent images actually show up in the UI panels
        /// like a real scan would, but builds its own throwaway <see cref="OcrService"/>/
        /// <see cref="ImageProcessor"/>/<see cref="ScanSettings"/> and a fresh
        /// <see cref="Character"/> list rather than touching <c>InventoryKamera.GatherData</c>'s
        /// real scan state -- this is a standalone probe, not a substitute for the real pipeline.
        /// Scan length is capped at <see cref="TestCharacterScanCount"/> via
        /// <see cref="FixedCharacterCountScanSettings"/> rather than the user's real saved
        /// NumOfCharToScan setting, so a quick test run doesn't require changing (and remembering to
        /// revert) that setting in Options.
        /// </summary>
        public static void RunControllerCharacterScanTest(IScanProgressReporter progressReporter)
        {
            // Navigation.GetWidth()/GetHeight()/GetPosition() all read from WindowSize/WindowPosition,
            // which stay zeroed until Navigation.Initialize() locates the game window -- the real scan
            // pipeline always runs this via MainForm.PreflightChecksPass() first. This standalone test
            // skips that pipeline entirely, so it must call Initialize() itself; missing this caused
            // every capture region to compute as 0x0, surfacing as "Parameter is not valid" out of
            // `new Bitmap(0, 0, ...)` deep inside Navigation.CaptureRegion.
            try
            {
                Navigation.Initialize();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Genshin Impact isn't running or its window couldn't be found: " + ex.Message,
                    "Controller Character Scan Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show(
                $"After clicking OK, you have {AltTabSeconds} seconds to switch to Genshin (Alt+Tab), " +
                "unpaused with no menu open. The controller will open the Character menu and scan the " +
                $"first {TestCharacterScanCount} characters (Attributes, then Constellations, then Talents).",
                "Controller Character Scan Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Thread.Sleep(AltTabSeconds * 1000);

            using (var controller = new GameController())
            {
                if (!controller.IsAvailable)
                {
                    MessageBox.Show(controller.FailureReason, "Controller Character Scan Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var ocrService = new OcrService();
                ocrService.Restart();
                var imagePreprocessor = new ImageProcessor();
                var scanSettings = new FixedCharacterCountScanSettings();
                var scraper = new CharacterScraper(ocrService, imagePreprocessor, scanSettings, progressReporter);
                var characters = new List<Character>();

                try
                {
                    scraper.ScanCharactersViaController(controller, ref characters);
                    MessageBox.Show($"Scanned {characters.Count} character(s). Check the log for details.",
                        "Controller Character Scan Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message + "\n" + ex.StackTrace, "Controller Character Scan Test", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
