namespace InventoryKamera
{
    /// <summary>
    /// The subset of <see cref="Properties.Settings"/> that scan logic (scrapers + the
    /// <see cref="InventoryKamera"/> orchestrator) reads, behind an injectable seam instead of the
    /// concrete WinForms <c>Properties.Settings.Default</c> static (Phase 2 §2.3).
    /// </summary>
    internal interface IScanSettings
    {
        bool ScanWeapons { get; }
        bool ScanArtifacts { get; }
        bool ScanCharacters { get; }
        bool ScanCharDevItems { get; }
        bool ScanMaterials { get; }
        int ScannerDelay { get; }
        decimal MinimumWeaponRarity { get; }
        decimal MinimumArtifactRarity { get; }
        decimal MinimumWeaponLevel { get; }
        decimal MinimumArtifactLevel { get; }
        bool LogScreenshots { get; }

        /// <summary>
        /// Tesseract mean-confidence percentage (0-100) below which a recognized item should be
        /// surfaced for inline user correction instead of used automatically (Phase 3 §3.3).
        /// </summary>
        int OcrConfidenceThreshold { get; }
        string TravelerName { get; }
        string WandererName { get; }
        int SortByObtained { get; }
        int NumOfCharToScan { get; }
        string Manequin1Name { get; }
        string Manequin2Name { get; }
    }
}
