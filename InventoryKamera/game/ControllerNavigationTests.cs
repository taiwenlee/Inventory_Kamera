using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace InventoryKamera.game
{
    /// <summary>
    /// Ad-hoc manual test routines for Phase 3 §6c's controller-driven navigation, triggered from
    /// Options-menu items in <c>MainForm</c>. Deliberately kept out of <c>MainForm.cs</c>/
    /// <c>MainForm.Designer.cs</c> -- editing those repeatedly risks tripping the WinForms Designer
    /// regeneration bug documented in MODERNIZATION_PLAN.md §3.0 (stripped <c>global::</c>
    /// qualifiers, rebound settings to a throwaway instance), and none of this logic needs the
    /// Designer surface at all. <c>MainForm</c>'s Click handlers just call into these one-liners.
    /// </summary>
    internal static class ControllerNavigationTests
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private const int AltTabSeconds = 4;

        // Genshin's inventory remembers whichever tab was last open, so navigation can't assume it
        // always lands on a known tab -- read the tab name label (top-left of the inventory screen)
        // and fuzzy-match it against the known tab order to figure out which one is active.
        private static readonly string[] InventoryTabNames =
        {
            "Weapons", "Artifacts", "Character Development Items", "Food", "Materials",
            "Gadget", "Quest", "Precious Items", "Furnishings",
        };

        private static void ShowAltTabPrompt(string title, string message)
        {
            MessageBox.Show(
                $"After clicking OK, you have {AltTabSeconds} seconds to switch to Genshin (Alt+Tab) " +
                $"and make sure it's unpaused. {message}",
                title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Thread.Sleep(AltTabSeconds * 1000);
        }

        // Throwaway feasibility spike for Phase 3 §6c -- see ControllerSpike.cs.
        public static void RunControllerInputSpike()
        {
            MessageBox.Show(
                $"After clicking OK, you have {AltTabSeconds} seconds to switch to Genshin (Alt+Tab). " +
                "The left stick will nudge and the A button will press once the timer runs out.",
                "Controller Input Spike", MessageBoxButtons.OK, MessageBoxIcon.Information);

            string result = ControllerSpike.TapAButton(AltTabSeconds);
            MessageBox.Show(result, "Controller Input Spike", MessageBoxButtons.OK,
                result.StartsWith("Success") ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        // First real slice: switch Genshin to controller-input mode, then open its Esc-equivalent
        // menu (Start button). Make sure Genshin is focused and unpaused before clicking this --
        // there's no way to verify that from here.
        public static void RunOpenMenuTest()
        {
            ShowAltTabPrompt("Controller Menu Test", "The left stick will nudge, then Start will be pressed to open the menu.");

            using (var controller = new GameController())
            {
                if (!controller.IsAvailable)
                {
                    MessageBox.Show(controller.FailureReason, "Controller Menu Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                controller.EnterControllerMode();
                // Switching Genshin's UI prompt scheme appears to be visually instant but not
                // immediately ready to route button presses -- 200ms wasn't enough for Start to
                // register (2026-07-05 live test); try a longer settle window.
                Thread.Sleep(1000);
                controller.OpenMenu();
            }

            MessageBox.Show("Sent: stick nudge, then Start. Check whether Genshin's menu opened.",
                "Controller Menu Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // The pause menu's tab bar is a 4-wide x 5-tall grid, cursor starts at [0,0] (top-left) when
        // the menu opens. Inventory is at [0,2] -- a pure vertical move, the simplest case to
        // validate direction mapping/step timing before anything diagonal.
        public static void RunNavigateToInventoryTest()
        {
            ShowAltTabPrompt("Controller Navigate Test", "This will open the menu, move down twice, then confirm with B.");

            using (var controller = new GameController())
            {
                if (!controller.IsAvailable)
                {
                    MessageBox.Show(controller.FailureReason, "Controller Navigate Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                controller.EnterControllerMode();
                Thread.Sleep(1000);
                controller.OpenMenu();
                Thread.Sleep(1000);
                controller.Move(GameController.MenuDirection.Down, 2);
                Thread.Sleep(300);
                // B is the universal confirm button, A is back/cancel -- see RunNavigateToCharacterTest.
                controller.TapButton(Xbox360Button.B);
            }

            MessageBox.Show("Sent: open menu, move down x2, confirm with B. Check whether Inventory opened.",
                "Controller Navigate Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Character menu is at [2,1] from the [0,0] start -- first test of combined
        // horizontal+vertical movement (right 2, down 1), issued as two separate straight-line legs.
        public static void RunNavigateToCharacterTest()
        {
            ShowAltTabPrompt("Controller Navigate Test", "This will open the menu, move right twice then down once, then confirm with B.");

            using (var controller = new GameController())
            {
                if (!controller.IsAvailable)
                {
                    MessageBox.Show(controller.FailureReason, "Controller Navigate Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                controller.EnterControllerMode();
                Thread.Sleep(1000);
                controller.OpenMenu();
                Thread.Sleep(1000);
                controller.Move(GameController.MenuDirection.Right, 2);
                controller.Move(GameController.MenuDirection.Down, 1);
                Thread.Sleep(300);
                // Confirmed via live testing (2026-07-05): B is the universal confirm button
                // (A is back/cancel) -- corrected from an earlier A press that appeared to work but
                // was actually wrong, per direct user correction.
                controller.TapButton(Xbox360Button.B);
            }

            MessageBox.Show("Sent: open menu, move right x2 then down x1, confirm with B. Check whether Character opened.",
                "Controller Navigate Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

        // Opens the pause menu and navigates into Inventory -- shared by every test that needs to
        // already be inside Inventory before doing its own thing. Must be called from inside the
        // GameController's using block (not after), same reasoning as the capture/OCR note below.
        private static void EnterInventory(GameController controller)
        {
            controller.EnterControllerMode();
            Thread.Sleep(1000);
            controller.OpenMenu();
            Thread.Sleep(1000);
            controller.Move(GameController.MenuDirection.Down, 2);
            Thread.Sleep(300);
            // Entering the Inventory tab needs B, not A -- see RunNavigateToInventoryTest.
            controller.TapButton(Xbox360Button.B);
            Thread.Sleep(1000);
        }

        /// <summary>
        /// Captures the tab-name label (top-left of the inventory screen), OCRs it, and fuzzy-matches
        /// it against <see cref="InventoryTabNames"/>. Returns the matched index into
        /// <see cref="InventoryTabNames"/> (-1 if no confident match) and the raw OCR text for
        /// diagnostics. Must be called while still inside Inventory (see <see cref="EnterInventory"/>
        /// and the same before-Dispose timing note).
        /// </summary>
        private static int DetectCurrentTabIndex(out string rawText)
        {
            Directory.CreateDirectory("./logging");

            using (var fullWindow = Navigation.CaptureWindow())
            {
                fullWindow.Save("./logging/InventoryFullWindow.png");
            }
            using (var region = Navigation.CaptureRegion(
                // Live-tuned (2026-07-05): starts past the backpack icon to the left of the tab
                // name text (an earlier, further-left start fed stray icon shapes into OCR), and
                // wide enough to cover the longest tab name ("Character Development Items"). Height
                // increased and y shifted down slightly after the Quest tab's text was sitting right
                // at the bottom edge of the old 0.03-0.09 band, clipping its descenders and producing
                // garbage OCR ("[0]1171") -- the Furnishings tab happened to sit more centered in that
                // same band, which is why the original calibration looked fine on it.
                x: (int)(0.09 * Navigation.GetWidth()),
                y: (int)(0.035 * Navigation.GetHeight()),
                width: (int)(0.20 * Navigation.GetWidth()),
                height: (int)(0.08 * Navigation.GetHeight())))
            {
                region.Save("./logging/InventoryTabRegion.png");

                // Every real scraper OCR call site (e.g. InventoryScraper.ScanItemCount) preprocesses
                // -- grayscale, contrast boost, invert -- before OCR; this test call was skipping all
                // of that and feeding Tesseract the raw color capture directly, which produced garbage
                // output ("[0]1171"/"[011231") on the Quest tab despite the crop itself looking clean
                // to the eye. Matching the standard pipeline here.
                var imagePreprocessor = new ImageProcessor();
                Bitmap processed = imagePreprocessor.ConvertToGrayscale(region);
                imagePreprocessor.SetContrast(60.0, ref processed);
                imagePreprocessor.SetInvert(ref processed);
                processed.Save("./logging/InventoryTabRegionProcessed.png");

                using (processed)
                using (var ocrService = new OcrService())
                {
                    rawText = ocrService.AnalyzeText(processed, Tesseract.PageSegMode.SingleLine).Trim();
                }
            }

            var normalizedTabs = InventoryTabNames.Select(t => t.ToLower().Replace(" ", "")).ToArray();
            string normalizedText = Regex.Replace(rawText.ToLower(), @"[\W]", string.Empty);
            string matchedNormalized = TextNormalizer.FindClosestInList(normalizedText, new HashSet<string>(normalizedTabs));
            int matchedIndex = Array.IndexOf(normalizedTabs, matchedNormalized);

            Logger.Debug("Inventory tab OCR: rawText=\"{0}\" normalizedText=\"{1}\" matchedTab=\"{2}\" matchedIndex={3}",
                rawText, normalizedText, matchedIndex >= 0 ? InventoryTabNames[matchedIndex] : "(none)", matchedIndex);

            return matchedIndex;
        }

        public static void RunReadInventoryTabTest()
        {
            ShowAltTabPrompt("Read Inventory Tab Test", "This will open the menu, navigate to Inventory, then read the current tab name.");

            try
            {
                Navigation.Initialize();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not find Genshin's window: {ex.Message}", "Read Inventory Tab Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string rawText;
            int matchedIndex;
            using (var controller = new GameController())
            {
                if (!controller.IsAvailable)
                {
                    MessageBox.Show(controller.FailureReason, "Read Inventory Tab Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Capture/OCR must happen here, before this using block ends -- Dispose() calls
                // ExitControllerMode(), which mashes A (back/cancel) as a safety net before
                // disconnecting. Capturing after the using block closed meant the mash-back exit had
                // already backed out of Inventory before the screenshot was ever taken.
                EnterInventory(controller);
                matchedIndex = DetectCurrentTabIndex(out rawText);
            }

            string matchedTab = matchedIndex >= 0 ? InventoryTabNames[matchedIndex] : null;
            MessageBox.Show(
                $"Raw OCR text: \"{rawText}\"\nMatched tab: {matchedTab ?? "(no confident match)"}\n\n" +
                "Saved logging/InventoryFullWindow.png (full screen) and logging/InventoryTabRegion.png " +
                "(the cropped guess) -- send me InventoryFullWindow.png if the crop looks wrong so I can find the right coordinates.",
                "Read Inventory Tab Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// Enters Inventory, detects whichever tab Genshin remembered as last-open, and cycles
        /// LB/RB (shoulder buttons -- the inventory sub-tab row's own switch input, distinct from the
        /// pause menu's stick-driven grid) to land on <paramref name="targetTab"/> regardless of
        /// where it started. Not yet verified live -- first use of <see cref="DetectCurrentTabIndex"/>
        /// for something other than just reporting the result.
        /// </summary>
        private static void RunSwitchToTab(string targetTab)
        {
            ShowAltTabPrompt("Switch Inventory Tab Test", $"This will open the menu, navigate to Inventory, detect the current tab, then switch to {targetTab}.");

            try
            {
                Navigation.Initialize();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not find Genshin's window: {ex.Message}", "Switch Inventory Tab Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int targetIndex = Array.IndexOf(InventoryTabNames, targetTab);
            string rawText;
            int currentIndex;
            using (var controller = new GameController())
            {
                if (!controller.IsAvailable)
                {
                    MessageBox.Show(controller.FailureReason, "Switch Inventory Tab Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                EnterInventory(controller);
                currentIndex = DetectCurrentTabIndex(out rawText);

                if (currentIndex < 0)
                {
                    MessageBox.Show($"Could not confidently identify the current tab (raw OCR: \"{rawText}\"). Not attempting to switch.",
                        "Switch Inventory Tab Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Tabs wrap around (circular), so the shorter direction isn't always "forward" --
                // compare the forward (RB) distance against the backward (LB) distance and take
                // whichever is fewer presses. Live-tested (2026-07-05): 300ms/tap was slower than
                // needed; presses register fine much faster.
                int tabCount = InventoryTabNames.Length;
                int forwardSteps = ((targetIndex - currentIndex) % tabCount + tabCount) % tabCount;
                int backwardSteps = tabCount - forwardSteps;
                bool goForward = forwardSteps <= backwardSteps;
                int steps = goForward ? forwardSteps : backwardSteps;
                Xbox360Button shoulderButton = goForward ? Xbox360Button.RightShoulder : Xbox360Button.LeftShoulder;

                for (int i = 0; i < steps; i++)
                {
                    controller.TapButton(shoulderButton, holdMs: 80);
                    Thread.Sleep(100);
                }

                MessageBox.Show(
                    $"Detected starting tab: {InventoryTabNames[currentIndex]}. Sent {steps} " +
                    $"{(goForward ? "RB" : "LB")} presses toward {targetTab}. Check whether {targetTab} is now active.",
                    "Switch Inventory Tab Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        public static void RunSwitchToWeaponsTabTest() => RunSwitchToTab("Weapons");
        public static void RunSwitchToArtifactsTabTest() => RunSwitchToTab("Artifacts");
        public static void RunSwitchToCharacterDevItemsTabTest() => RunSwitchToTab("Character Development Items");

        // Switches to the given inventory tab from wherever the cursor currently is, using the same
        // circular shortest-direction logic as RunSwitchToTab. Must run inside Inventory already
        // (see EnterInventory) and inside the GameController's using block.
        private static void SwitchToTab(GameController controller, string targetTab)
        {
            int tabIndex = DetectCurrentTabIndex(out _);
            int targetIndex = Array.IndexOf(InventoryTabNames, targetTab);
            if (tabIndex == targetIndex) return;

            int tabCount = InventoryTabNames.Length;
            int forwardSteps = ((targetIndex - tabIndex) % tabCount + tabCount) % tabCount;
            int backwardSteps = tabCount - forwardSteps;
            bool goForward = forwardSteps <= backwardSteps;
            Xbox360Button shoulderButton = goForward ? Xbox360Button.RightShoulder : Xbox360Button.LeftShoulder;
            for (int i = 0; i < Math.Min(forwardSteps, backwardSteps); i++)
            {
                controller.TapButton(shoulderButton, holdMs: 80);
                Thread.Sleep(100);
            }
            Thread.Sleep(300);
        }

        /// <summary>
        /// Captures the selected item's always-visible detail card in controller mode -- the same
        /// region serves the Weapons, Artifacts, and Character Development Items tabs (per user).
        /// Percentages measured by the user with the coordinate-picker tool from a full-window
        /// capture (2026-07-03, 1920x1080) -- replaces the initial guess that reused
        /// <c>InventoryScraper.GetItemCard()</c>'s mouse-hover popup percentages, including its
        /// <c>Navigation.IsNormal</c> variants: those windowed/fullscreen offsets belonged to the
        /// mouse-mode popup, and no windowed-mode measurement exists yet for this panel. Re-measure
        /// with the picker if windowed mode misbehaves.
        /// </summary>
        private static Bitmap CaptureSelectedItemCard()
        {
            return Navigation.CaptureRegion(
                x: (int)(0.7031 * Navigation.GetWidth()),
                y: (int)(0.1231 * Navigation.GetHeight()),
                width: (int)(0.2167 * Navigation.GetWidth()),
                height: (int)(0.7556 * Navigation.GetHeight()));
        }

        /// <summary>
        /// Reads the name of whichever weapon is currently selected in Inventory (must already be on
        /// the Weapons tab, see <see cref="SwitchToTab"/>), without any mouse clicking. Card region:
        /// see <see cref="CaptureSelectedItemCard"/>. Preprocessing matches
        /// <c>InventoryScraper.ScanItemName</c>'s proven pipeline for this exact visual pattern (white
        /// embossed text on an orange/brown gradient nameplate): gamma correction, then grayscale +
        /// invert, no contrast step -- a first attempt using generic grayscale+contrast+invert
        /// produced total garbage despite the crop itself being clean and legible to the eye.
        /// </summary>
        private static (string RawText, string MatchedName) ReadSelectedWeaponName(string fileSuffix = "")
        {
            using (var card = CaptureSelectedItemCard())
            {
                card.Save($"./logging/SelectedWeaponCard{fileSuffix}.png");

                using (var nameRegion = GenshinProcesor.CopyBitmap(card, new System.Drawing.Rectangle(
                    x: 0, y: 0, width: card.Width,
                    height: (int)(card.Height * (Navigation.IsNormal ? 0.07 : 0.06)))))
                {
                    var nameBitmap = nameRegion;
                    // SetGamma clones and reassigns nameBitmap to a new Bitmap rather than mutating
                    // in place -- dispose it explicitly once done with it.
                    GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref nameBitmap);
                    var imagePreprocessor = new ImageProcessor();
                    Bitmap processed = imagePreprocessor.ConvertToGrayscale(nameBitmap);
                    nameBitmap.Dispose();
                    imagePreprocessor.SetInvert(ref processed);

                    string rawText;
                    using (processed)
                    using (var ocrService = new OcrService())
                    {
                        rawText = ocrService.AnalyzeText(processed, Tesseract.PageSegMode.SingleBlock).Trim();
                    }

                    string normalizedText = Regex.Replace(rawText.ToLower(), @"[\W]", string.Empty);
                    string matchedName = GenshinProcesor.FindClosestWeapon(normalizedText);

                    Logger.Debug("Selected weapon name OCR: rawText=\"{0}\" normalizedText=\"{1}\" matchedName=\"{2}\"",
                        rawText, normalizedText, matchedName);

                    return (rawText, matchedName);
                }
            }
        }

        /// <summary>
        /// Reads name, level, refinement, and equipped character for whichever weapon is currently
        /// selected in Inventory -- everything <c>WeaponScraper.ScanWeapons()</c>'s mouse-based path
        /// captures per item, needed before this can actually replace it. Captures the card once and
        /// derives all four sub-crops from it (matching how the mouse-based path works), reusing each
        /// field's exact percentage regions and preprocessing pipeline from
        /// <c>WeaponScraper</c>/<c>InventoryScraper</c> (<see cref="ReadSelectedWeaponName"/>'s name
        /// pipeline, plus <c>GetLevelBitmap</c>/<c>ScanLevel</c>, <c>GetRefinementBitmap</c>/
        /// <c>ScanRefinement</c>, and <c>GetEquippedBitmap</c>/<c>ScanEquippedCharacter</c>'s) rather
        /// than inventing new ones -- not yet confirmed these regions land correctly in controller
        /// mode the way the name region did.
        /// </summary>
        private static (string Name, int Level, bool Ascended, int Refinement, string Equipped) ReadSelectedWeaponDetails()
        {
            var imagePreprocessor = new ImageProcessor();

            using (var card = CaptureSelectedItemCard())
            {
                card.Save("./logging/SelectedWeaponDetailsCard.png");

                string name;
                using (var nameRegion = GenshinProcesor.CopyBitmap(card, new System.Drawing.Rectangle(
                    x: 0, y: 0, width: card.Width,
                    height: (int)(card.Height * (Navigation.IsNormal ? 0.07 : 0.06)))))
                {
                    var nameBitmap = nameRegion;
                    GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref nameBitmap);
                    Bitmap processed = imagePreprocessor.ConvertToGrayscale(nameBitmap);
                    nameBitmap.Dispose();
                    imagePreprocessor.SetInvert(ref processed);
                    using (processed)
                    using (var ocrService = new OcrService())
                    {
                        string rawText = ocrService.AnalyzeText(processed, Tesseract.PageSegMode.SingleBlock).Trim();
                        string normalizedText = Regex.Replace(rawText.ToLower(), @"[\W]", string.Empty);
                        name = GenshinProcesor.FindClosestWeapon(normalizedText);
                    }
                }

                int level = -1;
                bool ascended = false;
                using (var levelRegion = GenshinProcesor.CopyBitmap(card, new System.Drawing.Rectangle(
                    // Controller-mode-specific region, separate from InventoryScraper's mouse-mode
                    // percentages (see class doc) -- the "Lv. 90/90" row sits noticeably higher here
                    // (right where the orange header ends, ~29-30% down) than the mouse-hover popup's
                    // 36.7%, per live screenshot (2026-07-05). Widened generously since this is a
                    // first attempt at the controller-specific position, not yet confirmed.
                    x: (int)(card.Width * 0.05),
                    y: (int)(card.Height * 0.31),
                    width: (int)(card.Width * 0.35),
                    height: (int)(card.Height * 0.05))))
                {
                    levelRegion.Save("./logging/SelectedWeaponDetailsLevel.png");
                    Bitmap processed = imagePreprocessor.ConvertToGrayscale(levelRegion);
                    imagePreprocessor.SetInvert(ref processed);
                    using (processed)
                    using (var ocrService = new OcrService())
                    {
                        string rawText = ocrService.AnalyzeText(processed).Trim();
                        string text = Regex.Replace(rawText, @"(?![\d/]).", string.Empty);
                        if (text.Contains('/'))
                        {
                            string[] parts = text.Split(new[] { '/' }, 2);
                            if (parts.Length == 2 && int.TryParse(parts[0], out int lvl) && int.TryParse(parts[1], out int maxLevel))
                            {
                                maxLevel = (int)Math.Round(maxLevel / 10.0, MidpointRounding.AwayFromZero) * 10;
                                ascended = 20 <= lvl && lvl < maxLevel;
                                level = lvl;
                            }
                        }
                        Logger.Debug("Selected weapon level OCR: rawText=\"{0}\" filteredText=\"{1}\" parsedLevel={2}", rawText, text, level);
                    }
                }

                int refinement = -1;
                using (var refinementRegion = GenshinProcesor.CopyBitmap(card, new System.Drawing.Rectangle(
                    // Controller-mode-specific region (see level region's comment above for why this
                    // differs from InventoryScraper's mouse-mode percentages). Widened generously
                    // around the refinement-rank badge's estimated position; not yet confirmed. The
                    // digit-only filter downstream means capturing extra surrounding text (e.g.
                    // "Refinement Rank") alongside the badge number is harmless.
                    x: (int)(card.Width * 0.10),
                    y: (int)(card.Height * 0.35),
                    width: (int)(card.Width * 0.20),
                    height: (int)(card.Height * 0.05))))
                {
                    refinementRegion.Save("./logging/SelectedWeaponDetailsRefinement.png");
                    for (double factor = 1; factor <= 2; factor += 0.1)
                    {
                        using (var scaled = GenshinProcesor.ScaleImage(refinementRegion, factor))
                        {
                            Bitmap processed = imagePreprocessor.ConvertToGrayscale(scaled);
                            imagePreprocessor.SetInvert(ref processed);
                            using (processed)
                            using (var ocrService = new OcrService())
                            {
                                string rawText = ocrService.AnalyzeText(processed).Trim();
                                string text = Regex.Replace(rawText, @"[^\d]", string.Empty);
                                Logger.Debug("Selected weapon refinement OCR (scale={0:0.0}): rawText=\"{1}\" filteredText=\"{2}\"", factor, rawText, text);
                                if (int.TryParse(text, out int r) && 1 <= r && r <= 5)
                                {
                                    refinement = r;
                                    break;
                                }
                            }
                        }
                    }
                }

                string equipped = null;
                using (var equippedRegion = GenshinProcesor.CopyBitmap(card, new System.Drawing.Rectangle(
                    // Controller-mode-specific x start, shifted right from mouse-mode's 0.15 --
                    // the character portrait icon to the left of "Equipped: X" was still inside the
                    // crop at 0.15, and Tesseract hallucinated stray characters ("2 "/"bm") from it
                    // even though the actual text region itself was clean (live-tested 2026-07-05).
                    x: (int)(card.Width * 0.30),
                    y: (int)(card.Height * (Navigation.IsNormal ? 0.938 : 0.943)),
                    width: card.Width,
                    height: card.Height)))
                {
                    equippedRegion.Save("./logging/SelectedWeaponDetailsEquipped.png");
                    Bitmap processed = imagePreprocessor.ConvertToGrayscale(equippedRegion);
                    imagePreprocessor.SetContrast(60.0, ref processed);
                    using (processed)
                    using (var ocrService = new OcrService())
                    {
                        string extractedString = ocrService.AnalyzeText(processed);
                        // Original mouse-mode match required an exact "Equipped:" substring
                        // (case-sensitive, literal colon) -- too strict for OCR noise (missing/extra
                        // whitespace, colon misread, case drift). Match "Equipped" as a prefix,
                        // case-insensitively, and take everything after it as the name instead.
                        var match = Regex.Match(extractedString ?? "", @"Equipped\s*:?\s*(.+)", RegexOptions.IgnoreCase);
                        Logger.Debug("Selected weapon equipped OCR: rawText=\"{0}\" matched={1}", extractedString, match.Success);
                        if (match.Success)
                        {
                            string equippedName = Regex.Replace(match.Groups[1].Value, @"[\W]", string.Empty).ToLower();
                            equipped = GenshinProcesor.FindClosestCharacterName(equippedName);
                        }
                    }
                }

                Logger.Debug("Selected weapon details: name=\"{0}\" level={1} ascended={2} refinement={3} equipped=\"{4}\"",
                    name, level, ascended, refinement, equipped ?? "(none)");

                return (name, level, ascended, refinement, equipped);
            }
        }

        public static void RunReadSelectedWeaponDetailsTest()
        {
            ShowAltTabPrompt("Read Selected Weapon Details Test", "This will open the menu, switch to the Weapons tab, then read the selected weapon's name, level, refinement, and equipped character.");

            try
            {
                Navigation.Initialize();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not find Genshin's window: {ex.Message}", "Read Selected Weapon Details Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Directory.CreateDirectory("./logging");

            (string Name, int Level, bool Ascended, int Refinement, string Equipped) result;
            using (var controller = new GameController())
            {
                if (!controller.IsAvailable)
                {
                    MessageBox.Show(controller.FailureReason, "Read Selected Weapon Details Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                EnterInventory(controller);
                SwitchToTab(controller, "Weapons");
                result = ReadSelectedWeaponDetails();
            }

            MessageBox.Show(
                $"Name: {result.Name}\nLevel: {result.Level} (ascended: {result.Ascended})\n" +
                $"Refinement: {result.Refinement}\nEquipped: {result.Equipped ?? "(not equipped/not detected)"}\n\n" +
                "Saved logging/SelectedWeaponDetailsCard.png plus per-field crops -- send me these if any field looks wrong.",
                "Read Selected Weapon Details Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static void RunReadSelectedWeaponNameTest()
        {
            ShowAltTabPrompt("Read Selected Weapon Test", "This will open the menu, switch to the Weapons tab, then read the name of whichever weapon is currently selected.");

            try
            {
                Navigation.Initialize();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not find Genshin's window: {ex.Message}", "Read Selected Weapon Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Directory.CreateDirectory("./logging");

            (string rawText, string matchedName) result;
            using (var controller = new GameController())
            {
                if (!controller.IsAvailable)
                {
                    MessageBox.Show(controller.FailureReason, "Read Selected Weapon Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                EnterInventory(controller);
                SwitchToTab(controller, "Weapons");
                result = ReadSelectedWeaponName();
            }

            MessageBox.Show(
                $"Raw OCR text: \"{result.rawText}\"\nMatched weapon: {result.matchedName}\n\n" +
                "Saved logging/SelectedWeaponCard.png and other diagnostics -- send me these if the crop looks wrong.",
                "Read Selected Weapon Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// Verifies a single left-stick-right nudge actually advances the grid selection to the next
        /// weapon -- the last primitive needed before replacing the mouse-based scan loop. Reads the
        /// currently-selected weapon's name, sends one <see cref="GameController.MoveStep"/> to the
        /// right, then reads the name again. Matched-name equality is not a reliable signal on its
        /// own (per user, duplicate weapons commonly sit next to each other in the grid) -- this just
        /// reports both results side by side with their saved screenshots for visual comparison,
        /// rather than asserting pass/fail from the name alone.
        /// </summary>
        public static void RunAdvanceToNextWeaponTest()
        {
            ShowAltTabPrompt("Advance Weapon Test", "This will open the menu, switch to Weapons, read the selected weapon's name, nudge the left stick right once, then read the name again.");

            try
            {
                Navigation.Initialize();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not find Genshin's window: {ex.Message}", "Advance Weapon Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Directory.CreateDirectory("./logging");

            (string rawText, string matchedName) before, after;
            using (var controller = new GameController())
            {
                if (!controller.IsAvailable)
                {
                    MessageBox.Show(controller.FailureReason, "Advance Weapon Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                EnterInventory(controller);
                SwitchToTab(controller, "Weapons");
                before = ReadSelectedWeaponName("Before");

                controller.MoveStep(GameController.MenuDirection.Right);
                Thread.Sleep(300);

                after = ReadSelectedWeaponName("After");
            }

            MessageBox.Show(
                $"Before: \"{before.rawText}\" -> matched: {before.matchedName}\n" +
                $"After:  \"{after.rawText}\" -> matched: {after.matchedName}\n\n" +
                "Names matching doesn't necessarily mean the selection didn't move (duplicate weapons " +
                "can sit next to each other) -- check logging/SelectedWeaponCardBefore.png and " +
                "SelectedWeaponCardAfter.png visually to confirm.",
                "Advance Weapon Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
