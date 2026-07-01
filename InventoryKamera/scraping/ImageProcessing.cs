using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Mean value of each colour channel over every pixel, matching Accord's
        /// <c>ImageStatistics(bitmap).Red/Green/Blue.Mean</c>. Colour images only.
        /// </summary>
        internal static unsafe (double R, double G, double B) AverageColor(Bitmap bitmap)
        {
            int bpp = BytesPerPixel(bitmap.PixelFormat);
            if (bpp < 3)
                throw new NotSupportedException($"AverageColor expects a colour image, got {bitmap.PixelFormat}.");

            int w = bitmap.Width, h = bitmap.Height;
            double sumR = 0, sumG = 0, sumB = 0;

            var data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, bitmap.PixelFormat);
            try
            {
                byte* baseP = (byte*)data.Scan0;
                for (int y = 0; y < h; y++)
                {
                    byte* row = baseP + y * data.Stride;
                    for (int x = 0; x < w; x++)
                    {
                        byte* p = row + x * bpp;
                        sumB += p[0];
                        sumG += p[1];
                        sumR += p[2];
                    }
                }
            }
            finally { bitmap.UnlockBits(data); }

            long count = (long)w * h;
            return (sumR / count, sumG / count, sumB / count);
        }

        // The 8 Kirsch compass kernels, in raster tap order:
        // tap 0..8 = offsets (-1,-1)(0,-1)(1,-1)(-1,0)(0,0)(1,0)(-1,1)(0,1)(1,1).
        private static readonly int[][] KirschKernels =
        {
            new[] {  5,  5,  5, -3,  0, -3, -3, -3, -3 }, // N
            new[] {  5,  5, -3,  5,  0, -3, -3, -3, -3 }, // NW
            new[] {  5, -3, -3,  5,  0, -3,  5, -3, -3 }, // W
            new[] { -3, -3, -3,  5,  0, -3,  5,  5, -3 }, // SW
            new[] { -3, -3, -3, -3,  0, -3,  5,  5,  5 }, // S
            new[] { -3, -3, -3, -3,  0,  5, -3,  5,  5 }, // SE
            new[] { -3, -3,  5, -3,  0,  5, -3, -3,  5 }, // E
            new[] { -3,  5,  5, -3,  0,  5, -3, -3, -3 }, // NE
        };

        /// <summary>
        /// Kirsch compass edge detector, matching Accord's <c>KirschEdgeDetector</c>: for each pixel
        /// and colour channel, the maximum of the 8 compass-kernel convolutions, clamped to [0,255].
        /// Out-of-bounds neighbours are treated as 0. 24/32bpp colour in, 24bpp out (per channel).
        /// </summary>
        internal static unsafe Bitmap EdgeDetectKirsch(Bitmap bitmap)
        {
            int bpp = BytesPerPixel(bitmap.PixelFormat);
            if (bpp < 3)
                throw new NotSupportedException($"Edge detection expects a colour image, got {bitmap.PixelFormat}.");

            int w = bitmap.Width, h = bitmap.Height;
            var result = new Bitmap(w, h, PixelFormat.Format24bppRgb);

            var src = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, bitmap.PixelFormat);
            var dst = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                byte* sBase = (byte*)src.Scan0, dBase = (byte*)dst.Scan0;
                for (int y = 0; y < h; y++)
                {
                    byte* drow = dBase + y * dst.Stride;
                    for (int x = 0; x < w; x++)
                    {
                        for (int c = 0; c < 3; c++)
                        {
                            // Gather the 3x3 neighbourhood for this channel (OOB = 0).
                            int t0 = Sample(sBase, src.Stride, bpp, w, h, x - 1, y - 1, c);
                            int t1 = Sample(sBase, src.Stride, bpp, w, h, x,     y - 1, c);
                            int t2 = Sample(sBase, src.Stride, bpp, w, h, x + 1, y - 1, c);
                            int t3 = Sample(sBase, src.Stride, bpp, w, h, x - 1, y,     c);
                            int t4 = Sample(sBase, src.Stride, bpp, w, h, x,     y,     c);
                            int t5 = Sample(sBase, src.Stride, bpp, w, h, x + 1, y,     c);
                            int t6 = Sample(sBase, src.Stride, bpp, w, h, x - 1, y + 1, c);
                            int t7 = Sample(sBase, src.Stride, bpp, w, h, x,     y + 1, c);
                            int t8 = Sample(sBase, src.Stride, bpp, w, h, x + 1, y + 1, c);

                            int max = 0;
                            foreach (var k in KirschKernels)
                            {
                                int r = k[0] * t0 + k[1] * t1 + k[2] * t2
                                      + k[3] * t3 + k[4] * t4 + k[5] * t5
                                      + k[6] * t6 + k[7] * t7 + k[8] * t8;
                                if (r > max) max = r;
                            }
                            drow[x * 3 + c] = (byte)(max > 255 ? 255 : max);
                        }
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(src);
                result.UnlockBits(dst);
            }
            return result;
        }

        private static unsafe int Sample(byte* baseP, int stride, int bpp, int w, int h, int x, int y, int channel)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return 0;
            return baseP[y * stride + x * bpp + channel];
        }

        /// <summary>
        /// Find the bounding rectangles of 8-connected foreground (non-zero) blobs in an 8bpp image,
        /// keeping only those whose width and height fall within the given inclusive ranges. Matches
        /// Accord's <c>BlobCounter { FilterBlobs = true, Min/MaxWidth, Min/MaxHeight }</c> +
        /// <c>GetObjectsRectangles()</c>, returned in raster-scan discovery order.
        /// </summary>
        internal static unsafe List<Rectangle> FindBlobRectangles(Bitmap binary, int minWidth, int maxWidth, int minHeight, int maxHeight)
        {
            if (binary.PixelFormat != PixelFormat.Format8bppIndexed)
                throw new NotSupportedException($"Blob detection expects 8bpp, got {binary.PixelFormat}.");

            int w = binary.Width, h = binary.Height;
            var foreground = new bool[w * h];

            var data = binary.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, binary.PixelFormat);
            try
            {
                byte* baseP = (byte*)data.Scan0;
                for (int y = 0; y < h; y++)
                {
                    byte* row = baseP + y * data.Stride;
                    for (int x = 0; x < w; x++)
                        if (row[x] > 0) foreground[y * w + x] = true;
                }
            }
            finally { binary.UnlockBits(data); }

            var rectangles = new List<Rectangle>();
            var visited = new bool[w * h];
            var stack = new Stack<int>();

            // Raster-scan discovery order matches Accord's labelling order.
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int start = y * w + x;
                    if (!foreground[start] || visited[start]) continue;

                    int minX = x, maxX = x, minY = y, maxY = y;
                    visited[start] = true;
                    stack.Push(start);

                    while (stack.Count > 0)
                    {
                        int idx = stack.Pop();
                        int px = idx % w, py = idx / w;
                        if (px < minX) minX = px;
                        if (px > maxX) maxX = px;
                        if (py < minY) minY = py;
                        if (py > maxY) maxY = py;

                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int ny = py + dy;
                            if (ny < 0 || ny >= h) continue;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = px + dx;
                                if (nx < 0 || nx >= w) continue;
                                int nIdx = ny * w + nx;
                                if (foreground[nIdx] && !visited[nIdx])
                                {
                                    visited[nIdx] = true;
                                    stack.Push(nIdx);
                                }
                            }
                        }
                    }

                    int bw = maxX - minX + 1, bh = maxY - minY + 1;
                    if (bw >= minWidth && bw <= maxWidth && bh >= minHeight && bh <= maxHeight)
                        rectangles.Add(new Rectangle(minX, minY, bw, bh));
                }
            }
            return rectangles;
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
