using System.Collections.Generic;
using System.Drawing;

namespace InventoryKamera
{
    /// <summary>
    /// Default <see cref="IImagePreprocessor"/> implementation; delegates straight to the pure static
    /// <see cref="ImageProcessing"/> functions, which stay put since <c>ImagePreprocessingParityTests</c>
    /// and <c>KirschBlobParityTests</c> already pin them to Accord's exact pixel output.
    /// </summary>
    internal sealed class ImageProcessor : IImagePreprocessor
    {
        public Bitmap ConvertToGrayscale(Bitmap bitmap) => ImageProcessing.ConvertToGrayscale(bitmap);

        public void SetInvert(ref Bitmap bitmap) => ImageProcessing.SetInvert(ref bitmap);

        public void SetThreshold(int threshold, ref Bitmap bitmap) => ImageProcessing.SetThreshold(threshold, ref bitmap);

        public void SetContrast(double contrast, ref Bitmap bitmap) => ImageProcessing.SetContrast(contrast, ref bitmap);

        public void FilterColors(ref Bitmap bitmap, IntRange red, IntRange green, IntRange blue) =>
            ImageProcessing.FilterColors(ref bitmap, red, green, blue);

        public (double R, double G, double B) AverageColor(Bitmap bitmap) => ImageProcessing.AverageColor(bitmap);

        public Bitmap EdgeDetectKirsch(Bitmap bitmap) => ImageProcessing.EdgeDetectKirsch(bitmap);

        public List<Rectangle> FindBlobRectangles(Bitmap binary, int minWidth, int maxWidth, int minHeight, int maxHeight) =>
            ImageProcessing.FindBlobRectangles(binary, minWidth, maxWidth, minHeight, maxHeight);
    }
}
