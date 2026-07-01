using System.Drawing;
using System.Drawing.Imaging;
using Xunit;

namespace InventoryKamera.Tests
{
    /// <summary>
    /// ImageProcessor is a thin instance-method seam over the static ImageProcessing functions
    /// (Phase 2 §2.1) -- these confirm delegation is wired correctly. Pixel-level correctness is
    /// already pinned by ImagePreprocessingParityTests/KirschBlobParityTests against the static class.
    /// </summary>
    public class ImageProcessorTests
    {
        private static Bitmap MakeSolidColor(int width, int height, Color color)
        {
            var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp)) g.Clear(color);
            return bmp;
        }

        [Fact]
        public void AverageColor_MatchesStaticImplementation()
        {
            IImagePreprocessor processor = new ImageProcessor();
            using var bitmap = MakeSolidColor(4, 4, Color.FromArgb(10, 20, 30));

            var expected = ImageProcessing.AverageColor(bitmap);
            var actual = processor.AverageColor(bitmap);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ConvertToGrayscale_MatchesStaticImplementation()
        {
            IImagePreprocessor processor = new ImageProcessor();
            using var bitmap = MakeSolidColor(4, 4, Color.FromArgb(10, 20, 30));

            using var expected = ImageProcessing.ConvertToGrayscale(bitmap);
            using var actual = processor.ConvertToGrayscale(bitmap);

            Assert.Equal(expected.GetPixel(0, 0), actual.GetPixel(0, 0));
        }

        [Fact]
        public void SetThreshold_MatchesStaticImplementation()
        {
            IImagePreprocessor processor = new ImageProcessor();
            using var expected = ImageProcessing.ConvertToGrayscale(MakeSolidColor(4, 4, Color.FromArgb(200, 200, 200)));
            using var actual = ImageProcessing.ConvertToGrayscale(MakeSolidColor(4, 4, Color.FromArgb(200, 200, 200)));

            var expectedBmp = expected;
            var actualBmp = actual;
            ImageProcessing.SetThreshold(100, ref expectedBmp);
            processor.SetThreshold(100, ref actualBmp);

            Assert.Equal(expectedBmp.GetPixel(0, 0), actualBmp.GetPixel(0, 0));
        }
    }
}
