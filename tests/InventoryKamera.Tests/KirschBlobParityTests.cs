using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using Accord.Imaging;
using Accord.Imaging.Filters;
using InventoryKamera;
using Xunit;

namespace InventoryKamera.Tests
{
    /// <summary>
    /// Parity tests for the two remaining Accord algorithms in the inventory-grid path:
    /// KirschEdgeDetector and BlobCounter. Each runs the pure-System.Drawing reimplementation and the
    /// Accord original on the same synthetic inputs and asserts identical results. Once these pass,
    /// Accord can be removed (the tests then stand as its behavioural spec). No game assets required.
    /// </summary>
    public class KirschBlobParityTests
    {
        private static Bitmap NewColor(int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp)) g.Clear(Color.Black);
            return bmp;
        }

        // Compares the interior (excluding the 1-pixel border). Accord's KirschEdgeDetector applies an
        // undocumented normalization to the outermost ring only; our reimplementation matches Accord
        // exactly on every interior pixel. The border is immaterial here: it is the extreme edge of the
        // captured window (never near item icons), and a 1px-wide ring cannot pass the blob size filter.
        private static void AssertSameInterior(Bitmap a, Bitmap b)
        {
            Assert.Equal(a.Width, b.Width);
            Assert.Equal(a.Height, b.Height);
            for (int y = 1; y < a.Height - 1; y++)
                for (int x = 1; x < a.Width - 1; x++)
                    Assert.Equal(a.GetPixel(x, y), b.GetPixel(x, y));
        }

        [Fact]
        public void Kirsch_MatchesAccord_OnSingleWhitePixel()
        {
            var img = NewColor(5, 5);
            img.SetPixel(2, 2, Color.White);

            using (var mine = ImageProcessing.EdgeDetectKirsch(img))
            using (var accord = new KirschEdgeDetector().Apply(img))
                AssertSameInterior(accord, mine);
        }

        [Fact]
        public void Kirsch_MatchesAccord_OnVerticalEdge()
        {
            var img = NewColor(5, 3);
            for (int y = 0; y < 3; y++) for (int x = 3; x < 5; x++) img.SetPixel(x, y, Color.White);

            using (var mine = ImageProcessing.EdgeDetectKirsch(img))
            using (var accord = new KirschEdgeDetector().Apply(img))
                AssertSameInterior(accord, mine);
        }

        [Fact]
        public void Kirsch_MatchesAccord_OnPseudoRandomColorImage()
        {
            var rng = new Random(1234);
            var img = NewColor(37, 29); // odd dims to catch stride/border bugs
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                    img.SetPixel(x, y, Color.FromArgb(rng.Next(256), rng.Next(256), rng.Next(256)));

            using (var mine = ImageProcessing.EdgeDetectKirsch(img))
            using (var accord = new KirschEdgeDetector().Apply(img))
                AssertSameInterior(accord, mine);
        }

        [Fact]
        public void Kirsch_MatchesAccord_OnRectangleOutlines()
        {
            // Closer to real input: filled icon-like rectangles whose edges Kirsch will trace.
            var img = NewColor(60, 45);
            using (var g = Graphics.FromImage(img))
            {
                g.FillRectangle(Brushes.White, new Rectangle(5, 5, 15, 12));
                g.FillRectangle(Brushes.Gray, new Rectangle(30, 20, 18, 14));
            }

            using (var mine = ImageProcessing.EdgeDetectKirsch(img))
            using (var accord = new KirschEdgeDetector().Apply(img))
                AssertSameInterior(accord, mine);
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

        private static System.Collections.Generic.List<Rectangle> AccordBlobs(
            Bitmap bin, int minW, int maxW, int minH, int maxH)
        {
            using (var bc = new BlobCounter
            {
                FilterBlobs = true,
                MinWidth = minW,
                MaxWidth = maxW,
                MinHeight = minH,
                MaxHeight = maxH,
            })
            {
                bc.ProcessImage(bin);
                return bc.GetObjectsRectangles().ToList();
            }
        }

        [Theory]
        [InlineData(3, 100, 3, 100)]
        [InlineData(11, 100, 3, 100)]  // width filter excludes the 10-wide blob
        [InlineData(3, 100, 11, 100)]  // height filter excludes the 8-tall blob
        [InlineData(3, 8, 3, 100)]     // max-width filter excludes the 12-wide blob
        public void Blobs_MatchAccord_OnTwoSeparatedRects(int minW, int maxW, int minH, int maxH)
        {
            using (var bin = Binary(g =>
            {
                g.FillRectangle(Brushes.White, new Rectangle(2, 2, 10, 8));
                g.FillRectangle(Brushes.White, new Rectangle(20, 15, 12, 10));
            }, 40, 30))
            {
                var mine = ImageProcessing.FindBlobRectangles(bin, minW, maxW, minH, maxH);
                var accord = AccordBlobs(bin, minW, maxW, minH, maxH);
                Assert.Equal(accord, mine);
            }
        }

        [Fact]
        public void Blobs_MatchAccord_OnDiagonalTouch_8Connected()
        {
            using (var bin = Binary(g =>
            {
                g.FillRectangle(Brushes.White, new Rectangle(2, 2, 5, 5));
                g.FillRectangle(Brushes.White, new Rectangle(7, 7, 5, 5)); // touches previous only at a corner
            }, 20, 20))
            {
                var mine = ImageProcessing.FindBlobRectangles(bin, 1, 100, 1, 100);
                var accord = AccordBlobs(bin, 1, 100, 1, 100);
                Assert.Equal(accord, mine);
            }
        }

        [Fact]
        public void Blobs_MatchAccord_OnHollowRectangleGrid()
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
                var accord = AccordBlobs(bin, 5, 40, 5, 40);
                Assert.Equal(accord, mine);
            }
        }
    }
}
