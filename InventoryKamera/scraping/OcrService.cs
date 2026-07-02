using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using Tesseract;

namespace InventoryKamera
{
    /// <inheritdoc cref="IOcrService"/>
    internal sealed class OcrService : IOcrService, IDisposable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly string tesseractDatapath;
        private readonly string tesseractLanguage;
        private readonly int engineCount;

        private BlockingCollection<TesseractEngine> engines;

        /// <param name="tesseractDatapath">Directory containing the trained data files.</param>
        /// <param name="tesseractLanguage">Trained data file name (without extension).</param>
        /// <param name="engineCount">
        /// Engine pool size. Defaults to roughly one engine per logical processor, clamped so tiny
        /// machines still get a usable pool and huge ones don't load an excessive number of models.
        /// </param>
        public OcrService(string tesseractDatapath = @".\tessdata", string tesseractLanguage = "genshin_fast_09_04_21", int? engineCount = null)
        {
            this.tesseractDatapath = tesseractDatapath;
            this.tesseractLanguage = tesseractLanguage;
            this.engineCount = engineCount ?? Math.Max(4, Math.Min(12, Environment.ProcessorCount));
        }

        public void Restart()
        {
            if (engines is null) engines = new BlockingCollection<TesseractEngine>();
            lock (engines)
            {
                while (engines.TryTake(out TesseractEngine e))
                {
                    e.Dispose();
                }

                try
                {
                    for (int i = 0; i < engineCount; i++)
                    {
                        engines.Add(new TesseractEngine(tesseractDatapath, tesseractLanguage, EngineMode.LstmOnly));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to initialize Tesseract engines.");
                    throw;
                }
            }
            Logger.Debug("{EngineCount} engines restarted", engineCount);
        }

        public string AnalyzeText(Bitmap bitmap, PageSegMode pageMode = PageSegMode.SingleLine, bool numbersOnly = false)
        {
            return AnalyzeTextWithConfidence(bitmap, pageMode, numbersOnly).Text;
        }

        public (string Text, float Confidence) AnalyzeTextWithConfidence(Bitmap bitmap, PageSegMode pageMode = PageSegMode.SingleLine, bool numbersOnly = false)
        {
            // Lazily start the pool if a caller reaches AnalyzeTextWithConfidence before Restart() --
            // normal production flow always calls Restart() once per scan first, but this keeps the
            // service safe (and directly usable in tests) even if that ordering isn't followed.
            if (engines is null) Restart();

            string text = "";
            float confidence;
            // Blocks efficiently until an engine is free, instead of busy-polling with Thread.Sleep.
            TesseractEngine e = engines.Take();

            if (numbersOnly) e.SetVariable("tessedit_char_whitelist", "0123456789");
            using (var pix = BitmapToPix(bitmap))
            using (var page = e.Process(pix, pageMode))
            {
                using (var iter = page.GetIterator())
                {
                    iter.Begin();
                    do
                    {
                        text += iter.GetText(PageIteratorLevel.TextLine);
                    }
                    while (iter.Next(PageIteratorLevel.TextLine));
                }
                confidence = page.GetMeanConfidence();
            }
            engines.Add(e);

            return (text, confidence);
        }

        /// <summary>
        /// Convert a <see cref="Bitmap"/> to a Tesseract <see cref="Pix"/> for OCR. The Tesseract
        /// package's netstandard2.0 target (used on modern .NET) has no direct Bitmap overload of
        /// <c>TesseractEngine.Process</c> (that only exists in its net47/net48 targets), so the image
        /// is round-tripped through an in-memory PNG — lossless, so pixel values are unchanged.
        /// </summary>
        private static Pix BitmapToPix(Bitmap bitmap)
        {
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                return Pix.LoadFromMemory(stream.ToArray());
            }
        }

        public void Dispose()
        {
            if (engines is null) return;
            while (engines.TryTake(out var e))
            {
                e.Dispose();
            }
            engines.Dispose();
        }
    }
}
