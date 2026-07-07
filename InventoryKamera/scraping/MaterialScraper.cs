using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace InventoryKamera
{
    [Serializable]
	public struct Material : ISerializable
	{
		public string name;
		public int count;

		public Material(string _name, int _count)
		{
			name = _name;
			count = _count;
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context) => info.AddValue(name, count);

		public override int GetHashCode()
		{
			return name.GetHashCode();
		}

		public override bool Equals(object obj)
		{
			return obj is Material material && name == material.name;
		}
	}

	internal class MaterialScraper : InventoryScraper
	{
		private static NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();


		public MaterialScraper(IOcrService ocrService, IImagePreprocessor imagePreprocessor, IScanSettings scanSettings, IScanProgressReporter progressReporter) : base(ocrService, imagePreprocessor, scanSettings, progressReporter)
		{
			inventoryPage = InventoryPage.CharacterDevelopmentItems;
		}

		public MaterialScraper(IOcrService ocrService, IImagePreprocessor imagePreprocessor, IScanSettings scanSettings, IScanProgressReporter progressReporter, InventoryPage section) : base(ocrService, imagePreprocessor, scanSettings, progressReporter)
		{
			inventoryPage = section;
		}

		internal void SetInventoryPage(InventoryPage page)
		{
			if (materialPages.Contains(page)) inventoryPage = page;
		}

		private int ScanMora()
		{
			var region = new Rectangle(
				x: (int)(125 / 1280.0 * Navigation.GetWidth()),
				y: (int)(665 / 720.0 * Navigation.GetHeight()),
				width: (int)(300 / 1280.0 * Navigation.GetWidth()),
				height: (int)(30 / 720.0 * Navigation.GetHeight()));

			if (Navigation.GetAspectRatio() == new Size(8, 5))
			{
				region.Y = (int)( 740 / 800.0 * Navigation.GetHeight() );
			}

			using (var screenshot = Navigation.CaptureRegion(region))
			{
				string mora = ParseMoraFromScreenshot(screenshot);

				if (int.TryParse(mora, out int count))
				{
					progressReporter.ResetCharacterDisplay();
					progressReporter.SetMora(screenshot, count);
                    if (scanSettings.LogScreenshots) 
						SaveInventoryBitmap(screenshot, "mora.png");
				}
				else
				{
					progressReporter.SetNavigation_Image(screenshot);
					progressReporter.AddError("Unable to parse mora count");
					SaveInventoryBitmap(screenshot, "mora.png");
				}
				return count;
			}
		}

		public string ParseMoraFromScreenshot(Bitmap screenshot)
		{
			using (var gray = imagePreprocessor.ConvertToGrayscale(screenshot))
			{
				var invert = (Bitmap)gray.Clone();
				imagePreprocessor.SetInvert(ref invert);
				var input = ocrService.AnalyzeText(invert).Split(' ').ToList();
				Logger.Debug("Scanned mora input: {0}", input.ToString());
				input.RemoveAll(e => Regex.IsMatch(e.Trim(), @"[^0-9]") || string.IsNullOrWhiteSpace(e.Trim()));
				var mora = input.LastOrDefault();
				Logger.Debug("Parsed mora input: {0}", mora);
				return mora;
			}
		}

		public string ScanMaterialName(out Bitmap nameplate)
		{
			// Grab item name on right
			var refWidth = 1280.0;
			var refHeight = Navigation.GetAspectRatio() == new Size(16,9) ? 720.0 : 800.0;

			var width = Navigation.GetWidth();
			var height = Navigation.GetHeight();

			var reference = new Rectangle(872, 80, 327, 37);

			// Nameplate is in the same place in 16:9 and 16:10
			var region= new RECT(
				Left:   (int)( reference.Left   / refWidth  * width),
				Top:    (int)( reference.Top    / refHeight * height),
				Right:  (int)( reference.Right  / refWidth  * width),
				Bottom: (int)( reference.Bottom / refHeight * height));

			Bitmap bm = Navigation.CaptureRegion(region);
			nameplate = (Bitmap)bm.Clone();

			// Alter Image
			GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref bm);
			Bitmap n = imagePreprocessor.ConvertToGrayscale(bm);
			imagePreprocessor.SetInvert(ref n);

			string text = ocrService.AnalyzeText(n,Tesseract.PageSegMode.Auto);
			text = Regex.Replace(text, @"[\W\s]", string.Empty).ToLower();

			//UI
			n.Dispose();
			bm.Dispose();

			if (inventoryPage == InventoryPage.CharacterDevelopmentItems)
				return GenshinProcesor.FindClosestDevelopmentName(text);

			if (inventoryPage == InventoryPage.Materials)
				return GenshinProcesor.FindClosestMaterialName(text);

			return null;
		}

		public int ScanMaterialCount(Rectangle rectangle, out Bitmap quantity)
		{
			Dictionary<int, int> counts = new Dictionary<int, int>();
			var region = new RECT(
				Left: rectangle.X,
				Top: (int)(rectangle.Y + (0.8 * rectangle.Height)), // Only get the bottom of inventory item
				Right: rectangle.Right,
				Bottom: rectangle.Bottom + 10);

            var rRange = new IntRange(0, 150);
            var bRange = new IntRange(0, 150);
            var gRange = new IntRange(0, 150);

            using (Bitmap bm = Navigation.CaptureRegion(region))
			{
				quantity = (Bitmap)bm.Clone();

				for (var scale = 1.0; scale <= 3; scale += 0.5)
                {
					using (Bitmap rescaled = GenshinProcesor.ResizeImage(bm, (int)(bm.Width * scale), (int)(bm.Height * scale)))
                    {
                        Bitmap copy = (Bitmap)rescaled.Clone();
                        imagePreprocessor.FilterColors(ref copy, rRange, bRange, gRange);

                        for (int i = 0; i < copy.Width; i++)
                            for (int j = 0; j < copy.Height * 0.25; j++)
                                copy.SetPixel(i, j, Color.White);

                        Bitmap n = imagePreprocessor.ConvertToGrayscale(copy);
                        
						imagePreprocessor.SetInvert(ref n);
                        imagePreprocessor.SetThreshold(50, ref n);

                        string original = ocrService.AnalyzeText(n).Trim();

						n.Dispose();
						copy.Dispose();

                        if (int.TryParse(original, out int val)) return val;

                        // Might be worth it to train some more numbers
                        var cleaned = original;

                        cleaned = cleaned.Replace("M", "111");

                        cleaned = Regex.Replace(cleaned, @"[^0-9]", string.Empty);

                        int.TryParse(cleaned, out val);

                        Logger.Debug($"Scanned: {original} -> Regex: {cleaned} -> Parsed: {val}");

                        if (counts.TryGetValue(val, out var counter))
                        {
                            if (counter >= 3 && val != 0) return val;
                            counts[val]++;
                        }
                        else
                        {
                            counts.Add(val, 1);
                        }
                    }
                }
			}
			
			var nullableMode = SafeExtractMaxCounter(counts);
			if (nullableMode == null)
				return 0;

			var mode = nullableMode.Value;
			if (mode.Key == 0 && counts.Count >= 5) 
				return 0;
			
			counts.Remove(mode.Key);
			return SafeExtractMaxCounter(counts)?.Value ?? 0;
		}

		private static KeyValuePair<int, int>? SafeExtractMaxCounter(Dictionary<int, int> counts)
		{
            return counts.Count == 0 ? null : (KeyValuePair<int, int>?)counts.Aggregate((l, r) => l.Value > r.Value ? l : r);
        }

		/// <summary>
		/// Controller-mode equivalent of <see cref="ScanMaterialName"/>: reads from the always-visible
		/// detail card (<see cref="InventoryScraper.GetItemNameBitmapViaController"/>) instead of the
		/// mouse-hover popup region. Per user (2026-07-05), the card is shared by Materials and
		/// Character Development Items the same way it already is for Weapons/Artifacts.
		/// </summary>
		private string ScanMaterialNameViaController(Bitmap nameBitmap)
		{
			// SetGamma/ConvertToGrayscale clone and reassign rather than mutate in place -- plain
			// locals (not `using`-declared) since C# disallows passing a using variable by ref, and
			// each stage's bitmap is disposed explicitly once the next stage's copy exists.
			Bitmap bm = (Bitmap)nameBitmap.Clone();
			GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref bm);

			Bitmap n = imagePreprocessor.ConvertToGrayscale(bm);
			bm.Dispose();
			imagePreprocessor.SetInvert(ref n);

			string text;
			using (n)
			{
				text = ocrService.AnalyzeText(n, Tesseract.PageSegMode.Auto);
			}
			text = Regex.Replace(text, @"[\W\s]", string.Empty).ToLower();

			if (inventoryPage == InventoryPage.CharacterDevelopmentItems)
				return GenshinProcesor.FindClosestDevelopmentName(text);

			if (inventoryPage == InventoryPage.Materials)
				return GenshinProcesor.FindClosestMaterialName(text);

			return null;
		}

		// Controller mode's grid is 10 items per row (confirmed by user, 2026-07-05) -- also used as
		// ArtifactScraper.ScanArtifactsViaController's per-row page cap.
		private const int itemsPerRow = 10;

		// 5 rows fit on screen before the grid needs to scroll at 16:9 (confirmed by user, 2026-07-05);
		// 6 rows fit at 16:10 (confirmed by user, 2026-07-07 -- the taller window shows one more row
		// before Genshin needs to snap-scroll). Once the selection advances past the last pre-scroll
		// row (0-based index RowsVisiblePreScroll - 1), Genshin auto-snap-scrolls exactly one row at a
		// time, always re-pinning the newly-active row to the same fixed on-screen position -- per
		// user: "row 6 and up are all on the same scrolled row near the bottom of the page" (16:9
		// case). That means only RowsVisiblePreScroll + 1 fixed y-positions ever exist (the pre-scroll
		// rows, plus one shared "scrolled" position for every row past that), not one per inventory row.
		private static int RowsVisiblePreScroll => Navigation.IsNormal ? 5 : 6;

		// Percentages measured with the coordinate-picker tool against a full-window capture
		// (2026-07-05, 3837x2158, 16:9) of three known grid positions: row 0/col 0 (pre-scroll), row
		// 4/col 9 (last pre-scroll row), and row 5/col 0 (first post-scroll row). Column spacing
		// computed from the first two ((0.6325 - 0.0831) / 9); cross-checked by reapplying it to row
		// 4's x, which reproduced 0.6325 exactly, confirming both the column formula and that columns
		// don't shift between rows. Row spacing computed from the same two points across 4 row-steps
		// (0 to 4). Width/height averaged across all three measurements (~0.0510 x ~0.0206).
		//
		// 16:10 row 0/col 0 and row 5/col 9 (last pre-scroll row, since RowsVisiblePreScroll is 6 at
		// 16:10) measured the same way (2026-07-07, 1680x1050): (0.0833, 0.1943) and (0.6327, 0.7771).
		// Column step from these ((0.6327 - 0.0833) / 9 = 0.061044) matches 16:9's column step almost
		// exactly, confirming column spacing is resolution-independent (only letterboxed vertically) --
		// so quantityBaseX/quantityColStep are shared, unbranched, same as every other x-axis value in
		// this codebase's 16:9/16:10 handling.
		//
		// 16:10 first post-scroll row (row 6/col 0, measured 2026-07-07 from FullInventory_PostScroll,
		// 1680x1050): (0.0851, 0.7857) -- gives QuantityPostScrollY. STILL UNVERIFIED for 16:10: the
		// two drift-per-scroll-row constants below (needed for row 7+) are still 16:9-only -- only one
		// post-scroll row has been measured at 16:10 so far, and drift needs at least two post-scroll
		// rows to compute a rate. Reusing the 16:9 rate as a placeholder until a second 16:10
		// post-scroll row (e.g. row 7) is measured.
		private const double quantityColStep = 0.061044;
		private const double quantityBaseX = 0.0831;
		private static double QuantityRow0Y => Navigation.IsNormal ? 0.2150 : 0.1943;
		private static double QuantityLastPreScrollRowY => Navigation.IsNormal ? 0.7340 : 0.7771;
		private static double QuantityRowStep => (QuantityLastPreScrollRowY - QuantityRow0Y) / (RowsVisiblePreScroll - 1);
		private static double QuantityPostScrollY => Navigation.IsNormal ? 0.7674 : 0.7900;
		private static double QuantityWidth => Navigation.IsNormal ? 0.0510 : 0.0509;
		private static double QuantityHeight => Navigation.IsNormal ? 0.0206 : 0.01615;

		// Widening the crop height to absorb scroll drift (2026-07-05) was tried and reverted --
		// broke digit OCR entirely once the top-whiteout band (tuned for this tight height, see
		// ScanQuantityBitmapViaController) started eating into the taller crop's actual digits. Per
		// user: the real cause is each successive scroll (every row advance past row 4) shifting the
		// quantity position down by a small, roughly constant amount -- not a one-time offset that a
		// taller crop could just absorb. Modeled directly below instead via a per-scroll drift.
		// Per user (2026-07-05): Materials and Character Development Items drift by different amounts
		// -- plausibly because Genshin's scroll-snap distance is proportional to the tab's total item
		// count (a longer list scrolling a smaller fraction per snap) rather than a fixed pixel amount,
		// so the two tabs need separate constants rather than one shared value. Tune directly here.
		private const double quantityDriftPerScrollRowMaterials = 0.0015;
		private const double quantityDriftPerScrollRowCharDevItems = 0.001;

		/// <summary>
		/// Computes the quantity readout's on-screen rectangle for a given inventory row/column via
		/// fixed percentages instead of per-item blob detection (<see cref="InventoryScraper.GetPageOfItems"/>)
		/// -- chosen over that approach for scan speed, at the cost of needing the measurements/model
		/// below to be correct up front. Rows 0-4 use directly measured per-row positions; row 5 onward
		/// don't land on a single fixed position the way first assumed -- per user (2026-07-05, live
		/// testing), each successive scroll drifts the quantity position down by a small roughly-
		/// constant amount (<see cref="quantityDriftPerScrollRow"/>), so this accumulates that drift per
		/// scroll rather than reusing one constant y for every row past 4.
		/// STILL UNVERIFIED, not yet confirmed correct live: the drift amount/direction is a first
		/// estimate (~1% of window height per scroll) from a single round of live testing, not a
		/// direct measurement the way rows 0/4/5's positions were -- may need retuning, and may not
		/// stay linear indefinitely for very large inventories.
		/// </summary>
		private Rectangle GetQuantityRegionViaController(int globalRow, int column)
		{
			double x = quantityBaseX + column * quantityColStep;
			double y;
			if (globalRow < RowsVisiblePreScroll)
			{
				y = QuantityRow0Y + globalRow * QuantityRowStep;
			}
			else
			{
				// The last pre-scroll row lands at QuantityPostScrollY; every row after that adds one
				// more scroll's worth of drift on top of it -- rate depends on which tab we're in (see
				// the constants' own comment).
				double driftPerScrollRow = inventoryPage == InventoryPage.CharacterDevelopmentItems
					? quantityDriftPerScrollRowCharDevItems
					: quantityDriftPerScrollRowMaterials;
				int scrollsPast = globalRow - RowsVisiblePreScroll;
				y = QuantityPostScrollY + scrollsPast * driftPerScrollRow;
			}

			return new Rectangle(
				x: (int)(x * Navigation.GetWidth()),
				y: (int)(y * Navigation.GetHeight()),
				width: (int)(QuantityWidth * Navigation.GetWidth()),
				height: (int)(QuantityHeight * Navigation.GetHeight()));
		}

		/// <summary>
		/// Controller-mode equivalent of <see cref="ScanMaterialCount"/>'s digit-recognition pipeline
		/// (multi-scale color filter -> grayscale -> invert -> threshold -> OCR, majority-vote fallback
		/// across scales) -- identical to that method's logic, just fed a bitmap already captured from
		/// <see cref="GetQuantityRegionViaController"/> instead of re-deriving a grid-cell's bottom
		/// strip from a mouse-detected rectangle (there is no such rectangle in controller mode).
		/// </summary>
		private int ScanQuantityBitmapViaController(Bitmap quantityBitmap)
		{
			Dictionary<int, int> counts = new Dictionary<int, int>();

			var rRange = new IntRange(0, 150);
			var bRange = new IntRange(0, 150);
			var gRange = new IntRange(0, 150);

			for (var scale = 1.0; scale <= 3; scale += 0.5)
			{
				using (Bitmap rescaled = GenshinProcesor.ResizeImage(quantityBitmap, (int)(quantityBitmap.Width * scale), (int)(quantityBitmap.Height * scale)))
				{
					Bitmap copy = (Bitmap)rescaled.Clone();
					imagePreprocessor.FilterColors(ref copy, rRange, bRange, gRange);

					for (int i = 0; i < copy.Width; i++)
						for (int j = 0; j < copy.Height * 0.25; j++)
							copy.SetPixel(i, j, Color.White);

					Bitmap n = imagePreprocessor.ConvertToGrayscale(copy);

					imagePreprocessor.SetInvert(ref n);
					imagePreprocessor.SetThreshold(50, ref n);

					string original = ocrService.AnalyzeText(n).Trim();

					n.Dispose();
					copy.Dispose();

					if (int.TryParse(original, out int val)) return val;

					var cleaned = original;
					cleaned = cleaned.Replace("M", "111");
					cleaned = Regex.Replace(cleaned, @"[^0-9]", string.Empty);

					int.TryParse(cleaned, out val);

					Logger.Debug($"Scanned (controller): {original} -> Regex: {cleaned} -> Parsed: {val}");

					if (counts.TryGetValue(val, out var counter))
					{
						if (counter >= 3 && val != 0) return val;
						counts[val]++;
					}
					else
					{
						counts.Add(val, 1);
					}
				}
			}

			var nullableMode = SafeExtractMaxCounter(counts);
			if (nullableMode == null)
				return 0;

			var mode = nullableMode.Value;
			if (mode.Key == 0 && counts.Count >= 5)
				return 0;

			counts.Remove(mode.Key);
			return SafeExtractMaxCounter(counts)?.Value ?? 0;
		}

		/// <summary>
		/// Controller-driven materials/character-development-items scan (Phase 3 §6c). Name is read via
		/// the same always-visible detail card technique as
		/// <see cref="WeaponScraper.ScanWeaponsViaController"/>/<see cref="ArtifactScraper.ScanArtifactsViaController"/>.
		/// Quantity is NOT on that card, though -- a coordinate-picker measurement of three different
		/// items (2026-07-05) put their quantity readouts at three different on-screen positions,
		/// confirming quantity is tied to the item's grid slot, not any fixed card region, the same way
		/// it is in mouse mode. Per user (2026-07-05), computed directly from fixed percentages
		/// (<see cref="GetQuantityRegionViaController"/>) rather than per-item blob detection
		/// (<see cref="InventoryScraper.GetPageOfItems"/>, which was tried first and works but re-screenshots
		/// and re-analyzes the whole window every single item) -- this trades a small one-time
		/// measurement/maintenance cost for meaningfully faster scanning, matching how Weapons/Artifacts
		/// already avoid any per-item detection.
		/// Since this tab has no per-page item-count readout
		/// (<see cref="InventoryScraper.ScanItemCountViaController"/> only covers Weapons/Artifacts/
		/// Furnishings), this mirrors mouse-mode <see cref="Scan_Materials"/>'s own stopping condition:
		/// stop once a scanned name repeats one already recorded in <paramref name="inventory"/>.
		/// STILL UNVERIFIED, none of this has been live-tested yet: the fixed quantity percentages
		/// above (only 3 of the 6 possible row positions were directly measured), and whether a single
		/// right-stick step reliably advances/auto-scrolls through this grid the same way it does for
		/// Weapons.
		/// </summary>
		/// <returns>The tab actually active once this method returns, to hand into the next
		/// controller-driven phase (see <see cref="InventoryScraper.SwitchToTabViaController"/>).</returns>
		internal string ScanMaterialsViaController(GameController controller, ref Inventory inventory, string knownCurrentTab = null)
		{
			StopScanning = false;

			string targetTab = inventoryPage == InventoryPage.CharacterDevelopmentItems
				? "Character Development Items"
				: "Materials";

			string currentTab = SwitchToTabViaController(controller, targetTab, knownCurrentTab);

			// Full-window reference shot of the grid before scanning starts -- useful alongside the
			// per-item quantity crops for judging whether GetQuantityRegionViaController's fixed
			// percentages actually line up with the real grid (e.g. the 16:10 row-count/spacing gap).
			if (scanSettings.LogScreenshots)
			{
				using (var fullInventory = Navigation.CaptureWindow())
				{
					SaveInventoryBitmap(fullInventory, $"FullInventory_{Navigation.GetWidth()}x{Navigation.GetHeight()}.png");
				}
			}

			int scanned = 0;
			int column = 0;
			int globalRow = 0;
			int consecutiveEmptyReads = 0;
			const int maxConsecutiveEmptyReads = 3;
			bool loggedPostScrollShot = false;

			while (!InventoryKamera.CancelRequested && !StopScanning)
			{
				progressReporter.WaitIfCorrectionPending();

				// One-time full-window capture right as the grid crosses into post-scroll territory --
				// this is the point GetQuantityRegionViaController's post-scroll y/drift constants need
				// to be measured against (still unmeasured for 16:10, per the pending remeasurement
				// noted on RowsVisiblePreScroll/QuantityRowStep above).
				if (scanSettings.LogScreenshots && !loggedPostScrollShot && globalRow == RowsVisiblePreScroll)
				{
					loggedPostScrollShot = true;
					using (var postScrollShot = Navigation.CaptureWindow())
					{
						SaveInventoryBitmap(postScrollShot, $"FullInventory_PostScroll_{Navigation.GetWidth()}x{Navigation.GetHeight()}.png");
					}
				}

				using (Bitmap card = GetItemCardViaController())
				using (Bitmap nameBitmap = GetItemNameBitmapViaController(card))
				{
					string name = ScanMaterialNameViaController(nameBitmap);
					Material material = new Material(name, 0);

					if (string.IsNullOrEmpty(name))
					{
						consecutiveEmptyReads++;
						if (consecutiveEmptyReads >= maxConsecutiveEmptyReads)
						{
							Logger.Debug("{0} consecutive unreadable item names -- assuming end of {1} list, stopping controller scan.",
								consecutiveEmptyReads, inventoryPage);
							break;
						}
					}
					else
					{
						consecutiveEmptyReads = 0;
					}

					if (!string.IsNullOrEmpty(name) && inventory.Materials.Contains(material))
					{
						Logger.Debug("Repeat material found ({0}) -- stopping controller {1} scan.", name, inventoryPage);
						break;
					}

					if (!string.IsNullOrEmpty(name))
					{
						using (Bitmap quantity = Navigation.CaptureRegion(GetQuantityRegionViaController(globalRow, column)))
						{
							int count = ScanQuantityBitmapViaController(quantity);
							if (count == 0)
							{
								progressReporter.AddError($"Failed to parse quantity for {name}");
							}
							if (scanSettings.LogScreenshots || count == 0)
							{
								SaveInventoryBitmap(quantity, $"quantity/{name}_Quantity.png");
							}

							material.count = count;
							inventory.Materials.Add(material);
							progressReporter.ResetCharacterDisplay();
							progressReporter.SetMaterial(nameBitmap, quantity, name, count);
							scanned++;
						}
					}
				}

				controller.MoveStep(GameController.MenuDirection.Right,
					holdMs: ScaledControllerDelay(80), settleMs: ScaledControllerDelay(100));

				column++;
				if (column >= itemsPerRow)
				{
					column = 0;
					globalRow++;
				}
			}

			Logger.Info("Controller {0} scan finished: {1} scanned (cancelled={2}, stopped={3})",
				inventoryPage, scanned, InventoryKamera.CancelRequested, StopScanning);

			// Always report targetTab here, not whatever SwitchToTabViaController returned -- see
			// WeaponScraper.ScanWeaponsViaController's matching comment: by the time we get here the
			// inventory is on targetTab regardless of whether the pre-switch OCR detection succeeded,
			// and reporting the real value lets the next phase skip re-running that same flaky OCR.
			return targetTab;
		}
    }
}