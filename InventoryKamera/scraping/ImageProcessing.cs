using Accord;
using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace InventoryKamera
{
    /// <summary>
    /// Image pre-processing operations used to prepare screenshots for OCR and blob detection.
    /// Extracted from <see cref="GenshinProcesor"/> so they can be exercised in isolation (the
    /// scraper's static constructor spins up Tesseract engines and loads lookup tables, which the
    /// image filters do not need).
    ///
    /// These are pure System.Drawing (GDI+) reimplementations of the Accord.Imaging filters the
    /// project previously depended on; Accord was built against the abandoned CoreCompat.System.Drawing
    /// fork and blocks the move to .NET 8. Every operation reproduces Accord's exact pixel output,
    /// pinned by ImagePreprocessingParityTests. See [[net8-blocked-by-accord]].
    /// </summary>
    internal static class ImageProcessing
    {
        // Rec. 709 luma weights, matching the coefficients Accord's Grayscale filter was constructed with.
        private const double LumaR = 0.2125, LumaG = 0.7154, LumaB = 0.0721;

        /// <summary>
        /// Convert a 24/32bpp image to an 8bpp indexed grayscale bitmap, matching
        /// Accord's <c>Grayscale(0.2125, 0.7154, 0.0721)</c> (weights applied then truncated).
        /// </summary>
        internal static unsafe Bitmap ConvertToGrayscale(Bitmap bitmap)
        {
            int bpp = BytesPerPixel(bitmap.PixelFormat);
            if (bpp < 3)
                throw new NotSupportedException($"Grayscale expects a colour image, got {bitmap.PixelFormat}.");

            int w = bitmap.Width, h = bitmap.Height;
            var gray = new Bitmap(w, h, PixelFormat.Format8bppIndexed);
            SetGrayscalePalette(gray);

            var src = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, bitmap.PixelFormat);
            var dst = gray.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
            try
            {
                byte* srcBase = (byte*)src.Scan0, dstBase = (byte*)dst.Scan0;
                for (int y = 0; y < h; y++)
                {
                    byte* srow = srcBase + y * src.Stride;
                    byte* drow = dstBase + y * dst.Stride;
                    for (int x = 0; x < w; x++)
                    {
                        byte* p = srow + x * bpp;           // little-endian BGRA order
                        drow[x] = (byte)(LumaR * p[2] + LumaG * p[1] + LumaB * p[0]);
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(src);
                gray.UnlockBits(dst);
            }
            return gray;
        }

        /// <summary>Invert every intensity/colour channel (255 - value); alpha is left untouched.</summary>
        internal static unsafe void SetInvert(ref Bitmap bitmap)
        {
            int bpp = BytesPerPixel(bitmap.PixelFormat);
            int channels = Math.Min(bpp, 3); // don't invert the alpha byte of 32bpp
            int w = bitmap.Width, h = bitmap.Height;

            var data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, bitmap.PixelFormat);
            try
            {
                byte* baseP = (byte*)data.Scan0;
                for (int y = 0; y < h; y++)
                {
                    byte* row = baseP + y * data.Stride;
                    for (int x = 0; x < w; x++)
                    {
                        byte* p = row + x * bpp;
                        for (int c = 0; c < channels; c++) p[c] = (byte)(255 - p[c]);
                    }
                }
            }
            finally { bitmap.UnlockBits(data); }
        }

        /// <summary>
        /// Binarize an 8bpp grayscale image: value &gt;= threshold becomes 255, otherwise 0
        /// (inclusive at the boundary, matching Accord's Threshold).
        /// </summary>
        internal static unsafe void SetThreshold(int threshold, ref Bitmap bitmap)
        {
            if (bitmap.PixelFormat != PixelFormat.Format8bppIndexed)
                throw new NotSupportedException($"Threshold expects 8bpp grayscale, got {bitmap.PixelFormat}.");

            int w = bitmap.Width, h = bitmap.Height;
            var data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, bitmap.PixelFormat);
            try
            {
                byte* baseP = (byte*)data.Scan0;
                for (int y = 0; y < h; y++)
                {
                    byte* row = baseP + y * data.Stride;
                    for (int x = 0; x < w; x++) row[x] = row[x] >= threshold ? (byte)255 : (byte)0;
                }
            }
            finally { bitmap.UnlockBits(data); }
        }

        /// <summary>
        /// Linear contrast stretch matching Accord's <c>ContrastCorrection(factor)</c>: input range
        /// [factor, 255-factor] is mapped onto [0, 255], truncated and clamped. Works on 8bpp
        /// grayscale and 24/32bpp colour (per channel).
        /// </summary>
        internal static unsafe void SetContrast(double contrast, ref Bitmap bitmap)
        {
            int factor = (int)contrast;
            byte[] lut = BuildContrastLut(factor);

            int bpp = BytesPerPixel(bitmap.PixelFormat);
            int channels = Math.Min(bpp, 3);
            int w = bitmap.Width, h = bitmap.Height;

            var data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, bitmap.PixelFormat);
            try
            {
                byte* baseP = (byte*)data.Scan0;
                for (int y = 0; y < h; y++)
                {
                    byte* row = baseP + y * data.Stride;
                    if (bpp == 1)
                    {
                        for (int x = 0; x < w; x++) row[x] = lut[row[x]];
                    }
                    else
                    {
                        for (int x = 0; x < w; x++)
                        {
                            byte* p = row + x * bpp;
                            for (int c = 0; c < channels; c++) p[c] = lut[p[c]];
                        }
                    }
                }
            }
            finally { bitmap.UnlockBits(data); }
        }

        /// <summary>
        /// Keep pixels whose R/G/B all fall inside the given inclusive ranges; replace the rest with
        /// white. Matches Accord's <c>ColorFiltering</c> with a white FillColor. Colour images only.
        /// </summary>
        internal static unsafe void FilterColors(ref Bitmap bm, IntRange red, IntRange green, IntRange blue)
        {
            int bpp = BytesPerPixel(bm.PixelFormat);
            if (bpp < 3)
                throw new NotSupportedException($"Colour filtering expects a colour image, got {bm.PixelFormat}.");

            int w = bm.Width, h = bm.Height;
            var data = bm.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, bm.PixelFormat);
            try
            {
                byte* baseP = (byte*)data.Scan0;
                for (int y = 0; y < h; y++)
                {
                    byte* row = baseP + y * data.Stride;
                    for (int x = 0; x < w; x++)
                    {
                        byte* p = row + x * bpp;
                        byte b = p[0], g = p[1], r = p[2];
                        bool keep = r >= red.Min && r <= red.Max
                                 && g >= green.Min && g <= green.Max
                                 && b >= blue.Min && b <= blue.Max;
                        if (!keep) { p[0] = 255; p[1] = 255; p[2] = 255; } // fill white
                    }
                }
            }
            finally { bm.UnlockBits(data); }
        }

        private static byte[] BuildContrastLut(int factor)
        {
            int lo = factor, hi = 255 - factor;
            var lut = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                int v = hi > lo ? (int)((i - lo) * 255.0 / (hi - lo)) : (i >= lo ? 255 : 0);
                lut[i] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
            }
            return lut;
        }

        private static void SetGrayscalePalette(Bitmap indexed)
        {
            var palette = indexed.Palette;
            for (int i = 0; i < 256; i++) palette.Entries[i] = Color.FromArgb(i, i, i);
            indexed.Palette = palette; // assignment is required for the change to take effect
        }

        private static int BytesPerPixel(PixelFormat format) =>
            System.Drawing.Image.GetPixelFormatSize(format) / 8;
    }
}
