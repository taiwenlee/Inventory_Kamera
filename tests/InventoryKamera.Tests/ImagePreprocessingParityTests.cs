using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using Accord;
using InventoryKamera;
using Xunit;

namespace InventoryKamera.Tests
{
    /// <summary>
    /// Golden parity tests for the image pre-processing operations. They pin the exact pixel output
    /// of the current Accord.Imaging backend so it can be reimplemented without Accord while proving
    /// identical results. OCR only ever sees the pre-processed image, so pixel-for-pixel parity here
    /// guarantees scan behaviour is unchanged — no game screenshots required.
    ///
    /// Golden values were captured from Accord.Imaging 3.8.0.
    /// </summary>
    public class ImagePreprocessingParityTests
    {
        // A fixed palette exercising channel extremes, mid-grey, and a mixed colour.
        private static readonly Color[] Palette =
        {
            Color.FromArgb(255, 0, 0),      // red
            Color.FromArgb(0, 255, 0),      // green
            Color.FromArgb(0, 0, 255),      // blue
            Color.FromArgb(255, 255, 255),  // white
            Color.FromArgb(0, 0, 0),        // black
            Color.FromArgb(128, 128, 128),  // mid grey
            Color.FromArgb(127, 200, 64),   // mixed
        };

        private static Bitmap PaletteRow()
        {
            var bmp = new Bitmap(Palette.Length, 1, PixelFormat.Format24bppRgb);
            for (int x = 0; x < Palette.Length; x++) bmp.SetPixel(x, 0, Palette[x]);
            return bmp;
        }

        private static int[] GrayRow(Bitmap b) =>
            Enumerable.Range(0, b.Width).Select(x => (int)b.GetPixel(x, 0).R).ToArray();

        private static Bitmap GrayPalette() => ImageProcessing.ConvertToGrayscale(PaletteRow());

        [Fact]
        public void Grayscale_UsesLumaWeightsTruncated_AndReturns8bppIndexed()
        {
            var gray = GrayPalette();

            // 0.2125*R + 0.7154*G + 0.0721*B, truncated toward zero.
            Assert.Equal(new[] { 54, 182, 18, 255, 0, 128, 174 }, GrayRow(gray));
            Assert.Equal(PixelFormat.Format8bppIndexed, gray.PixelFormat);
        }

        [Fact]
        public void Invert_IsComplementOfEachValue()
        {
            var inv = GrayPalette();
            ImageProcessing.SetInvert(ref inv);

            Assert.Equal(new[] { 201, 73, 237, 0, 255, 127, 81 }, GrayRow(inv));
        }

        [Theory]
        [InlineData(64, new[] { 0, 255, 0, 255, 0, 255, 255 })]
        [InlineData(128, new[] { 0, 255, 0, 255, 0, 255, 255 })]
        [InlineData(182, new[] { 0, 255, 0, 255, 0, 0, 0 })]   // value == threshold maps to white (>=)
        public void Threshold_IsInclusiveAtBoundary(int threshold, int[] expected)
        {
            var th = GrayPalette();
            ImageProcessing.SetThreshold(threshold, ref th);

            Assert.Equal(expected, GrayRow(th));
        }

        [Theory]
        [InlineData(30, new[] { 31, 198, 0, 255, 0, 128, 188 })]
        [InlineData(60, new[] { 0, 230, 0, 255, 0, 128, 215 })]
        [InlineData(80, new[] { 0, 255, 0, 255, 0, 128, 252 })]
        [InlineData(85, new[] { 0, 255, 0, 255, 0, 129, 255 })]
        [InlineData(100, new[] { 0, 255, 0, 255, 0, 129, 255 })]
        public void Contrast_IsLevelsLinearStretchAround128(int factor, int[] expected)
        {
            // Maps input [factor, 255-factor] -> [0, 255], truncated and clamped.
            var c = GrayPalette();
            ImageProcessing.SetContrast(factor, ref c);

            Assert.Equal(expected, GrayRow(c));
        }

        [Fact]
        public void AverageColor_IsArithmeticMeanPerChannel()
        {
            using (var bmp = new Bitmap(2, 2, PixelFormat.Format24bppRgb))
            {
                bmp.SetPixel(0, 0, Color.FromArgb(10, 20, 30));
                bmp.SetPixel(1, 0, Color.FromArgb(40, 50, 60));
                bmp.SetPixel(0, 1, Color.FromArgb(70, 80, 90));
                bmp.SetPixel(1, 1, Color.FromArgb(100, 110, 120));

                var avg = ImageProcessing.AverageColor(bmp);

                Assert.Equal(55.0, avg.R);   // (10+40+70+100)/4
                Assert.Equal(65.0, avg.G);   // (20+50+80+110)/4
                Assert.Equal(75.0, avg.B);   // (30+60+90+120)/4
            }
        }

        [Fact]
        public void FilterColors_KeepsPixelsInRange_FillsRestWithFillColor()
        {
            var bmp = PaletteRow();
            ImageProcessing.FilterColors(ref bmp,
                new IntRange(0, 150), new IntRange(0, 150), new IntRange(0, 150));

            var actual = Enumerable.Range(0, bmp.Width)
                .Select(x => bmp.GetPixel(x, 0))
                .Select(p => (p.R, p.G, p.B))
                .ToArray();

            var expected = new[]
            {
                ((byte)255, (byte)255, (byte)255), // red    -> out of range -> fill white
                ((byte)255, (byte)255, (byte)255), // green  -> fill
                ((byte)255, (byte)255, (byte)255), // blue   -> fill
                ((byte)255, (byte)255, (byte)255), // white  -> fill
                ((byte)0,   (byte)0,   (byte)0),   // black  -> all channels in range -> kept
                ((byte)128, (byte)128, (byte)128), // grey   -> kept
                ((byte)255, (byte)255, (byte)255), // mixed (G=200) -> fill
            };

            Assert.Equal(expected, actual);
        }
    }
}
