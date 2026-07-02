using System.Drawing;

namespace InventoryKamera
{
    /// <summary>
    /// The scan-progress-reporting surface scan logic calls into during a scan, behind an injectable
    /// seam instead of the concrete static <see cref="UserInterface"/> WinForms class (Phase 2 §2.5).
    /// <see cref="ScanViewModel"/> is the only implementation: most methods still delegate straight to
    /// <see cref="UserInterface"/>'s direct <c>Control.Invoke</c> writes, but it owns real observable
    /// state for the counter group as the first slice of the larger MVVM redesign -- see the plan
    /// doc's §2.5 sequencing note for why the rest is being carved out incrementally instead of all at
    /// once.
    /// </summary>
    internal interface IScanProgressReporter
    {
        void SetGear(Bitmap bm, Weapon weapon);
        void SetGear(Bitmap bm, Artifact artifact);
        void SetGearPictureBox(Bitmap bm);
        void SetGearTextBox(string text);
        void SetMainCharacterName(string text);
        void SetCharacter_NameAndElement(Bitmap bm, string name, string element);
        void SetCharacter_Level(Bitmap bm, int level, int maxLevel);
        void SetCharacter_Constellation(int level);
        void SetMaterial(Bitmap nameplate, Bitmap quantity, string name, int count);
        void SetMora(Bitmap mora, int count);
        void SetCharacter_Talent(Bitmap bm, string text, int i);
        void SetWeapon_Max(int value);
        void SetArtifact_Max(int value);
        void SetCharacter_Max(int value);
        void IncrementArtifactCount();
        void IncrementWeaponCount();
        void IncrementCharacterCount();
        void SetProgramStatus(string status, bool ok = true);
        void AddError(string error);
        void SetNavigation_Image(Bitmap bm);
        void ResetCharacterDisplay();
        void ResetGearDisplay();
        void ResetCounters();
        void ResetErrors();
        void ResetAll();

        /// <summary>
        /// Surfaces a low-confidence OCR result for inline user correction (Phase 3 §3.3). Blocks the
        /// calling (scan) thread until the user resolves it; returns <paramref name="recognizedText"/>
        /// unchanged if nothing is subscribed (e.g. running headless/under test) or the user declines
        /// to correct it, so callers can always trust the return value the same way they'd trust a
        /// direct OCR result.
        /// </summary>
        string RequestCorrection(Bitmap image, string recognizedText, float confidencePercent, string fieldLabel);

        /// <summary>
        /// Blocks the calling thread while any inline correction requested via
        /// <see cref="RequestCorrection"/> is awaiting user input. Scan loops that queue capture work
        /// onto background workers instead of processing it inline (<c>ArtifactScraper</c>/
        /// <c>WeaponScraper</c>'s <c>QueueScan</c>) must call this between items -- otherwise a
        /// worker blocking inside <see cref="RequestCorrection"/> has no effect on the loop that's
        /// still clicking/scrolling the game.
        /// </summary>
        void WaitIfCorrectionPending();
    }
}
