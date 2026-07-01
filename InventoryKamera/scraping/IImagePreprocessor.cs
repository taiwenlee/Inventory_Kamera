using System.Collections.Generic;
using System.Drawing;

namespace InventoryKamera
{
    /// <summary>
    /// Instance-method seam over <see cref="ImageProcessing"/> (Phase 2 §2.1), so scrapers can take
    /// preprocessing as a constructor dependency instead of calling the static class directly, once
    /// §2.2's DI wiring lands.
    /// </summary>
    internal interface IImagePreprocessor
    {
        Bitmap ConvertToGrayscale(Bitmap bitmap);
        void SetInvert(ref Bitmap bitmap);
        void SetThreshold(int threshold, ref Bitmap bitmap);
        void SetContrast(double contrast, ref Bitmap bitmap);
        void FilterColors(ref Bitmap bitmap, IntRange red, IntRange green, IntRange blue);
        (double R, double G, double B) AverageColor(Bitmap bitmap);
        Bitmap EdgeDetectKirsch(Bitmap bitmap);
        List<Rectangle> FindBlobRectangles(Bitmap binary, int minWidth, int maxWidth, int minHeight, int maxHeight);
    }
}
