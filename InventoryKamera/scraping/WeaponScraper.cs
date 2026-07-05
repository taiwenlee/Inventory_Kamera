using InventoryKamera.game;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using NLog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace InventoryKamera
{
    internal class WeaponScraper : InventoryScraper
    {
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		public WeaponScraper(IOcrService ocrService, IImagePreprocessor imagePreprocessor, IScanSettings scanSettings, IScanProgressReporter progressReporter) : base(ocrService, imagePreprocessor, scanSettings, progressReporter)
        {
            inventoryPage = InventoryPage.Weapons;
            SortByLevel = scanSettings.MinimumWeaponLevel > 1;
        }

        public void ScanWeapons(int count = 0)
        {
            // Determine maximum number of weapons to scan
            int weaponCount = count == 0 ? ScanItemCount() : count;
            int page = 0;
            var (rectangles, cols, rows) = GetPageOfItems(page);
            int fullPage = cols * rows;
            int totalRows = (int)Math.Ceiling(weaponCount / (decimal)cols);
            int cardsQueued = 0;
            int rowsQueued = 0;
            int offset = 0;
            progressReporter.SetWeapon_Max(weaponCount);

            // Determine Delay if delay has not been found before
            // Scraper.FindDelay(rectangles);

            StopScanning = false;

            Logger.Info("Found {0} for weapon count.", weaponCount);

            SelectSortingMethod();

            // Go through weapon list
            while (cardsQueued < weaponCount && !InventoryKamera.CancelRequested)
            {
                Logger.Debug("Scanning weapon page {0}", page);
                Logger.Debug("Located {0} possible item locations on page.", rectangles.Count);

                int cardsRemaining = weaponCount - cardsQueued;
                // Go through each "page" of items and queue. In the event that not a full page of
                // items are scrolled to, offset the index of rectangle to start clicking from.
                // Clamped to 0: GetPageOfItems can fall back to a previous page's row count when
                // detection fails, which can make this arithmetic go negative and throw.
                for (int i = cardsRemaining < fullPage ? Math.Max(0, (rows - (totalRows - rowsQueued)) * cols) : 0; i < rectangles.Count; i++)
                {
                    // Blocks here (not just once at loop entry) if a previously-queued item's
                    // recognition is still awaiting an inline correction -- keeps the game from being
                    // clicked/scrolled further ahead of what the user is currently looking at.
                    progressReporter.WaitIfCorrectionPending();

                    Rectangle item = rectangles[i];
                    Navigation.SetCursor(item.Center().X, item.Center().Y + offset);
                    Navigation.Click();
                    Navigation.SystemWait(Navigation.Speed.SelectNextInventoryItem);

                    // Queue card for scanning
                    QueueScan(cardsQueued);
                    cardsQueued++;
                    if (cardsQueued >= weaponCount || this.StopScanning || InventoryKamera.CancelRequested)
                    {
                        if (InventoryKamera.CancelRequested) Logger.Info("Stopping weapon scan: cancel requested");
                        else if (StopScanning) Logger.Info("Stopping weapon scan based on filtering");
                        else Logger.Info("Stopping weapon scan based on scans queued ({0} of {1})", cardsQueued, weaponCount);
                        return;
                    }
                }
                Logger.Debug("Finished queuing page of weapons. Scrolling...");

                // The last item(s) queued this page may still be awaiting correction on a worker
                // thread -- wait before scrolling the game past what's currently displayed.
                progressReporter.WaitIfCorrectionPending();

                rowsQueued += rows;

                // Page done, now scroll
                // If the number of remaining scans is shorter than a full page then
                // only scroll a few rows
                if (totalRows - rowsQueued <= rows)
                {
                    if (Navigation.GetAspectRatio() == new Size(8, 5))
                    {
                        offset = 35; // Lazy fix
                    }
                    for (int i = 0; i < 10 * (totalRows - rowsQueued) - 1; i++)
                    {
                        Navigation.sim.Mouse.VerticalScroll(-1);
                        Navigation.Wait(1);
                    }
                    Navigation.SystemWait(Navigation.Speed.Fast);
                }
                else
                {
                    // Scroll back one to keep it from getting too crazy
                    if (rowsQueued % 15 == 0)
                    {
                        Navigation.sim.Mouse.VerticalScroll(1);
                    }
                    for (int i = 0; i < 10 * rows - 1; i++)
                    {
                        Navigation.sim.Mouse.VerticalScroll(-1);
                        Navigation.Wait(1);
                    }
                    Navigation.SystemWait(Navigation.Speed.Fast);
                }
                ++page;
                (rectangles, cols, rows) = GetPageOfItems(page, acceptLess: totalRows - rowsQueued <= fullPage);
            }

            void SelectLevelSorting()
            {
                Navigation.SetCursor(
                    X: (int)(230 / 1280.0 * Navigation.GetWidth()),
                    Y: (int)(680 / 720.0 * Navigation.GetHeight()));
                Navigation.Click();
                Navigation.Wait();
                Navigation.SetCursor(
                    X: (int)(250 / 1280.0 * Navigation.GetWidth()),
                    Y: (int)(575 / 720.0 * Navigation.GetHeight()));
                Navigation.Click();
                Navigation.Wait();
            }

            void SelectQualitySorting()
            {
                Navigation.SetCursor(
                                        X: (int)(230 / 1280.0 * Navigation.GetWidth()),
                                        Y: (int)(680 / 720.0 * Navigation.GetHeight()));
                Navigation.Click();
                Navigation.Wait();
                Navigation.SetCursor(
                    X: (int)(250 / 1280.0 * Navigation.GetWidth()),
                    Y: (int)(615 / 720.0 * Navigation.GetHeight()));
                Navigation.Click();
                Navigation.Wait();
            }

            void SelectSortingMethod()
            {
                if (SortByLevel)
                {
                    Logger.Debug("Sorting by level to optimize scan time.");
                    // Check if sorted by level
                    if (CurrentSortingMethod() != "level")
                    {
                        Logger.Debug("Not already sorting by level...");
                        // If not, sort by level
                        SelectLevelSorting();
                    }
                    Logger.Debug("Inventory is sorted by level.");
                }
                else
                {
                    Logger.Debug("Sorting by quality to optimize scan time.");
                    // Check if sorted by quality
                    if (CurrentSortingMethod() != "quality")
                    {
                        Logger.Debug("Not already sorting by quality...");
                        // If not, sort by quality
                        SelectQualitySorting();
                    }
                    Logger.Debug("Inventory is sorted by quality");
                }
            }
        }

        private void QueueScan(int id)
        {
			var card = GetItemCard();

            Bitmap name, level, refinement, equipped, locked;
            name = GetItemNameBitmap(card);
            locked = GetLockedBitmap(card);
            equipped = GetEquippedBitmap(card);
            level = GetLevelBitmap(card);
            refinement = GetRefinementBitmap(card);

            //Navigation.DisplayBitmap(name);
            //Navigation.DisplayBitmap(locked);
            //Navigation.DisplayBitmap(equipped);
            //Navigation.DisplayBitmap(level);
            //Navigation.DisplayBitmap(refinement);

            // Separate to all pieces of card
            List<Bitmap> weaponImages = new List<Bitmap>
            {
                name, //0
                level,
                refinement,
                locked,
                equipped,
                card //5
            };

            bool a = false;

            bool belowRarity = GetQuality(name) < scanSettings.MinimumWeaponRarity;
            bool belowLevel = ScanLevel(level, ref a) < scanSettings.MinimumWeaponLevel;
            StopScanning = (SortByLevel && belowLevel) || (!SortByLevel && belowRarity);

            if (StopScanning || belowRarity || belowLevel)
            {
                weaponImages.ForEach(i => i.Dispose());
                return;
            }

            // Send images to worker queue
            InventoryKamera.workerChannel.Writer.TryWrite(new OCRImageCollection(weaponImages, "weapon", id));
        }

        /// <summary>
        /// Controller-mode equivalent of <see cref="QueueScan"/>: takes an already-captured card
        /// (from <see cref="ScanWeaponsViaController"/>'s navigation loop) instead of re-capturing via
        /// <see cref="InventoryScraper.GetItemCard"/>'s mouse-hover-popup region. Same filtering/queue
        /// dispatch as the mouse path so both feed the identical worker/cataloguing pipeline.
        /// </summary>
        private void QueueScanViaController(Bitmap card, int id)
        {
            Bitmap name = GetItemNameBitmapViaController(card);
            Bitmap level = GetLevelBitmapViaController(card);
            Bitmap refinement = GetRefinementBitmapViaController(card);
            Bitmap equipped = GetEquippedBitmapViaController(card);
            Bitmap locked = GetLockedBitmapViaController(card);

            List<Bitmap> weaponImages = new List<Bitmap>
            {
                name, //0
                level,
                refinement,
                locked,
                equipped,
                card //5
            };

            bool a = false;

            bool belowRarity = GetQuality(name) < scanSettings.MinimumWeaponRarity;
            bool belowLevel = ScanLevel(level, ref a) < scanSettings.MinimumWeaponLevel;

            // Safe as of 2026-07-04: ScanWeaponsViaController sorts by level/quality before scanning
            // (SetSortModeViaController, confirmed live-working), so a below-threshold item guarantees
            // every later item is too, matching QueueScan's (mouse path) own precondition.
            StopScanning = (SortByLevel && belowLevel) || (!SortByLevel && belowRarity);

            if (StopScanning || belowRarity || belowLevel)
            {
                weaponImages.ForEach(i => i.Dispose());
                return;
            }

            InventoryKamera.workerChannel.Writer.TryWrite(new OCRImageCollection(weaponImages, "weapon", id));
        }

        // Fixed dropdown order per user (2026-07-04): Level, Quality, Type.
        private static readonly string[] SortModeNames = { "Level", "Quality", "Type" };

        /// <summary>
        /// Reads the currently selected sort mode from the collapsed dropdown button (region measured
        /// with the coordinate-picker tool, 2026-07-04) and fuzzy-matches it against
        /// <see cref="SortModeNames"/>. Must be called while on the Weapons tab with the dropdown
        /// closed. Returns null if no confident match.
        /// </summary>
        private string DetectCurrentSortModeViaController()
        {
            using (var region = Navigation.CaptureRegion(
                x: (int)(0.0625 * Navigation.GetWidth()),
                y: (int)(0.9037 * Navigation.GetHeight()),
                width: (int)(0.1167 * Navigation.GetWidth()),
                height: (int)(0.0389 * Navigation.GetHeight())))
            {
                var preprocessor = new ImageProcessor();
                Bitmap processed = preprocessor.ConvertToGrayscale(region);
                preprocessor.SetContrast(60.0, ref processed);
                preprocessor.SetInvert(ref processed);

                string rawText;
                using (processed)
                using (var ocr = new OcrService())
                {
                    rawText = ocr.AnalyzeText(processed, Tesseract.PageSegMode.SingleLine).Trim();
                }

                string normalizedText = Regex.Replace(rawText.ToLower(), @"[\W]", string.Empty);
                var normalizedModes = SortModeNames.Select(m => m.ToLower()).ToArray();
                string matchedNormalized = TextNormalizer.FindClosestInList(normalizedText, new HashSet<string>(normalizedModes));
                int index = Array.IndexOf(normalizedModes, matchedNormalized);

                Logger.Debug("Sort mode OCR: rawText=\"{0}\" normalizedText=\"{1}\" matched=\"{2}\"",
                    rawText, normalizedText, index >= 0 ? SortModeNames[index] : "(none)");

                return index >= 0 ? SortModeNames[index] : null;
            }
        }

        /// <summary>
        /// Sets the weapon list's sort mode via controller (per user, 2026-07-04, live-tested
        /// working): D-pad Down opens the dropdown, the left stick moves the highlighted selection
        /// up/down within it, B confirms/closes it (the established confirm button everywhere else in
        /// this codebase). No-ops if already on <paramref name="targetMode"/>. Assumes the dropdown
        /// opens with the currently-active mode pre-highlighted, so the up/down step count can be
        /// computed from <see cref="DetectCurrentSortModeViaController"/>'s result -- confirmed live
        /// rather than just assumed. If detection fails (no confident OCR match), skips sorting
        /// entirely rather than guessing a direction.
        /// </summary>
        private void SetSortModeViaController(GameController controller, string targetMode)
        {
            string currentMode = DetectCurrentSortModeViaController();
            if (currentMode == targetMode) return;

            int currentIndex = Array.IndexOf(SortModeNames, currentMode);
            int targetIndex = Array.IndexOf(SortModeNames, targetMode);
            if (currentIndex < 0)
            {
                Logger.Warn("Could not confidently detect current weapon sort mode -- skipping sort selection.");
                return;
            }

            controller.TapButton(Xbox360Button.Down, holdMs: ScaledControllerDelay(80));
            Thread.Sleep(ScaledControllerDelay(300));

            int steps = Math.Abs(targetIndex - currentIndex);
            var direction = targetIndex > currentIndex ? GameController.MenuDirection.Down : GameController.MenuDirection.Up;
            controller.Move(direction, steps);
            Thread.Sleep(ScaledControllerDelay(100));

            controller.TapButton(Xbox360Button.B, holdMs: ScaledControllerDelay(80));
            Thread.Sleep(ScaledControllerDelay(300));

            Logger.Info("Controller weapon sort: {0} -> {1} ({2} steps {3})", currentMode, targetMode, steps, direction);
        }

        /// <summary>
        /// Controller-driven weapon scan (Phase 3 §6c) -- real replacement for <see cref="ScanWeapons"/>'s
        /// mouse click/scroll loop, wired into <c>InventoryKamera.GatherData</c>. Takes an
        /// already-connected <paramref name="controller"/> that's already inside Inventory (per user,
        /// 2026-07-04: switching between Weapons/Artifacts tabs shouldn't back all the way out to the
        /// unpaused game state and re-enter -- <c>GatherData</c> now owns one <see cref="GameController"/>
        /// and one <see cref="InventoryScraper.EnterInventoryViaController"/> call spanning every
        /// controller-driven scan phase, with each phase just switching tabs via
        /// <see cref="InventoryScraper.SwitchToTabViaController"/> instead of a full exit/re-entry).
        /// Switches to Weapons, then repeatedly reads the selected item's card and advances to the next
        /// item with a single left-stick push to the Right -- per user (2026-07-03, live-tested), the
        /// grid auto-advances/wraps to the next item in inventory order on its own, including across row
        /// boundaries, so no column/row bookkeeping is needed (an earlier Down+Left-to-column-0 manual
        /// wrap scheme, never live-tested past 3 sequential items, has been removed).
        /// STILL UNVERIFIED: end-of-list behavior for the last partial row/end of inventory, and
        /// <see cref="SetSortModeViaController"/>'s pre-highlighted-dropdown assumption. Sorts by
        /// Level or Quality first (matching <see cref="SortByLevel"/>, the same flag the mouse path
        /// uses) via <see cref="SetSortModeViaController"/> (2026-07-04), which is what makes
        /// <see cref="QueueScanViaController"/>'s early-stop-on-threshold safe again.
        /// </summary>
        /// <returns>The tab actually active once this method returns ("Weapons" on a normal run, or
        /// whatever <paramref name="knownCurrentTab"/> was if tab detection failed and switching had
        /// to be skipped) -- pass this into the next controller-driven phase's own call so it can skip
        /// re-detecting via OCR (see <see cref="InventoryScraper.SwitchToTabViaController"/>).</returns>
        public string ScanWeaponsViaController(GameController controller, int count = 0, string knownCurrentTab = null)
        {
            StopScanning = false;

            string currentTab = SwitchToTabViaController(controller, "Weapons", knownCurrentTab);
            SetSortModeViaController(controller, SortByLevel ? "Level" : "Quality");

            int weaponCount = count == 0 ? ScanItemCountViaController() : count;
            progressReporter.SetWeapon_Max(weaponCount);

            int scanned = 0;

            while (scanned < weaponCount && !InventoryKamera.CancelRequested && !StopScanning)
            {
                progressReporter.WaitIfCorrectionPending();

                // Not wrapped in `using` -- QueueScanViaController hands `card` into weaponImages,
                // which either gets disposed immediately (filtered out) or flows into the worker
                // channel for async cataloguing/disposal, matching QueueScan's mouse-path pattern.
                // Disposing it here would race the worker thread.
                Bitmap card = GetItemCardViaController();
                QueueScanViaController(card, scanned);
                scanned++;

                if (scanned >= weaponCount) break;

                // Per user (2026-07-03, live-tested): the grid auto-advances to the next item in
                // inventory order on its own, including across row boundaries -- a single left
                // stick push to the Right is all that's needed. Supersedes an earlier, never-live-
                // tested Down+Left-to-column-0 manual row-wrap scheme (and the column/
                // GetPageOfItems bookkeeping it needed). Timing is scaled by the app's existing
                // scan-speed setting via ScaledControllerDelay (2026-07-04) rather than fixed --
                // see that method's doc comment for the base values and the Fast-tier reasoning.
                // An extra fixed animation-settle wait was tried here (2026-07-04, for the
                // lock-icon check's benefit) and reverted -- measured as the direct cause of a
                // significant scan slowdown, so MoveStep's own settle is relied on alone. If lock
                // status proves unreliable live, that's the first place to look again.
                controller.MoveStep(GameController.MenuDirection.Right,
                    holdMs: ScaledControllerDelay(80), settleMs: ScaledControllerDelay(100));
            }

            Logger.Info("Controller weapon scan finished: {0} of {1} scanned (cancelled={2}, stopped={3})",
                scanned, weaponCount, InventoryKamera.CancelRequested, StopScanning);

            return currentTab;
        }

        Bitmap GetLevelBitmap(Bitmap card)
        {
            return GenshinProcesor.CopyBitmap(card,
                new Rectangle(
                    x: (int)(card.Width * 0.060),
                    y: (int)(card.Height * (Navigation.IsNormal ? 0.367 : 0.320)),
                    width: (int)(card.Width * 0.262),
                    height: (int)(card.Height * (Navigation.IsNormal ? 0.035 : 0.033))));
        }

        Bitmap GetRefinementBitmap(Bitmap card)
        {
            return GenshinProcesor.CopyBitmap(card,
                new Rectangle(
                    x: (int)(card.Width * (Navigation.IsNormal ? 0.058 : 0.057)),
                    y: (int)(card.Height * (Navigation.IsNormal ? 0.417 : 0.364)),
                    width: (int)(card.Width * (Navigation.IsNormal ? 0.074 : 0.075)),
                    height: (int)(card.Height * (Navigation.IsNormal ? 0.038 : 0.034))));
        }

        /// <summary>Controller-mode equivalent of <see cref="GetLevelBitmap"/>, measured with the
        /// coordinate-picker tool (2026-07-03).</summary>
        Bitmap GetLevelBitmapViaController(Bitmap card)
        {
            return GenshinProcesor.CopyBitmap(card,
                new Rectangle(
                    x: (int)(card.Width * 0.0564),
                    y: (int)(card.Height * 0.3102),
                    width: (int)(card.Width * 0.2618),
                    height: (int)(card.Height * 0.0352)));
        }

        /// <summary>Controller-mode equivalent of <see cref="GetRefinementBitmap"/>, measured with the
        /// coordinate-picker tool (2026-07-03).</summary>
        Bitmap GetRefinementBitmapViaController(Bitmap card)
        {
            return GenshinProcesor.CopyBitmap(card,
                new Rectangle(
                    x: (int)(card.Width * 0.0564),
                    y: (int)(card.Height * 0.3556),
                    width: (int)(card.Width * 0.0691),
                    height: (int)(card.Height * 0.0324)));
        }

        /// <summary>Controller-mode equivalent of <see cref="InventoryScraper.GetLockedBitmap"/>,
        /// measured with the coordinate-picker tool (2026-07-04). Reuses the same lock-color pixel
        /// check (<c>CatalogueFromBitmapsAsync</c>'s <c>lockedColor</c>/<c>CompareColors</c> at pixel
        /// (5,5)) as the mouse path -- it's the same in-game lock badge asset, just captured from a
        /// different on-screen card position, so the reference color should still apply.</summary>
        Bitmap GetLockedBitmapViaController(Bitmap card)
        {
            return GenshinProcesor.CopyBitmap(card,
                new Rectangle(
                    x: (int)(card.Width * 0.8545),
                    y: (int)(card.Height * 0.3083),
                    width: (int)(card.Width * 0.0764),
                    height: (int)(card.Height * 0.0389)));
        }

        public async Task<Weapon> CatalogueFromBitmapsAsync(List<Bitmap> bm, int id)
		{
			// Init Variables
			string name = null;
			int level = -1;
			bool ascended = false;
			int refinementLevel = -1;
			bool locked = false;
			string equippedCharacter = null;
			int rarity = 0;

			if (bm.Count >= 4)
			{
				int w_name = 0; int w_level = 1; int w_refinement = 2; int w_lock = 3; int w_equippedCharacter = 4;

				// Check for Rarity
				rarity = GetQuality(bm[w_name]);

				// Check for equipped color
				Color equippedColor = Color.FromArgb(255, 255, 231, 187);
				Color equippedStatus = bm[w_equippedCharacter].GetPixel(5, 5);
				bool b_equipped = GenshinProcesor.CompareColors(equippedColor, equippedStatus);

				// Check for lock color
				Color lockedColor = Color.FromArgb(255, 70, 80, 100); // Dark area around red lock
				Color lockStatus = bm[w_lock].GetPixel(5, 5);
				locked = GenshinProcesor.CompareColors(lockedColor, lockStatus);

				List<Task> tasks = new List<Task>();

				var taskName = Task.Run(() =>
				{
					name = ScanWeaponNameWithCorrection(bm[w_name]);
				});
				var taskLevel = Task.Run(() => level = ScanLevel(bm[w_level], ref ascended));
				var taskRefinement = Task.Run(() => refinementLevel = ScanRefinement(bm[w_refinement]));
				var taskEquipped = Task.Run(() => equippedCharacter = ScanEquippedCharacter(bm[w_equippedCharacter]));

				tasks.Add(taskName);
				tasks.Add(taskLevel);
				tasks.Add(taskRefinement);

				if (b_equipped)
				{
					tasks.Add(taskEquipped);
				}

				await Task.WhenAll(tasks.ToArray());
			}
			return new Weapon(name, level, ascended, refinementLevel, locked, equippedCharacter, id, rarity);
		}

        public bool IsEnhancementMaterial(Bitmap nameBitmap)
		{
			string material = ScanEnchancementOreName(nameBitmap);
			return !string.IsNullOrWhiteSpace(material) && GenshinProcesor.enhancementMaterials.Contains(material.ToLower());
		}

		public string ScanEnchancementOreName(Bitmap bm)
		{
			// Analyze
			string name = GenshinProcesor.FindClosestMaterialName(ScanItemName(bm), minConfidence: 95);

			return name;
		}

        #region Task Methods

		private string ScanWeaponName(string name)
        {
            return GenshinProcesor.FindClosestWeapon(name);
        }

        /// <summary>
        /// Weapon name recognition, gated on inline correction (Phase 3 §3.3) since a misread name
        /// silently corrupts export data -- unlike <see cref="ScanEnchancementOreName"/> (fed dozens
        /// of times per scan by enhancement fodder), this runs once per weapon actually being
        /// cataloged, so a correction popup here is rare enough not to be disruptive.
        /// </summary>
        private string ScanWeaponNameWithCorrection(Bitmap nameBitmap)
        {
            var (rawName, confidencePercent) = ScanItemNameWithConfidence(nameBitmap);
            string name = ScanWeaponName(rawName);

            Logger.Debug("Weapon name OCR: rawText=\"{0}\" matchedName=\"{1}\" confidence={2:0.0}% threshold={3}%", rawName, name, confidencePercent, scanSettings.OcrConfidenceThreshold);
            if (string.IsNullOrWhiteSpace(name) || confidencePercent < scanSettings.OcrConfidenceThreshold)
            {
                Logger.Debug("Weapon name below confidence threshold -- requesting inline correction");
                string corrected = progressReporter.RequestCorrection(nameBitmap, rawName, confidencePercent, "Weapon name");
                if (!string.IsNullOrWhiteSpace(corrected) && corrected != rawName)
                {
                    string normalized = Regex.Replace(corrected.ToLower(), @"[\W]", string.Empty);
                    name = ScanWeaponName(normalized) ?? corrected;
                }
            }

            return name;
        }

        public int ScanLevel(Bitmap bm, ref bool ascended)
		{
			Bitmap n = imagePreprocessor.ConvertToGrayscale(bm);
			imagePreprocessor.SetInvert(ref n);

			string text = ocrService.AnalyzeText(n).Trim();
			n.Dispose();
			text = Regex.Replace(text, @"(?![\d/]).", string.Empty);

			if (text.Contains('/'))
			{
				string[] temp = text.Split(new[] { '/' }, 2);

				if (temp.Length == 2)
				{
					if (int.TryParse(temp[0], out int level) && int.TryParse(temp[1], out int maxLevel))
					{
						maxLevel = (int)Math.Round(maxLevel / 10.0, MidpointRounding.AwayFromZero) * 10;
						ascended = 20 <= level && level < maxLevel;
						return level;
					}
				}
			}
			return -1;
		}

		public int ScanRefinement(Bitmap image)
		{
			for (double factor = 1; factor <= 2; factor += 0.1)
			{
				using (Bitmap up = GenshinProcesor.ScaleImage(image, factor))
				{
					Bitmap n = imagePreprocessor.ConvertToGrayscale(up);
					imagePreprocessor.SetInvert(ref n);

					string text = ocrService.AnalyzeText(n).Trim();
					n.Dispose();
					text = Regex.Replace(text, @"[^\d]", string.Empty);

					// Parse Int
					if (int.TryParse(text, out int refinementLevel) && 1 <= refinementLevel && refinementLevel <= 5)
					{
						return refinementLevel;
					}
				}
			}
			return -1;
		}

		public string ScanEquippedCharacter(Bitmap bm)
		{
			Bitmap n = imagePreprocessor.ConvertToGrayscale(bm);
			imagePreprocessor.SetContrast(60.0, ref n);

			string extractedString = ocrService.AnalyzeText(n);
			n.Dispose();

			if (extractedString != "")
			{
				var regexItem = new Regex("Equipped:");
				if (regexItem.IsMatch(extractedString))
				{
					var name = extractedString.Split(':')[1];

					name = Regex.Replace(name, @"[\W]", string.Empty).ToLower();
					name = GenshinProcesor.FindClosestCharacterName(name);

					return name;
				}
			}
			// artifact has no equipped character
			return null;
		}

		#endregion Task Methods
	}
}