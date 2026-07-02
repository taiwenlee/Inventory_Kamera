using System.Drawing;

namespace InventoryKamera
{
    /// <summary>
    /// Carries a low-confidence OCR result out to the UI for inline correction (Phase 3 §3.3), and
    /// carries the user's answer back. <see cref="ScanViewModel.RequestCorrection"/> raises
    /// <see cref="ScanViewModel.CorrectionRequested"/> with one of these and blocks synchronously on
    /// the subscriber's <c>Control.Invoke</c> call returning -- there's no separate wait handle here
    /// because a modal dialog shown inside that <c>Invoke</c> already blocks the calling (scan)
    /// thread for free, the same idiom every other <see cref="ScanViewModel"/> event already uses.
    /// </summary>
    internal sealed class OcrCorrectionEventArgs
    {
        /// <summary>Owned by this instance; the raiser (<see cref="ScanViewModel"/>) disposes it after the event returns.</summary>
        public Bitmap Image { get; }
        public string RecognizedText { get; }

        /// <summary>Tesseract's mean confidence for this recognition, as a 0-100 percentage.</summary>
        public float ConfidencePercent { get; }

        /// <summary>Human-readable description of what's being recognized, e.g. "Artifacts item count".</summary>
        public string FieldLabel { get; }

        /// <summary>
        /// Set by the subscriber's dialog before <c>Invoke</c> returns. Null (unset) means "use
        /// <see cref="RecognizedText"/> as-is" -- the same outcome as if no correction UI existed at
        /// all, so cancelling the dialog degrades gracefully to today's behavior.
        /// </summary>
        public string ResolvedText { get; set; }

        public OcrCorrectionEventArgs(Bitmap image, string recognizedText, float confidencePercent, string fieldLabel)
        {
            Image = image;
            RecognizedText = recognizedText;
            ConfidencePercent = confidencePercent;
            FieldLabel = fieldLabel;
        }
    }
}
