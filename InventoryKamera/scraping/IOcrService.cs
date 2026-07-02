using System.Drawing;
using Tesseract;

namespace InventoryKamera
{
    /// <summary>
    /// OCR text recognition, backed by a pool of Tesseract engines. Extracted from
    /// <see cref="GenshinProcesor"/> (Phase 2 §2.1 — decompose the static god-class into injectable
    /// services), the same pattern used for <see cref="ImageProcessing"/> in Phase 1. Unlike the
    /// static class it came from, this has no static-constructor side effects: constructing an
    /// instance does no disk I/O, so it can actually be unit-tested — previously impossible, since
    /// touching any static member of <c>GenshinProcesor</c> triggered eager Tesseract engine loading.
    /// </summary>
    internal interface IOcrService
    {
        /// <summary>
        /// (Re)creates the engine pool, disposing any existing engines first. Called once per scan
        /// so each scan starts with a fresh pool.
        /// </summary>
        void Restart();

        /// <summary>Recognizes text in a captured region.</summary>
        string AnalyzeText(Bitmap bitmap, PageSegMode pageMode = PageSegMode.SingleLine, bool numbersOnly = false);

        /// <summary>
        /// Recognizes text in a captured region, also returning Tesseract's own mean confidence
        /// (0.0-1.0, page-level average across recognized text lines) for that recognition. Used by
        /// callers that need to decide whether a result is trustworthy enough to use automatically or
        /// should be surfaced for inline user correction (Phase 3 §3.3) — most callers that just need
        /// the text should keep using <see cref="AnalyzeText"/>.
        /// </summary>
        (string Text, float Confidence) AnalyzeTextWithConfidence(Bitmap bitmap, PageSegMode pageMode = PageSegMode.SingleLine, bool numbersOnly = false);
    }
}
