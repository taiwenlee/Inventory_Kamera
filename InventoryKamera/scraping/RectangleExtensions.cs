using System.Drawing;

namespace InventoryKamera
{
    /// <summary>
    /// Replaces <c>Accord.Imaging.ExtensionMethods.Center(Rectangle)</c>, the last piece of the
    /// Accord.Imaging surface still used after the image filters were reimplemented in
    /// <see cref="ImageProcessing"/>.
    /// </summary>
    internal static class RectangleExtensions
    {
        public static Point Center(this Rectangle rect) =>
            new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }
}
