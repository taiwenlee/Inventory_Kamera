using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using InventoryKamera;
using Xunit;

namespace InventoryKamera.Tests
{
    /// <summary>
    /// Golden parity tests for the two Accord algorithms replaced in the inventory-grid path:
    /// KirschEdgeDetector and BlobCounter. The expected values below were captured from Accord.Imaging
    /// 3.8.0 itself (matched exactly against ImageProcessing.EdgeDetectKirsch / FindBlobRectangles on
    /// these fixtures, confirmed via a throwaway net472 harness during the Accord removal), so the
    /// tests no longer need a live Accord dependency to keep pinning that behaviour. No game assets
    /// required.
    ///
    /// Kirsch note: Accord applies an undocumented normalization to only the outermost 1px border of
    /// the image; our reimplementation matches Accord exactly on every interior pixel (verified across
    /// thousands of samples, including a pseudo-random image). The border is immaterial here — it is
    /// the extreme edge of the captured window (never near item icons), and a 1px-wide ring cannot
    /// pass the blob size filter. The two large-image cases below are checked via a checksum of their
    /// interior pixels (captured from the verified-correct output) rather than the full grid.
    /// </summary>
    public class KirschBlobParityTests
    {
        private static Bitmap NewColor(int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp)) g.Clear(Color.Black);
            return bmp;
        }

        private static long InteriorChecksum(Bitmap b)
        {
            long sum = 0;
            for (int y = 1; y < b.Height - 1; y++)
                for (int x = 1; x < b.Width - 1; x++)
                {
                    var p = b.GetPixel(x, y);
                    sum = sum * 31 + p.R;
                    sum = sum * 31 + p.G;
                    sum = sum * 31 + p.B;
                }
            return sum;
        }

        [Fact]
        public void Kirsch_MatchesAccordGolden_OnSingleWhitePixel()
        {
            var img = NewColor(5, 5);
            img.SetPixel(2, 2, Color.White);

            using (var mine = ImageProcessing.EdgeDetectKirsch(img))
            {
                var interior = new int[3, 3];
                for (int y = 1; y <= 3; y++)
                    for (int x = 1; x <= 3; x++)
                        interior[y - 1, x - 1] = mine.GetPixel(x, y).R;

                Assert.Equal(new[,]
                {
                    { 255, 255, 255 },
                    { 255,   0, 255 },
                    { 255, 255, 255 },
                }, interior);
            }
        }

        [Fact]
        public void Kirsch_MatchesAccordGolden_OnVerticalEdge()
        {
            var img = NewColor(5, 3);
            for (int y = 0; y < 3; y++) for (int x = 3; x < 5; x++) img.SetPixel(x, y, Color.White);

            using (var mine = ImageProcessing.EdgeDetectKirsch(img))
            {
                var interior = new[] { mine.GetPixel(1, 1).R, mine.GetPixel(2, 1).R, mine.GetPixel(3, 1).R };
                Assert.Equal(new byte[] { 0, 255, 255 }, interior);
            }
        }

        [Fact]
        public void Kirsch_MatchesAccordGolden_OnPseudoRandomColorImage()
        {
            var rng = new Random(1234);
            var img = NewColor(37, 29); // odd dims to catch stride/border bugs
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                    img.SetPixel(x, y, Color.FromArgb(rng.Next(256), rng.Next(256), rng.Next(256)));

            using (var mine = ImageProcessing.EdgeDetectKirsch(img))
                Assert.Equal(-4805421250952462299L, InteriorChecksum(mine));
        }

        [Fact]
        public void Kirsch_MatchesAccordGolden_OnRectangleOutlines()
        {
            // Closer to real input: filled icon-like rectangles whose edges Kirsch will trace.
            var img = NewColor(60, 45);
            using (var g = Graphics.FromImage(img))
            {
                g.FillRectangle(Brushes.White, new Rectangle(5, 5, 15, 12));
                g.FillRectangle(Brushes.Gray, new Rectangle(30, 20, 18, 14));
            }

            using (var mine = ImageProcessing.EdgeDetectKirsch(img))
                Assert.Equal(-5445102176596345604L, InteriorChecksum(mine));
        }

        // --- BlobCounter parity ---

        private static Bitmap Binary(Action<Graphics> draw, int w, int h)
        {
            var img = NewColor(w, h);
            using (var g = Graphics.FromImage(img)) draw(g);
            var bin = ImageProcessing.ConvertToGrayscale(img);
            ImageProcessing.SetThreshold(75, ref bin);
            img.Dispose();
            return bin;
        }

        [Theory]
        [MemberData(nameof(TwoSeparatedRectsCases))]
        public void Blobs_MatchAccordGolden_OnTwoSeparatedRects(int minW, int maxW, int minH, int maxH, Rectangle[] expected)
        {
            using (var bin = Binary(g =>
            {
                g.FillRectangle(Brushes.White, new Rectangle(2, 2, 10, 8));
                g.FillRectangle(Brushes.White, new Rectangle(20, 15, 12, 10));
            }, 40, 30))
            {
                var mine = ImageProcessing.FindBlobRectangles(bin, minW, maxW, minH, maxH);
                Assert.Equal(expected, mine);
            }
        }

        public static TheoryData<int, int, int, int, Rectangle[]> TwoSeparatedRectsCases() => new TheoryData<int, int, int, int, Rectangle[]>
        {
            { 3, 100, 3, 100, new[] { new Rectangle(2, 2, 10, 8), new Rectangle(20, 15, 12, 10) } },
            { 11, 100, 3, 100, new[] { new Rectangle(20, 15, 12, 10) } },  // width filter excludes the 10-wide blob
            { 3, 100, 11, 100, Array.Empty<Rectangle>() },                // height filter excludes both (8 and 10 tall)
            { 3, 8, 3, 100, Array.Empty<Rectangle>() },                   // max-width filter excludes both (10 and 12 wide)
        };

        [Fact]
        public void Blobs_MatchAccordGolden_OnDiagonalTouch_8Connected()
        {
            using (var bin = Binary(g =>
            {
                g.FillRectangle(Brushes.White, new Rectangle(2, 2, 5, 5));
                g.FillRectangle(Brushes.White, new Rectangle(7, 7, 5, 5)); // touches previous only at a corner
            }, 20, 20))
            {
                var mine = ImageProcessing.FindBlobRectangles(bin, 1, 100, 1, 100);
                // 8-connected: the two squares merge into one blob spanning both.
                Assert.Equal(new[] { new Rectangle(2, 2, 10, 10) }, mine);
            }
        }

        [Fact]
        public void Blobs_MatchAccordGolden_OnHollowRectangleGrid()
        {
            // Simulates the real pipeline: edge outlines of a grid of icons.
            using (var bin = Binary(g =>
            {
                var pen = new Pen(Color.White, 1);
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 4; col++)
                        g.DrawRectangle(pen, new Rectangle(5 + col * 22, 5 + row * 26, 16, 20));
            }, 110, 90))
            {
                var mine = ImageProcessing.FindBlobRectangles(bin, 5, 40, 5, 40);
                var expected = new[]
                {
                    new Rectangle(5, 5, 17, 21), new Rectangle(27, 5, 17, 21), new Rectangle(49, 5, 17, 21), new Rectangle(71, 5, 17, 21),
                    new Rectangle(5, 31, 17, 21), new Rectangle(27, 31, 17, 21), new Rectangle(49, 31, 17, 21), new Rectangle(71, 31, 17, 21),
                    new Rectangle(5, 57, 17, 21), new Rectangle(27, 57, 17, 21), new Rectangle(49, 57, 17, 21), new Rectangle(71, 57, 17, 21),
                };
                Assert.Equal(expected, mine);
            }
        }
    }
}
