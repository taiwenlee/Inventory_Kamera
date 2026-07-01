using Accord;
using Accord.Imaging;
using Accord.Imaging.Filters;
using System.Drawing;

namespace InventoryKamera
{
    /// <summary>
    /// Image pre-processing operations used to prepare screenshots for OCR and blob detection.
    /// Extracted from <see cref="GenshinProcesor"/> so they can be exercised in isolation (the
    /// scraper's static constructor spins up Tesseract engines and loads lookup tables, which the
    /// image filters do not need) and so the Accord.Imaging backend can be swapped out behind a
    /// stable surface. Behaviour is pinned by ImagePreprocessingParityTests.
    /// </summary>
    internal static class ImageProcessing
    {
        internal static Bitmap ConvertToGrayscale(Bitmap bitmap)
        {
            return new Grayscale(0.2125, 0.7154, 0.0721).Apply(bitmap);
        }

        internal static void SetContrast(double contrast, ref Bitmap bitmap)
        {
            new ContrastCorrection((int)contrast).ApplyInPlace(bitmap);
        }

        internal static void SetInvert(ref Bitmap bitmap)
        {
            new Invert().ApplyInPlace(bitmap);
        }

        internal static void SetThreshold(int threshold, ref Bitmap bitmap)
        {
            new Threshold(threshold).ApplyInPlace(bitmap);
        }

        internal static void FilterColors(ref Bitmap bm, IntRange red, IntRange green, IntRange blue)
        {
            ColorFiltering colorFilter = new ColorFiltering
            {
                Red = red,
                Green = green,
                Blue = blue,
                FillColor = new RGB(255, 255, 255)
            };
            colorFilter.ApplyInPlace(bm);
        }
    }
}
