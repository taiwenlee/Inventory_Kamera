namespace InventoryKamera
{
    /// <summary>
    /// Default <see cref="IScanSettings"/> implementation; forwards live to
    /// <see cref="Properties.Settings.Default"/>. Deliberately a live pass-through rather than a
    /// frozen snapshot: <c>Properties.Settings.Default</c> is two-way data-bound to MainForm/MainUI's
    /// controls (see <c>MainForm.Designer.cs</c>/<c>MainUI.Designer.cs</c>), so a user can toggle a
    /// checkbox mid-session and have it apply to the very next scan without restarting the app or
    /// re-saving to disk -- a snapshot taken once (e.g. at scraper-construction time) would silently
    /// go stale for exactly that case. This class exists so scan logic depends on a small interface
    /// instead of reaching into a concrete WinForms settings type; it does not change the underlying
    /// persistence or live-update behavior at all.
    /// </summary>
    internal sealed class ScanSettings : IScanSettings
    {
        public bool ScanWeapons => Properties.Settings.Default.ScanWeapons;
        public bool ScanArtifacts => Properties.Settings.Default.ScanArtifacts;
        public bool ScanCharacters => Properties.Settings.Default.ScanCharacters;
        public bool ScanCharDevItems => Properties.Settings.Default.ScanCharDevItems;
        public bool ScanMaterials => Properties.Settings.Default.ScanMaterials;
        public int ScannerDelay => Properties.Settings.Default.ScannerDelay;
        public decimal MinimumWeaponRarity => Properties.Settings.Default.MinimumWeaponRarity;
        public decimal MinimumArtifactRarity => Properties.Settings.Default.MinimumArtifactRarity;
        public decimal MinimumWeaponLevel => Properties.Settings.Default.MinimumWeaponLevel;
        public decimal MinimumArtifactLevel => Properties.Settings.Default.MinimumArtifactLevel;
        public bool LogScreenshots => Properties.Settings.Default.LogScreenshots;
        public string TravelerName => Properties.Settings.Default.TravelerName;
        public string WandererName => Properties.Settings.Default.WandererName;
        public int SortByObtained => Properties.Settings.Default.SortByObtained;
        public int NumOfCharToScan => Properties.Settings.Default.NumOfCharToScan;
        public string Manequin1Name => Properties.Settings.Default.Manequin1Name;
        public string Manequin2Name => Properties.Settings.Default.Manequin2Name;
    }
}
