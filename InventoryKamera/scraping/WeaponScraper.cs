using NLog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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