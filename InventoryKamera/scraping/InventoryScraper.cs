using InventoryKamera.game;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using NLog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace InventoryKamera
{

    public enum InventoryPage
    {
        Weapons,
        Artifacts,
        CharacterDevelopmentItems,
        Food,
        Materials,
        Gadget,
        Quest,
        PreciousItems,
        Furnishings,
    }

    public enum Quality
    {
        INVALID,
        ONESTAR,
        TWOSTAR,
        THREESTAR,
        FOURSTAR,
        FIVESTAR
    }

    internal static class InventoryPageExtension
    {
        public static string ToString(this InventoryPage page)
        {
            switch (page)
            {
                case InventoryPage.CharacterDevelopmentItems:
                    return "CharDevItems";
                default:
                    return page.ToString();
            }
        }
    }

    internal class InventoryScraper
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        protected InventoryPage inventoryPage;

        protected bool SortByLevel = false;

        protected int SortByObtained = 0;

        protected readonly List<InventoryPage> materialPages;

        private List<Rectangle> prevRect;
        private int prevColumn = 0;
        private int prevRow = 0;

        protected readonly IOcrService ocrService;
        protected readonly IImagePreprocessor imagePreprocessor;
        protected readonly IScanSettings scanSettings;
        protected readonly IScanProgressReporter progressReporter;

        /// <summary>
        /// Shared with every subclass (previously duplicated as a private method on
        /// <c>ArtifactScraper</c> only) so every controller-mode capture region -- including this
        /// base class's own <see cref="DetectCurrentTabIndexViaController"/> -- can leave a visual
        /// trail when <see cref="IScanSettings.LogScreenshots"/> is on, not just artifacts.
        /// <paramref name="relativePath"/> may contain '/' to nest into subfolders (e.g.
        /// <c>"weapons/weapon0/name/name"</c>), matching the per-item folder layout
        /// <c>InventoryKamera.ProcessImageCollectionAsync</c> already uses for cataloguing failures --
        /// so scan attempts saved from here land in the same kind of organized tree instead of a flat
        /// dump of files directly under <c>./logging</c>.
        /// </summary>
        protected void SaveDebugScreenshot(Bitmap bitmap, string relativePath)
        {
            if (!scanSettings.LogScreenshots) return;

            string fullPath = Path.Combine("./logging", relativePath + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            bitmap.Save(fullPath);
        }

        public InventoryScraper(IOcrService ocrService, IImagePreprocessor imagePreprocessor, IScanSettings scanSettings, IScanProgressReporter progressReporter)
        {
            this.ocrService = ocrService;
            this.imagePreprocessor = imagePreprocessor;
            this.scanSettings = scanSettings;
            this.progressReporter = progressReporter;

            materialPages = new List<InventoryPage>();

            materialPages.AddRange(Enum.GetValues(typeof(InventoryPage)).Cast<InventoryPage>());
            materialPages.Remove(InventoryPage.Weapons);
            materialPages.Remove(InventoryPage.Artifacts);
        }

        internal bool StopScanning { get; set; }

        /// <summary>
        /// Gets the item card on the right side of the in-game screen
        /// </summary>
        /// <returns>An image of the in-game item card</returns>
        internal Bitmap GetItemCard()
        {
            // Reverted to capturing the whole window and cropping in memory (2026-07-05) -- a direct
            // Navigation.CaptureRegion(cardRectangle) here was faster (avoids grabbing the ~85% of the
            // screen that gets thrown away) but CopyFromScreen has no bounds clipping of its own, and
            // this method's percentage-based rectangle math has no equivalent safety net outside
            // GenshinProcesor.CopyBitmap's ClipToSource -- caused screenshots to spill past the window
            // edge (reported as "screenshotting the whole screen") on at least one non-4K windowed
            // setup. Worth revisiting with an explicit clamp, but reverting to the known-safe pattern
            // first rather than guessing at a fix blind.
            Rectangle cardRectangle = new Rectangle();

            using (var window = Navigation.CaptureWindow())
            {
                cardRectangle.X = (int)(window.Width * 0.6807);
                cardRectangle.Y = (int)(window.Height * (Navigation.IsNormal ? 0.1102 : 0.0989));

                cardRectangle.Width = (int)(window.Width * 0.2573);
                cardRectangle.Height = (int)(window.Height * (Navigation.IsNormal ? 0.7787 : 0.8022));

                return GenshinProcesor.CopyBitmap(window, cardRectangle);
            }
        }

        /// <summary>
        /// Parses the item name from an item card's nameplate
        /// </summary>
        /// <param name="nameplate">The item nameplate to parse</param>
        /// <returns>String parsed from nameplate</returns>
        internal string ScanItemName(Bitmap nameplate)
        {
            return ScanItemNameWithConfidence(nameplate).Text;
        }

        /// <summary>
        /// Same recognition as <see cref="ScanItemName"/>, also returning Tesseract's confidence --
        /// used by callers that gate low-confidence item names on inline correction (Phase 3 §3.3).
        /// Not the default <see cref="ScanItemName"/> return shape because one caller
        /// (<c>WeaponScraper.ScanEnchancementOreName</c>) runs on every weapon used as upgrade fodder,
        /// often dozens per scan -- gating correction there would mean a popup for nearly every junk
        /// item fed to a bulk enhancement, so only callers that specifically opt in (the weapon's own
        /// headline name) should request the confidence and act on it.
        /// </summary>
        internal (string Text, float ConfidencePercent) ScanItemNameWithConfidence(Bitmap nameplate)
        {
            GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref nameplate);
            Bitmap n = imagePreprocessor.ConvertToGrayscale(nameplate);
            imagePreprocessor.SetInvert(ref n);

            // Analyze
            var (rawText, confidence) = ocrService.AnalyzeTextWithConfidence(n, Tesseract.PageSegMode.SingleBlock);
            string text = Regex.Replace(rawText.ToLower(), @"[\W]", string.Empty);

            n.Dispose();

            return (text, confidence * 100);
        }

        /// <summary>
        /// Parses the number of items from for the current inventory page.
        /// </summary>
        /// <remarks>Note: This is primarily for the Weapons, Artifacts, and Furnishing inventories since they have
        /// specific inventory item counts and capacities. 
        /// All other inventory pages have a "shared" inventory item counts and capacities.</remarks>
        /// <param name="inventoryPage">The current inventory page</param>
        /// <returns>The number of unique items for the current inventory</returns>
        /// <exception cref="FormatException">The inventory item count could not be found where it is expected</exception>
        internal int ScanItemCount()
        {
            //Find weapon count
            Rectangle region = new Rectangle(
                x: (int)(1030 / 1280.0 * Navigation.GetWidth()),
                y: (int)(20 / 720.0 * Navigation.GetHeight()),
                width: (int)(175 / 1280.0 * Navigation.GetWidth()),
                height: (int)(25 / 720.0 * Navigation.GetHeight()));

            return ScanItemCountFromRegion(region);
        }

        /// <summary>
        /// Controller-mode equivalent of <see cref="ScanItemCount"/> -- the count label sits in a
        /// different position under controller mode's HUD layout than the mouse-mode one above.
        /// Measured with the coordinate-picker tool (2026-07-03).
        /// </summary>
        internal int ScanItemCountViaController()
        {
            Rectangle region = new Rectangle(
                x: (int)(0.8865 * Navigation.GetWidth()),
                y: (int)(0.0509 * Navigation.GetHeight()),
                width: (int)(0.0641 * Navigation.GetWidth()),
                height: (int)(0.0269 * Navigation.GetHeight()));

            return ScanItemCountFromRegion(region);
        }

        private int ScanItemCountFromRegion(Rectangle region)
        {
            using (Bitmap countBitmap = Navigation.CaptureRegion(region))
            {
                progressReporter.SetNavigation_Image(countBitmap);

                Bitmap n = imagePreprocessor.ConvertToGrayscale(countBitmap);
                imagePreprocessor.SetContrast(60.0, ref n);
                imagePreprocessor.SetInvert(ref n);

                var (rawText, confidence) = ocrService.AnalyzeTextWithConfidence(n);
                string text = rawText.Trim();
                n.Dispose();

                // Remove any non-numeric and '/' characters
                text = Regex.Replace(text, @"[^0-9/]", string.Empty);

                float confidencePercent = confidence * 100;
                Logger.Debug("{0} item count OCR: text=\"{1}\" confidence={2:0.0}% threshold={3}%", inventoryPage, text, confidencePercent, scanSettings.OcrConfidenceThreshold);
                if (string.IsNullOrWhiteSpace(text) || confidencePercent < scanSettings.OcrConfidenceThreshold)
                {
                    Logger.Debug("{0} item count below confidence threshold -- requesting inline correction", inventoryPage);
                    string corrected = progressReporter.RequestCorrection(countBitmap, text, confidencePercent, $"{inventoryPage} item count");
                    text = Regex.Replace(corrected ?? string.Empty, @"[^0-9/]", string.Empty);
                }

                if (string.IsNullOrWhiteSpace(text) || scanSettings.LogScreenshots)
                {
                    SaveInventoryBitmap(countBitmap, "ItemCount.png");
                    SaveInventoryBitmap(Navigation.CaptureWindow(), $"InventoryWindow_{Navigation.GetWidth()}x{Navigation.GetHeight()}.png");
                    if (string.IsNullOrWhiteSpace(text)) throw new FormatException($"Unable to locate {inventoryPage} item count.");
                }

                int count;
                string pageCapacity;
                switch (inventoryPage)
                {
                    case InventoryPage.Artifacts:
                        pageCapacity = "1800";
                        break;
                    default:
                        pageCapacity = "2000";
                        break;
                }

                if (Regex.IsMatch(text, "/")) // Check for slash
                {
                    count = int.Parse(text.Split('/')[0]);
                }
                else if (Regex.Matches(text, pageCapacity).Count == 1) // Remove the inventory capacity from number
                {
                    text = text.Replace(pageCapacity, string.Empty);
                    count = int.Parse(text);
                }
                else // Extreme worst case
                {
                    count = 2000;
                    Logger.Debug("Defaulted to 2000 for inventory page capacity");
                }

                return count;
            }
        }

        /// <summary>
        /// Parses the sorting method for the current inventory
        /// </summary>
        /// <param name="inventoryPage">The current inventory page</param>
        /// <returns>The inventory page's current sorting method</returns>
        internal string CurrentSortingMethod()
        {
            Rectangle region;
            switch (inventoryPage)
            {
                case InventoryPage.Weapons:
                    region = new Rectangle(
                        x: (int)(100.0 / 1280.0 * Navigation.GetWidth()),
                        y: (int)(660.0 / 720.0 * Navigation.GetHeight()),
                        width: (int)(175.0 / 1280.0 * Navigation.GetWidth()),
                        height: (int)(40.0 / 720.0 * Navigation.GetHeight()));
                    break;
                case InventoryPage.Artifacts:
                    // TODO: Update this
                    region = new Rectangle(
                        x: (int)(140.0 / 1280.0 * Navigation.GetWidth()),
                        y: (int)(660.0 / 720.0 * Navigation.GetHeight()),
                        width: (int)(175.0 / 1280.0 * Navigation.GetWidth()),
                        height: (int)(40.0 / 720.0 * Navigation.GetHeight()));
                    break;
                default:
                    throw new NotImplementedException($"{inventoryPage} cannot be sorted");
            }

            using (var bm = Navigation.CaptureRegion(region))
            {
                var g = imagePreprocessor.ConvertToGrayscale(bm);
                var mode = ocrService.AnalyzeText(g).Trim().ToLower();
                return mode.Contains("level") ? "level" : mode.Contains("quality") ? "quality" : null;
            }
        }

        internal (List<Rectangle> rectangles, int cols, int rows) ProcessScreenshot(Bitmap screenshot, double weight = 0)
        {
            // Size of an item card is the same in 16:10 and 16:9. Also accounts for character icon and resolution size.
            double base_aspect_width = 1280.0;
            double base_aspect_height = 720.0;
            var icon = new Rectangle(
                x: 0,
                y: 0,
                width: (int)(screenshot.Width * 0.0651),
                height: (int)(screenshot.Height * (Navigation.IsNormal ? 0.1417: 0.1289)));

            if (Navigation.GetAspectRatio() == new Size(8, 5))
            {
                base_aspect_height = 800.0;
            }

            // Filter for relative size of items in inventory, give or take a few pixels
            int iconMinHeight = icon.Height - ((int)(icon.Height * 0.15));
            int iconMaxHeight = icon.Height + ((int)(icon.Height * 0.15));
            int iconMinWidth = icon.Width - ((int)(icon.Width * 0.15));
            int iconMaxWidth = icon.Width + ((int)(icon.Width * 0.15));
            int blobMinHeight = (int)(iconMinHeight * (1 - weight));
            int blobMaxHeight = (int)(iconMaxHeight * (1 + weight));
            int blobMinWidth = (int)(iconMinWidth * (1 - weight));
            int blobMaxWidth = (int)(iconMaxWidth * (1 + weight));
            {
                // Image pre-processing
                screenshot = imagePreprocessor.EdgeDetectKirsch(screenshot); // Algorithm to find edges. Really good but can take ~1s
                screenshot = imagePreprocessor.ConvertToGrayscale(screenshot);
                imagePreprocessor.SetThreshold(75, ref screenshot); // Convert to black and white only based on pixel intensity

                // Note: Processing won't always detect all item rectangles on screen. Since the
                // background isn't a solid color it's a bit trickier to filter out.

                // Don't save overlapping blobs
                List<Rectangle> rectangles = new List<Rectangle>();
                List<Rectangle> blobRects = imagePreprocessor.FindBlobRectangles(screenshot, blobMinWidth, blobMaxWidth, blobMinHeight, blobMaxHeight);

                int minWidth = blobRects[0].Width;
                int minHeight = blobRects[0].Height;
                foreach (var rect in blobRects)
                {
                    bool add = true;
                    foreach (var item in rectangles)
                    {
                        Rectangle r1 = rect;
                        Rectangle r2 = item;
                        Rectangle intersect = Rectangle.Intersect(r1, r2);
                        if (intersect.Width > r1.Width * .1)
                        {
                            add = false;
                            break;
                        }
                    }
                    if (add)
                    {
                        minWidth = Math.Min(minWidth, rect.Width);
                        minHeight = Math.Min(minHeight, rect.Height);
                        rectangles.Add(rect);
                    }
                }

                // Determine X and Y coordinates for columns and rows, respectively
                var colCoords = new List<int>();
                var rowCoords = new List<int>();

                foreach (var item in rectangles)
                {
                    bool addX = true;
                    bool addY = true;
                    foreach (var x in colCoords)
                    {
                        var xC = item.Center().X;
                        if (x - 75 / base_aspect_width * screenshot.Width <= xC && xC <= x + 75 / base_aspect_width * screenshot.Width)
                        {
                            addX = false;
                            break;
                        }
                    }
                    foreach (var y in rowCoords)
                    {
                        var yC = item.Center().Y;
                        if (y - 100 / base_aspect_height * screenshot.Height <= yC && yC <= y + 100 / base_aspect_height * screenshot.Height)
                        {
                            addY = false;
                            break;
                        }
                    }
                    if (addX)
                    {
                        colCoords.Add(item.Center().X);
                    }
                    if (addY)
                    {
                        rowCoords.Add(item.Center().Y);
                    }
                }

                // Going to use X,Y coordinate pairings to build rectangles around. Items that might have been missed
                // This is quite accurate and algorithmically puts rectangles over all items on the screen that were missed.
                // The center of each of these rectangles should be a good enough spot to click.
                rectangles.Clear();
                colCoords.Sort();
                rowCoords.Sort();

                colCoords.RemoveAll(col => col > screenshot.Width * 0.65);

                foreach (var row in rowCoords)
                {
                    foreach (var col in colCoords)
                    {
                        int x = (int)(col - (minWidth * .5));
                        int y = (int)(row - (minHeight * .5));

                        rectangles.Add(new Rectangle(x, y, minWidth, minHeight));
                    }
                }

                // Remove some rectangles that somehow overlap each other. Don't think this happens
                // but it doesn't hurt to double check.
                for (int i = 0; i < rectangles.Count - 1; i++)
                {
                    for (int j = i + 1; j < rectangles.Count; j++)
                    {
                        Rectangle r1 = rectangles[i];
                        Rectangle r2 = rectangles[j];
                        Rectangle intersect = Rectangle.Intersect(r1, r2);
                        if (intersect.Width > r1.Width * .2)
                        {
                            rectangles.RemoveAt(j);
                        }
                    }
                }

                // Sort by row then by column within each row
                rectangles = rectangles.OrderBy(r => r.Top).ThenBy(r => r.Left).ToList();

                var avgWidth = rectangles.Average(r => r.Width);
                var avgHeight = rectangles.Average (r => r.Height);

                rectangles.ForEach(r =>
                {
                    r.Width = (int)avgWidth;
                    r.Height = (int)avgHeight;
                });

                return (rectangles, colCoords.Count, rowCoords.Count);
            }
        }

        // Navigation.CaptureWindow()/CaptureRegion() downscale their own output above 1080p real
        // window height (Navigation.CaptureScale) -- both the grid-detection screenshot below and
        // every OCR crop taken elsewhere get proportionally fewer pixels at 4K for free. The one
        // place that needs to know about it explicitly: rectangle.Center() gets fed straight into
        // Navigation.SetCursor/CaptureRegion by every caller of GetPageOfItems, both of which expect
        // real screen coordinates -- so rectangles are scaled back up by 1/CaptureScale immediately
        // before returning, and every caller downstream (clicking, per-item OCR-region capture) never
        // needs to know downscaling happened at all.
        private static Rectangle ScaleRectangle(Rectangle r, double inverseScale)
        {
            return new Rectangle(
                (int)(r.X * inverseScale),
                (int)(r.Y * inverseScale),
                (int)(r.Width * inverseScale),
                (int)(r.Height * inverseScale));
        }

        internal (List<Rectangle> rectangles, int cols, int rows) GetPageOfItems(int pageNum, bool acceptLess = false)
        {
            // Screenshot of inventory
            using (Bitmap screenshot = Navigation.CaptureWindow())
            {
                Bitmap processedScreenshot = new Bitmap(screenshot);
                using (Graphics g = Graphics.FromImage(processedScreenshot))
                using (var brush = new SolidBrush(Color.Black))
                {
                    // Fill Top region
                    g.FillRectangle(brush, 0, 0, processedScreenshot.Width, (int)(processedScreenshot.Height * 0.09));

                    // Fill Left region
                    g.FillRectangle(brush, 0, 0, (int)(processedScreenshot.Width * 0.05), processedScreenshot.Height);

                    // Fill Right region
                    g.FillRectangle(brush, (int)(processedScreenshot.Width * 0.7), 0, processedScreenshot.Width, processedScreenshot.Height);

                    // Fill Bottom Region
                    g.FillRectangle(brush, 0, (int)(processedScreenshot.Height * 0.9), processedScreenshot.Width, processedScreenshot.Height);
                }

                double inverseCaptureScale = 1.0 / Navigation.CaptureScale;

                try
                {
                    List<Rectangle> rectangles;
                    int cols, rows, itemCount, counter = 0;
                    double weight = 0;
                    int itemPerPage = (inventoryPage != InventoryPage.Artifacts || !Navigation.IsNormal) ? 40 : 32;
                    do
                    {
                        // rectangles stay in screenshot's own (possibly downscaled) coordinate space
                        // through the whole retry loop -- debug drawing below clones/draws onto
                        // screenshot itself, so they need to line up with it, not real coordinates.
                        (rectangles, cols, rows) = ProcessScreenshot(processedScreenshot, weight);
                        itemCount = rows * cols;
                        if (itemCount != itemPerPage && !acceptLess)
                        {
                            Logger.Warn("Unable to locate full page of weapons with weight {0}", weight);
                            Logger.Warn("Detected {0} rows and {1} columns of items", rows, cols);

                            // Generate rectangles
                            using (Bitmap copy = (Bitmap)screenshot.Clone())
                            {
                                SaveInventoryBitmap(copy, $"{inventoryPage}Inventory{pageNum}_{cols}x{rows}.png");
                                using (Graphics g = Graphics.FromImage(copy))
                                    rectangles.ForEach(r => g.DrawRectangle(new Pen(Color.Green, 2), r));
                                SaveInventoryBitmap(copy, $"{inventoryPage}Inventory{pageNum}_{cols}x{rows} - weight {weight}.png");
#if DEBUG
                                //Navigation.DisplayBitmap(copy, $"weight = {weight}");
#endif
                            }
                        }
                        else break;

                        if (itemCount <= 32)
                            weight += 0.125;
                        else
                        { weight -= 0.095; ++counter; }
                        weight = Math.Min(weight, 1);
                        rectangles = null;
                    }
                    while (itemCount != itemPerPage && weight < 1 && counter < 25 && !InventoryKamera.CancelRequested);

                    processedScreenshot.Dispose();

                    if (rectangles == null)
                    {
                        Logger.Warn("Could not find {0} items in inventory. Re-using previous item page.", itemPerPage);

                        return prevRect == null ?
                            throw new ArgumentNullException("Could not find first page of items!")
                            :
                            (prevRect, prevColumn, prevRow);
                    }
                    else
                    {
                        if (scanSettings.LogScreenshots)
                        {
                            SaveInventoryBitmap(screenshot, $"{inventoryPage}Inventory.png");
                            using (Graphics g = Graphics.FromImage(screenshot))
                                rectangles.ForEach(r => g.DrawRectangle(new Pen(Color.Green, 2), r));

                            SaveInventoryBitmap(screenshot, $"{inventoryPage}Inventory{pageNum}_{cols}x{rows} - weight {weight}.png");
                        }

                        // Only now, right before handing rectangles to callers that click/capture at
                        // real screen coordinates, map them out of screenshot's (possibly downscaled)
                        // space -- prevRect gets cached already-scaled so the fallback path above needs
                        // no further conversion.
                        if (inverseCaptureScale != 1.0) rectangles = rectangles.Select(r => ScaleRectangle(r, inverseCaptureScale)).ToList();

                        prevRect = rectangles; prevColumn = cols; prevRow = rows;
                        return (rectangles, cols, rows);
                    }

                }
                catch (Exception)
                {
                    processedScreenshot.Dispose();
                    SaveInventoryBitmap(screenshot, $"{inventoryPage}Inventory.png");
                    throw;
                }
            }

        }

        /// <summary>
        /// Determines the quality of an item based on it's nameplate
        /// </summary>
        /// <param name="nameplate">The an item card's nameplate</param>
        /// <returns>An integer representing quality from 0 - 5 (invalid - 5 star)</returns>
        internal int GetQuality(Bitmap nameplate)
        {
            var avg = imagePreprocessor.AverageColor(nameplate);
            var averageColor = Color.FromArgb((int)avg.R, (int)avg.G, (int)avg.B);

            Color fiveStar = Color.FromArgb(255, 188, 105, 50);
            Color fourStar = Color.FromArgb(255, 161, 86, 224);
            Color threeStar = Color.FromArgb(255, 81, 127, 203);
            Color twoStar = Color.FromArgb(255, 42, 143, 114);
            Color oneStar = Color.FromArgb(255, 114, 119, 138);

            var colors = new List<Color> { Color.Black, oneStar, twoStar, threeStar, fourStar, fiveStar };

            var c = GenshinProcesor.ClosestColor(colors, averageColor);

            return colors.IndexOf(c);
        }

        /// <summary>
        /// Extracts a bitmap copy of an item card's nameplate
        /// </summary>
        /// <param name="card">Bitmap of the item card</param>
        /// <returns>A bitmap copy of the item card's nameplate</returns>
        internal static Bitmap GetItemNameBitmap(Bitmap card)
        {
            return GenshinProcesor.CopyBitmap(card,
                new Rectangle(
                    x: 0,
                    y: 0,
                    width: card.Width,
                    height: (int)(card.Height * (Navigation.IsNormal ? 0.07 : 0.06))));
        }

        /// <summary>
        /// Controller-mode equivalent of <see cref="GetItemNameBitmap"/> -- distinct percentage since
        /// controller mode's always-visible card has a different layout than the mouse-hover popup.
        /// Measured with the coordinate-picker tool (2026-07-03).
        /// </summary>
        internal static Bitmap GetItemNameBitmapViaController(Bitmap card)
        {
            return GenshinProcesor.CopyBitmap(card,
                new Rectangle(
                    x: 0,
                    y: 0,
                    width: card.Width,
                    height: (int)(card.Height * 0.0574)));
        }

        /// <summary>
        /// Extracts a bitmap copy of an item card's lock status icon
        /// </summary>
        /// <param name="card">Bitmap of the item card</param>
        /// <returns>A bitmap copy of the item card's lock status icon</returns>
        internal static Bitmap GetLockedBitmap(Bitmap card, bool isSanctified = false)
        {
            double baseY = Navigation.IsNormal ? 0.353 : 0.309;
            double sanctifiedShift = Navigation.IsNormal ? 0.0520 : 0.0471;
            double yShift = isSanctified ? sanctifiedShift : 0.0;

            return GenshinProcesor.CopyBitmap(card,
                new Rectangle(
                    x: (int)(card.Width * 0.75),
                    y: (int)(card.Height * (baseY + yShift)),
                    width: (int)(card.Width * 0.0955),
                    height: (int)(card.Height * (Navigation.IsNormal ? 0.055 : 0.0495))));
        }

        /// <summary>
        /// Extracts a bitmap copy of an item card's equipped character status
        /// </summary>
        /// <param name="card">Bitmap of the item card</param>
        /// <remarks>Note: This method is only useful for equippable items (artifacts and weapons)</remarks>
        /// <returns>A bitmap copy of the item card's equipped character status.</returns>
        internal static Bitmap GetEquippedBitmap(Bitmap card)
        {
            return GenshinProcesor.CopyBitmap(card,
                new Rectangle(
                    x: (int)(card.Width * 0.15),
                    y: (int)(double)(card.Height * (double)(Navigation.IsNormal ? 0.938 : 0.943)),
                    width: card.Width,
                    height: card.Height));
        }

        /// <summary>
        /// Controller-mode equivalent of <see cref="GetEquippedBitmap"/>. Measured with the
        /// coordinate-picker tool (2026-07-03) -- the left edge matters here: an earlier guess that
        /// started far enough left to include the character portrait icon made Tesseract hallucinate
        /// stray characters even though the text itself was clean.
        /// </summary>
        internal static Bitmap GetEquippedBitmapViaController(Bitmap card)
        {
            return GenshinProcesor.CopyBitmap(card,
                new Rectangle(
                    x: (int)(card.Width * 0.1527),
                    y: (int)(card.Height * 0.9444),
                    width: (int)(card.Width * 0.8309),
                    height: (int)(card.Height * 0.0519)));
        }

        // Genshin's inventory remembers whichever tab was last open, so tab-switching can't assume a
        // known starting tab -- the tab name label gets OCR'd and fuzzy-matched against this list.
        // Shared across Weapons/Artifacts/Character Development Items (Phase 3 §6c).
        internal static readonly string[] ControllerInventoryTabNames =
        {
            "Weapons", "Artifacts", "Character Development Items", "Food", "Materials",
            "Gadget", "Quest", "Precious Items", "Furnishings",
        };

        /// <summary>
        /// Captures the selected item's always-visible detail card in controller mode -- confirmed by
        /// the user to be the same region for Weapons, Artifacts, and Character Development Items.
        /// Percentages measured with the coordinate-picker tool from a full-window capture
        /// (2026-07-03, 1920x1080). Distinct from <see cref="GetItemCard"/> (the mouse-hover popup) --
        /// controller mode's panel sits at a different position, confirmed via live testing.
        /// </summary>
        internal Bitmap GetItemCardViaController()
        {
            return Navigation.CaptureRegion(
                x: (int)(0.7031 * Navigation.GetWidth()),
                y: (int)((Navigation.IsNormal ? 0.1231 : 0.1120) * Navigation.GetHeight()),
                width: (int)(0.2167 * Navigation.GetWidth()),
                height: (int)((Navigation.IsNormal ? 0.7556 : 0.7800) * Navigation.GetHeight()));
        }
        /// <summary>
        /// Opens the pause menu and navigates into Inventory via controller -- ported from the
        /// live-verified sequence in <c>ControllerNavigationTests</c> (2026-07-05): open menu, move
        /// down twice (Inventory is at grid position [0,2]), confirm with B (Genshin's confirm button
        /// -- swapped from standard Xbox convention, A is back/cancel).
        /// </summary>
        internal void EnterInventoryViaController(GameController controller)
        {
            // The mouse-based scan's Navigation.InventoryScreen() started every phase with a real
            // Escape press to guarantee a known baseline (unpaused, no menu open) before doing
            // anything else -- controller-mode ad-hoc tests never needed this because the user
            // manually alt-tabbed into that same baseline state before clicking a test. A real scan
            // can't assume that: Genshin might already be sitting in a menu (paused, or left open by
            // a previous phase) when this runs, and EnterControllerMode()'s stick-nudge+A sequence
            // only reliably flips the input scheme from the free-roam state, not from inside a menu.
            Navigation.sim.Keyboard.KeyPress(Navigation.escapeKey);
            Navigation.SystemWait(Navigation.Speed.UI);

            // Per user (2026-07-04): "make sure the speed is hooked up" -- these were still fixed
            // regardless of the Fast/Normal/Slow setting, unlike every per-item timing elsewhere. This
            // is one-time setup (runs once per scan phase, not per item), so it's a much smaller share
            // of a full scan's total time than the per-item loop -- being generous here is nearly free.
            // Doubled again (2026-07-05) after a live tab-detection miss right after this sequence
            // ("Weapons" misread as "eepons |") -- the menu navigation itself was too fast and
            // occasionally not fully settled before the next step read the screen.
            controller.EnterControllerMode();
            Thread.Sleep(ScaledControllerDelay(2000));
            controller.OpenMenu();
            Thread.Sleep(ScaledControllerDelay(2000));
            controller.Move(GameController.MenuDirection.Down, 2, holdMs: ScaledControllerDelay(300), settleMs: ScaledControllerDelay(300));
            Thread.Sleep(ScaledControllerDelay(600));
            controller.TapButton(Xbox360Button.B, holdMs: ScaledControllerDelay(300));
            // Per user (2026-07-04): confirming into Inventory plays a screen-transition animation
            // that outlasts the plain-scaled wait -- unlike input-registration timing, an animation's
            // real duration doesn't shrink just because Fast wants quicker input pacing. Floored flat
            // regardless of speed setting (doubled 2026-07-05, same reasoning as above); this is a
            // one-time per-scan cost, so being generous here is nearly free even though the same floor
            // was rejected for the per-item advance wait.
            Thread.Sleep(Math.Max(3000, ScaledControllerDelay(2000)));
        }

        /// <summary>
        /// Captures the tab-name label (top-left of the inventory screen), OCRs it, and fuzzy-matches
        /// it against <see cref="ControllerInventoryTabNames"/>. Returns the matched index (-1 if no
        /// confident match). Must be called while already inside Inventory (see
        /// <see cref="EnterInventoryViaController"/>) and before the owning <c>GameController</c> is
        /// disposed -- disposal mashes back out of the menu as a safety net.
        /// </summary>
        internal int DetectCurrentTabIndexViaController(out string rawText)
        {
            // Height tightened 0.08 -> 0.05 (2026-07-07): the debug screenshot showed the sub-tab
            // icon row bleeding into the bottom of the crop, which likely confused Tesseract's
            // SingleLine page-seg mode into garbling otherwise-clean "Weapons" text into "Mn NT".
            // Needs live confirmation that the tab-name line itself is still fully inside the crop.
            using (var region = Navigation.CaptureRegion(
                x: (int)(0.09 * Navigation.GetWidth()),
                y: (int)(0.035 * Navigation.GetHeight()),
                width: (int)(0.20 * Navigation.GetWidth()),
                height: (int)(0.05 * Navigation.GetHeight())))
            {
                SaveDebugScreenshot(region, "tabdetection/region");

                var preprocessor = new ImageProcessor();
                Bitmap processed = preprocessor.ConvertToGrayscale(region);
                preprocessor.SetContrast(60.0, ref processed);
                preprocessor.SetInvert(ref processed);

                SaveDebugScreenshot(processed, "tabdetection/processed");

                using (processed)
                using (var ocr = new OcrService())
                {
                    rawText = ocr.AnalyzeText(processed, Tesseract.PageSegMode.SingleLine).Trim();
                }
            }

            var normalizedTabs = ControllerInventoryTabNames.Select(t => t.ToLower().Replace(" ", "")).ToArray();
            string normalizedText = Regex.Replace(rawText.ToLower(), @"[\W]", string.Empty);
            string matchedNormalized = TextNormalizer.FindClosestInList(normalizedText, new HashSet<string>(normalizedTabs));
            return Array.IndexOf(normalizedTabs, matchedNormalized);
        }

        /// <summary>
        /// Switches to <paramref name="targetTab"/> from wherever the cursor currently is, cycling
        /// LB/RB (the inventory sub-tab row's own input, distinct from the pause menu's stick-driven
        /// grid). Tabs wrap around (circular) -- takes whichever direction is fewer presses. Must run
        /// inside Inventory already (see <see cref="EnterInventoryViaController"/>).
        /// </summary>
        /// <param name="knownCurrentTab">
        /// If the caller already knows which tab is active (e.g. the previous scan phase just
        /// finished switching to and scanning that exact tab, and nothing changes tabs mid-scan), pass
        /// it here to skip OCR detection entirely. Per user (2026-07-05): re-detecting via OCR for
        /// every phase transition is both slower and less reliable than just remembering where the
        /// last phase left off -- a live miss ("Weapons" misread as "eepons J"/"eepons |") persisted
        /// even after a settle-wait and 3 retry attempts, pointing at an OCR/rendering issue with this
        /// specific capture rather than a timing one; skipping the read avoids it outright.
        /// </param>
        /// <returns>The tab actually active after this call (<paramref name="targetTab"/> on success,
        /// or the original tab if detection failed and the switch was skipped) -- pass this into the
        /// next phase's <paramref name="knownCurrentTab"/> to keep the chain going without OCR.</returns>
        internal string SwitchToTabViaController(GameController controller, string targetTab, string knownCurrentTab = null)
        {
            int currentIndex;
            string rawText = null;

            if (knownCurrentTab != null)
            {
                currentIndex = Array.IndexOf(ControllerInventoryTabNames, knownCurrentTab);
                Logger.Info("Tab switch: using known current tab \"{0}\" (skipping OCR detection).", knownCurrentTab);
            }
            else
            {
                // Per user (2026-07-05): no settle wait existed between one scan phase's last
                // per-item advance and the next phase's tab-detection capture -- contributed to the
                // "Weapons" -> "eepons |" misread below, since detection fired immediately with zero
                // margin for whatever the previous action's animation/UI was still doing.
                Thread.Sleep(ScaledControllerDelay(500));

                // Detection can also flake even with the settle above -- retry a few times before
                // giving up, rather than treating a -1 "undetected" index as a valid array index in
                // the step-count math below. That silently produced a plausible-but-wrong step count
                // once (2 presses instead of 1), which would land on the wrong tab entirely and scan
                // garbage data with no visible error.
                currentIndex = -1;
                const int maxDetectAttempts = 3;
                for (int attempt = 1; attempt <= maxDetectAttempts && currentIndex < 0; attempt++)
                {
                    currentIndex = DetectCurrentTabIndexViaController(out rawText);
                    if (currentIndex < 0 && attempt < maxDetectAttempts)
                    {
                        Logger.Warn("Tab detection attempt {0} failed (raw=\"{1}\") -- retrying.", attempt, rawText);
                        Thread.Sleep(ScaledControllerDelay(300));
                    }
                }
            }

            int targetIndex = Array.IndexOf(ControllerInventoryTabNames, targetTab);

            if (targetIndex < 0)
            {
                string warning = $"\"{targetTab}\" is not a recognized inventory tab name -- skipped tab switch.";
                Logger.Warn(warning);
                progressReporter.AddError(warning);
                return knownCurrentTab;
            }

            if (currentIndex < 0)
            {
                // Per user (2026-07-07): don't let the scan continue against a tab it can't confirm
                // -- that risks silently cataloguing items under the wrong item type. Ending this
                // phase's scan (StopScanning) is safe: WeaponScraper/ArtifactScraper/MaterialScraper's
                // own scan loops all check StopScanning before every iteration, so this stops before a
                // single item is read rather than partway through a wrong-tab grid.
                string warning = $"Could not confidently detect the current inventory tab (last OCR: \"{rawText}\") -- " +
                    $"stopping this scan phase instead of switching to {targetTab} blind.";
                Logger.Warn(warning);
                progressReporter.AddError(warning);
                StopScanning = true;
                return knownCurrentTab; // unknown tab either way; nothing better to report back
            }

            string currentTabName = ControllerInventoryTabNames[currentIndex];

            if (currentIndex == targetIndex)
            {
                Logger.Info("Tab switch: scanned in on \"{0}\" (raw=\"{1}\"), already the target -- 0 shoulder presses needed.", currentTabName, rawText);
                return targetTab;
            }

            int tabCount = ControllerInventoryTabNames.Length;
            int forwardSteps = ((targetIndex - currentIndex) % tabCount + tabCount) % tabCount;
            int backwardSteps = tabCount - forwardSteps;
            bool goForward = forwardSteps <= backwardSteps;
            int steps = Math.Min(forwardSteps, backwardSteps);
            Xbox360Button shoulderButton = goForward ? Xbox360Button.RightShoulder : Xbox360Button.LeftShoulder;

            Logger.Info("Tab switch: scanned in on \"{0}\" (raw=\"{1}\"), target \"{2}\" -- {3} {4} ({5}) presses.",
                currentTabName, rawText, targetTab, steps, shoulderButton, goForward ? "forward/right" : "backward/left");

            // Per user (2026-07-04): base tab-switch timing doubled to 200/800/1000 (through several
            // rounds: 80/100/300 -> 120/150/400 -> 240/300/800 -> 100/400/500 -> this) -- still
            // scaled by ScaledControllerDelay, just a higher starting point.
            for (int i = 0; i < steps; i++)
            {
                controller.TapButton(shoulderButton, holdMs: ScaledControllerDelay(200));
                Thread.Sleep(ScaledControllerDelay(800));
            }
            Thread.Sleep(ScaledControllerDelay(1000));

            return targetTab;
        }

        /// <summary>
        /// Scales a base controller-timing duration by the app's existing scan-speed setting
        /// (<see cref="Navigation.GetDelay"/> -- the same 0.5x/Fast, 1x/Normal, 1.5x/Slow multiplier
        /// already applied to every mouse-path scan timing via <see cref="Navigation.SystemWait(Speed)"/>),
        /// so per-item controller-scan timing speeds up/slows down consistently with the rest of the
        /// app instead of being hardcoded. Below 1x (Fast), the multiplier is squared rather than
        /// applied once -- per direct user feedback (2026-07-04), a plain 0.5x scale of an
        /// already-tuned-fast base wasn't fast enough. A 20ms floor guards against a degenerate
        /// near-zero hold if the underlying setting is ever extended below 0.5x. UNVERIFIED how low
        /// Fast's resulting values can go and still register reliably against the game -- this is new,
        /// live-testing-only territory, not something a build/compile check can confirm.
        /// </summary>
        internal static int ScaledControllerDelay(int baseMs)
        {
            double delay = Navigation.GetDelay();
            if (delay < 1) delay *= delay;
            return Math.Max(20, (int)(baseMs * delay));
        }

        internal void SaveInventoryBitmap(Bitmap image, string filename)
        {
            var path = "./logging/";

            if (materialPages.Contains(inventoryPage))
                path += "Materials/";
            else path += inventoryPage.ToString() + "/";

            // filename may itself contain '/' to nest into a subfolder (e.g. "quantity/Iron_Quantity.png")
            // -- GatherData only creates the top-level per-page folder (./logging/materials/) up front,
            // so any subfolder needs creating here rather than assuming it already exists.
            Directory.CreateDirectory(Path.GetDirectoryName(path + filename));
            image.Save(path + filename);
        }

    }
}