using Accord.Imaging;
using Accord.Imaging.Filters;
using NLog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;

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

        // Material-grid geometry is calibrated once per scan session.
        // Card dimensions and spacing stay fixed; only the current grid origin
        // is allowed to translate as the inventory scroll reaches its limits.
        private bool materialGridCalibrated = false;
        private int materialGridCardWidth = 0;
        private int materialGridCardHeight = 0;
        private int materialGridColumnSpacing = 0;
        private int materialGridRowSpacing = 0;

        public InventoryScraper() 
        {
            materialPages = new List<InventoryPage>();

            materialPages.AddRange(Enum.GetValues(typeof(InventoryPage)).Cast<InventoryPage>());
            materialPages.Remove(InventoryPage.Weapons);
            materialPages.Remove(InventoryPage.Artifacts);
        }

        internal bool StopScanning { get; set; }

        protected void ResetMaterialGridCalibration()
        {
            materialGridCalibrated = false;
            materialGridCardWidth = 0;
            materialGridCardHeight = 0;
            materialGridColumnSpacing = 0;
            materialGridRowSpacing = 0;
        }

        private bool UsesMaterialSessionGrid()
        {
            return materialPages.Contains(inventoryPage);
        }

        private static int MedianInt(IEnumerable<int> values)
        {
            var ordered = values.OrderBy(value => value).ToList();
            if (ordered.Count == 0)
                return 0;

            int middle = ordered.Count / 2;
            if ((ordered.Count % 2) == 1)
                return ordered[middle];

            return (int)Math.Round((ordered[middle - 1] + ordered[middle]) / 2.0);
        }

        private static int RobustMedian(IEnumerable<int> values)
        {
            var candidates = values.Where(value => value > 0).ToList();
            if (candidates.Count == 0)
                return 0;

            int initialMedian = MedianInt(candidates);
            int tolerance = Math.Max(2, (int)Math.Round(initialMedian * 0.15));

            var filtered = candidates
                .Where(value => Math.Abs(value - initialMedian) <= tolerance)
                .ToList();

            return MedianInt(filtered.Count > 0 ? filtered : candidates);
        }

        private static int MedianSpacing(IReadOnlyList<int> coordinates)
        {
            if (coordinates.Count < 2)
                return 0;

            var spacings = new List<int>();
            for (int i = 1; i < coordinates.Count; i++)
                spacings.Add(coordinates[i] - coordinates[i - 1]);

            return RobustMedian(spacings);
        }

        private static void NormalizeCoordinatesToSpacing(
            List<int> coordinates,
            int spacing,
            int stableCoordinateCount)
        {
            if (coordinates.Count == 0 || spacing <= 0)
                return;

            int countForOrigin = Math.Min(
                Math.Max(1, stableCoordinateCount),
                coordinates.Count);

            var originCandidates = new List<int>();
            for (int i = 0; i < countForOrigin; i++)
                originCandidates.Add(coordinates[i] - (i * spacing));

            int origin = MedianInt(originCandidates);
            for (int i = 0; i < coordinates.Count; i++)
                coordinates[i] = origin + (i * spacing);
        }

        /// <summary>
        /// Gets the item card on the right side of the in-game screen
        /// </summary>
        /// <returns>An image of the in-game item card</returns>
        internal Bitmap GetItemCard()
        {
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
            GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref nameplate);
            Bitmap n = GenshinProcesor.ConvertToGrayscale(nameplate);
            GenshinProcesor.SetInvert(ref n);

            // Analyze
            string text = Regex.Replace(GenshinProcesor.AnalyzeText(n, Tesseract.PageSegMode.SingleBlock).ToLower(), @"[\W]", string.Empty);

            n.Dispose();

            return text;
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

            using (Bitmap countBitmap = Navigation.CaptureRegion(region))
            {
                UserInterface.SetNavigation_Image(countBitmap);

                Bitmap n = GenshinProcesor.ConvertToGrayscale(countBitmap);
                GenshinProcesor.SetContrast(60.0, ref n);
                GenshinProcesor.SetInvert(ref n);

                string text = GenshinProcesor.AnalyzeText(n).Trim();
                n.Dispose();

                // Remove any non-numeric and '/' characters
                text = Regex.Replace(text, @"[^0-9/]", string.Empty);

                if (string.IsNullOrWhiteSpace(text) || Properties.Settings.Default.LogScreenshots)
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
                else
                {
                    SaveInventoryBitmap(countBitmap, "ItemCount.png");
                    SaveInventoryBitmap(Navigation.CaptureWindow(), $"InventoryWindow_{Navigation.GetWidth()}x{Navigation.GetHeight()}.png");
                    throw new FormatException(
                        $"Unable to parse {inventoryPage} item count safely. OCR returned '{text}'. " +
                        "Kamera will not assume the inventory capacity because that can cause invalid grid navigation.");
                }

                if (count < 0 || count > 2000)
                {
                    throw new FormatException($"Parsed implausible {inventoryPage} item count '{count}'.");
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
                var g = GenshinProcesor.ConvertToGrayscale(bm);
                var mode = GenshinProcesor.AnalyzeText(g).Trim().ToLower();
                return mode.Contains("level") ? "level" : mode.Contains("quality") ? "quality" : null;
            }
        }

        private void RecoverMissingInternalGridCoordinates(List<int> coordinates, string axis)
        {
            if (coordinates == null || coordinates.Count < 3) return;

            coordinates.Sort();
            var gaps = new List<int>();
            for (int i = 1; i < coordinates.Count; i++)
            {
                int gap = coordinates[i] - coordinates[i - 1];
                if (gap > 0) gaps.Add(gap);
            }

            if (gaps.Count < 2) return;

            gaps.Sort();
            double medianSpacing = gaps[gaps.Count / 2];
            if (medianSpacing <= 0) return;

            var additions = new List<int>();
            for (int i = 1; i < coordinates.Count; i++)
            {
                int left = coordinates[i - 1];
                int right = coordinates[i];
                int gap = right - left;

                int segments = (int)Math.Round(gap / medianSpacing);
                if (segments < 2 || segments > 3) continue;

                double reconstructedSpacing = gap / (double)segments;
                if (Math.Abs(reconstructedSpacing - medianSpacing) > medianSpacing * 0.20)
                    continue;

                for (int segment = 1; segment < segments; segment++)
                {
                    int candidate = (int)Math.Round(left + reconstructedSpacing * segment);
                    if (!coordinates.Any(existing => Math.Abs(existing - candidate) <= medianSpacing * 0.25))
                        additions.Add(candidate);
                }
            }

            if (additions.Count > 0)
            {
                coordinates.AddRange(additions);
                coordinates.Sort();
                Logger.Info(
                    "Recovered {0} missing internal inventory {1} coordinate(s) from grid spacing: {2}",
                    additions.Count, axis, string.Join(", ", additions));
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
            using (BlobCounter blobCounter = new BlobCounter
            {
                FilterBlobs = true,
                MinHeight = (int)(iconMinHeight * (1 - weight)),
                MaxHeight = (int)(iconMaxHeight * (1 + weight)),
                MinWidth = (int)(iconMinWidth * (1 - weight)),
                MaxWidth = (int)(iconMaxWidth * (1 + weight)),
            })
            {
                // Image pre-processing
                screenshot = new KirschEdgeDetector().Apply(screenshot); // Algorithm to find edges. Really good but can take ~1s
                screenshot = new Grayscale(0.2125, 0.7154, 0.0721).Apply(screenshot);
                screenshot = new Threshold(75).Apply(screenshot); // Convert to black and white only based on pixel intensity			

                blobCounter.ProcessImage(screenshot);
                // Note: Processing won't always detect all item rectangles on screen. Since the
                // background isn't a solid color it's a bit trickier to filter out.

                // Don't save overlapping blobs
                List<Rectangle> rectangles = new List<Rectangle>();
                List<Rectangle> blobRects = blobCounter.GetObjectsRectangles().ToList();

                if (blobRects.Count == 0)
                {
                    Logger.Warn("No inventory item rectangles detected on the current screen.");
                    return (new List<Rectangle>(), 0, 0);
                }

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

                // Preserve the actual detected blobs for first-page material calibration.
                // The reconstructed green rectangles are intentionally created only after
                // reliable full-row dimensions have been established.
                var detectedRectangles = rectangles.ToList();

                // Going to use X,Y coordinate pairings to build rectangles around. Items that might have been missed
                // This is quite accurate and algorithmically puts rectangles over all items on the screen that were missed.
                // The center of each of these rectangles should be a good enough spot to click.
                rectangles.Clear();
                colCoords.Sort();
                rowCoords.Sort();

                // The detail pane is already masked beginning at 70% width. The older
                // 65% cutoff could discard a legitimate right-side inventory column at
                // some 16:9 resolutions (reported at 1600x900).
                colCoords.RemoveAll(col => col > screenshot.Width * 0.695);

                // If edge detection misses an entire internal column/row, reconstruct it
                // from neighboring grid spacing instead of silently scanning around it.
                RecoverMissingInternalGridCoordinates(colCoords, "column");
                RecoverMissingInternalGridCoordinates(rowCoords, "row");

                int gridWidth = minWidth;
                int gridHeight = minHeight;

                if (UsesMaterialSessionGrid())
                {
                    if (!materialGridCalibrated && rowCoords.Count > 1 && colCoords.Count > 1)
                    {
                        // MaterialScraper intentionally does not scan the lowest visible
                        // row because it can be a partial overlap row. Exclude that same
                        // row from geometry calibration so it cannot shrink every box.
                        var fullRowCoords = rowCoords.Take(rowCoords.Count - 1).ToList();
                        double rowTolerance = 50.0 / base_aspect_height * screenshot.Height;

                        var geometryRects = detectedRectangles
                            .Where(rectangle =>
                                rectangle.Center().X <= screenshot.Width * 0.695 &&
                                fullRowCoords.Any(row =>
                                    Math.Abs(rectangle.Center().Y - row) <= rowTolerance))
                            .ToList();

                        int calibratedWidth = RobustMedian(
                            geometryRects.Select(rectangle => rectangle.Width));
                        int calibratedHeight = RobustMedian(
                            geometryRects.Select(rectangle => rectangle.Height));
                        int calibratedColumnSpacing = MedianSpacing(colCoords);
                        int calibratedRowSpacing = MedianSpacing(fullRowCoords);

                        if (calibratedWidth > 0 &&
                            calibratedHeight > 0 &&
                            calibratedColumnSpacing > 0 &&
                            calibratedRowSpacing > 0)
                        {
                            materialGridCardWidth = calibratedWidth;
                            materialGridCardHeight = calibratedHeight;
                            materialGridColumnSpacing = calibratedColumnSpacing;
                            materialGridRowSpacing = calibratedRowSpacing;
                            materialGridCalibrated = true;

                            Logger.Info(
                                "Material grid session calibration locked: " +
                                "fullRows={0}/{1}, blobsUsed={2}, card={3}x{4}, " +
                                "columnSpacing={5}, rowSpacing={6}",
                                fullRowCoords.Count,
                                rowCoords.Count,
                                geometryRects.Count,
                                materialGridCardWidth,
                                materialGridCardHeight,
                                materialGridColumnSpacing,
                                materialGridRowSpacing);
                        }
                        else
                        {
                            Logger.Warn(
                                "Material grid calibration did not produce complete geometry. " +
                                "Falling back to current detected dimensions {0}x{1}.",
                                minWidth,
                                minHeight);
                        }
                    }

                    if (materialGridCalibrated)
                    {
                        gridWidth = materialGridCardWidth;
                        gridHeight = materialGridCardHeight;

                        // Keep the calibrated spacing constant. Re-detect only the current
                        // grid translation/origin because Genshin can shift the visible
                        // rows when scrolling reaches the bottom of the inventory.
                        NormalizeCoordinatesToSpacing(
                            colCoords,
                            materialGridColumnSpacing,
                            colCoords.Count);

                        // The last row can be partial, so use only the rows above it to
                        // establish the current vertical origin, then extrapolate the
                        // partial row using the fixed session spacing.
                        int stableRowsForOrigin = rowCoords.Count > 1
                            ? rowCoords.Count - 1
                            : rowCoords.Count;

                        NormalizeCoordinatesToSpacing(
                            rowCoords,
                            materialGridRowSpacing,
                            stableRowsForOrigin);

                        Logger.Debug(
                            "Reusing calibrated material geometry: card={0}x{1}, " +
                            "columnSpacing={2}, rowSpacing={3}, currentOrigin=({4},{5})",
                            gridWidth,
                            gridHeight,
                            materialGridColumnSpacing,
                            materialGridRowSpacing,
                            colCoords.Count > 0 ? colCoords[0] : 0,
                            rowCoords.Count > 0 ? rowCoords[0] : 0);
                    }
                }

                foreach (var row in rowCoords)
                {
                    foreach (var col in colCoords)
                    {
                        int x = (int)(col - (gridWidth * .5));
                        int y = (int)(row - (gridHeight * .5));

                        rectangles.Add(new Rectangle(x, y, gridWidth, gridHeight));
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
                try
                {
                    List<Rectangle> rectangles;
                    int cols, rows, itemCount, counter = 0;
                    double weight = 0;
                    int itemPerPage = (inventoryPage != InventoryPage.Artifacts || !Navigation.IsNormal) ? 40 : 32;
                    do
                    {
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
                    while (itemCount != itemPerPage && weight < 1 && counter < 25);

                    if (Properties.Settings.Default.LogScreenshots)
                    {
                        SaveInventoryBitmap(screenshot, $"{inventoryPage}Inventory.png");
                        using (Graphics g = Graphics.FromImage(screenshot))
                            rectangles.ForEach(r => g.DrawRectangle(new Pen(Color.Green, 2), r));

                        SaveInventoryBitmap(screenshot, $"{inventoryPage}Inventory{pageNum}_{cols}x{rows} - weight {weight}.png");
                    }
                    processedScreenshot.Dispose();

                    if (rectangles == null || rectangles.Count == 0 || cols == 0 || rows == 0)
                    {
                        Logger.Warn("Could not find {0} items in inventory. Re-using previous item page when available.", itemPerPage);

                        return prevRect == null ?
                            throw new FormatException(
                                $"Unable to locate the first {inventoryPage} inventory grid. " +
                                "Verify that Genshin is on the expected inventory screen.")
                            :
                            (prevRect, prevColumn, prevRow);
                    }
                    else
                    {
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
        internal static int GetQuality(Bitmap nameplate)
        {
            var averageColor = new ImageStatistics(nameplate);

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
        /// Extracts a deliberately wider lock-status region for the current weapon card UI.
        /// The previous crop was too far left for the 7.0 weapon detail panel and could miss
        /// the lock badge entirely.
        /// </summary>
        internal static Bitmap GetWeaponLockedBitmap(Bitmap card)
        {
            double y = Navigation.IsNormal ? 0.345 : 0.295;
            double height = Navigation.IsNormal ? 0.090 : 0.105;

            return GenshinProcesor.CopyBitmap(card,
                new Rectangle(
                    x: (int)(card.Width * 0.80),
                    y: (int)(card.Height * y),
                    width: (int)(card.Width * 0.18),
                    height: (int)(card.Height * height)));
        }

        /// <summary>
        /// Detects the coral/red lock glyph across a region instead of relying on one
        /// hard-coded background pixel. This tolerates small UI shifts, antialiasing,
        /// resolution scaling, and modest color changes.
        /// </summary>
        internal static bool IsLockedWeapon(Bitmap lockBitmap)
        {
            if (lockBitmap == null || lockBitmap.Width == 0 || lockBitmap.Height == 0)
                return false;

            int matchingPixels = 0;
            int totalPixels = lockBitmap.Width * lockBitmap.Height;
            int requiredPixels = Math.Max(12, (int)Math.Ceiling(totalPixels * 0.005));

            for (int y = 0; y < lockBitmap.Height; y++)
            {
                for (int x = 0; x < lockBitmap.Width; x++)
                {
                    Color pixel = lockBitmap.GetPixel(x, y);
                    float hue = pixel.GetHue();
                    float saturation = pixel.GetSaturation();
                    float brightness = pixel.GetBrightness();

                    bool redHue = hue <= 25.0f || hue >= 335.0f;
                    bool redDominant =
                        pixel.R >= 170 &&
                        pixel.R >= pixel.G + 35 &&
                        pixel.R >= pixel.B + 30;

                    if (redHue && saturation >= 0.30f && brightness >= 0.45f && redDominant)
                    {
                        matchingPixels++;
                        if (matchingPixels >= requiredPixels)
                            return true;
                    }
                }
            }

            return false;
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

        internal void SaveInventoryBitmap(Bitmap image, string filename)
        {
            var path = "./logging/";

            if (materialPages.Contains(inventoryPage))
                path += "Materials/";
            else path += inventoryPage.ToString() + "/";

            image.Save(path + filename);
        }

    }
}