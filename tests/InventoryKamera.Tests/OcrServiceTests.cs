using System.Drawing;
using System.Drawing.Imaging;
using InventoryKamera;
using Tesseract;
using Xunit;
using Xunit.Abstractions;

namespace InventoryKamera.Tests
{
    /// <summary>
    /// Real Tesseract OCR round-trip tests: render known text to a bitmap and verify AnalyzeText
    /// reads it back. Previously impossible -- touching any static member of GenshinProcesor (the
    /// class OCR used to live in) eagerly loaded the Tesseract engine pool from disk as a static
    /// constructor side effect, and there was no way to construct an isolated instance to test
    /// against. IOcrService/OcrService (Phase 2 §2.1) has an explicit, no-I/O constructor instead.
    ///
    /// The trained data (genshin_fast_09_04_21) is a custom model trained specifically on Genshin
    /// Impact's in-game font, not general-purpose English text, so recognition quality against
    /// generically-rendered text isn't guaranteed the way it would be for a general OCR engine --
    /// these tests use large, high-contrast, simple content (digits) to stay reliable.
    /// </summary>
    public class OcrServiceTests
    {
        private readonly ITestOutputHelper output;

        public OcrServiceTests(ITestOutputHelper output) => this.output = output;

        private static Bitmap RenderText(string text, int width = 300, int height = 100, float fontSize = 48)
        {
            var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font(FontFamily.GenericSansSerif, fontSize, FontStyle.Bold))
            {
                g.Clear(Color.White);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString(text, font, Brushes.Black, new PointF(10, 10));
            }
            return bmp;
        }

        [Fact]
        public void AnalyzeText_RecognizesRenderedDigits()
        {
            using var ocr = new OcrService();
            using var bitmap = RenderText("1234");

            string result = ocr.AnalyzeText(bitmap, PageSegMode.SingleLine, numbersOnly: true).Trim();
            output.WriteLine($"Recognized: '{result}'");

            Assert.Equal("1234", result);
        }

        [Fact]
        public void AnalyzeText_NumbersOnly_IgnoresNonDigitCharacters()
        {
            using var ocr = new OcrService();
            using var bitmap = RenderText("Lv.90");

            string result = ocr.AnalyzeText(bitmap, PageSegMode.SingleLine, numbersOnly: true).Trim();
            output.WriteLine($"Recognized: '{result}'");

            // With the digit whitelist applied, only "90" should come through, regardless of how
            // (or whether) "Lv." gets recognized -- this exercises numbersOnly specifically, which
            // several scrapers rely on for level/count/quantity fields.
            Assert.Contains("90", result);
        }

        [Fact]
        public void Restart_ProducesAWorkingEnginePool()
        {
            using var ocr = new OcrService();
            ocr.Restart();
            using var bitmap = RenderText("42");

            string result = ocr.AnalyzeText(bitmap, PageSegMode.SingleLine, numbersOnly: true).Trim();
            output.WriteLine($"Recognized after explicit Restart(): '{result}'");

            Assert.Equal("42", result);
        }
    }
}
