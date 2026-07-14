using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace InventoryKamera
{
    /// <summary>
    /// MVVM redesign for §2.5, carved out one control group at a time: owns genuine observable state
    /// for the counters (weapon/artifact/character scanned/max), status/errors, gear
    /// (weapon/artifact picture + text), material/mora display, and the generic navigation-image
    /// preview instead of delegating straight to <see cref="UserInterface"/>. <see cref="MainForm"/>
    /// owns one long-lived instance, subscribes to <see cref="CountersChanged"/>/
    /// <see cref="ProgramStatusChanged"/>/<see cref="ErrorAdded"/>/<see cref="ErrorsReset"/>/
    /// <see cref="GearChanged"/>/<see cref="MaterialChanged"/>/<see cref="MoraChanged"/>/
    /// <see cref="NavigationImageChanged"/> once at startup, and renders those controls itself --
    /// instead of a shared static facade owning them. Character display is the only group still
    /// bridging to <see cref="UserInterface"/> (deliberately deferred pending the user's planned
    /// character-scanning revamp) -- see the plan doc's §2.5 sequencing note.
    /// </summary>
    internal sealed class ScanViewModel : IScanProgressReporter
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private int weaponCount;
        private int? weaponMax;
        private int artifactCount;
        private int? artifactMax;
        private int characterCount;
        private int? characterMax;
        private int materialCount;
        private readonly Stopwatch scanStopwatch = new Stopwatch();

        private string programStatus = "";
        private bool programStatusOk = true;

        private readonly object imageLock = new object();
        private Bitmap gearImage;
        private string gearText = "";

        private Bitmap materialNameplateImage;
        private Bitmap materialQuantityImage;
        private string materialText = "";

        private Bitmap moraImage;
        private string moraText = "";

        private Bitmap navigationImage;

        /// <summary>
        /// Raised after any counter-related state changes. Scan logic calls this from background
        /// worker threads; subscribers are responsible for marshaling onto the UI thread themselves.
        /// </summary>
        public event Action CountersChanged;

        public int WeaponCount => weaponCount;

        /// <summary>Null until <see cref="SetWeapon_Max"/> runs -- matches the original "?" placeholder.</summary>
        public int? WeaponMax => weaponMax;
        public int ArtifactCount => artifactCount;

        /// <summary>Null until <see cref="SetArtifact_Max"/> runs -- matches the original "?" placeholder.</summary>
        public int? ArtifactMax => artifactMax;
        public int CharacterCount => characterCount;

        /// <summary>
        /// Null until <see cref="SetCharacter_Max"/> runs -- unlike weapon/artifact max, this is only
        /// known when the user has set a specific "characters to scan" count (not "All"), since
        /// scanning "all" characters has no fixed total known in advance.
        /// </summary>
        public int? CharacterMax => characterMax;

        /// <summary>
        /// Count of distinct materials scanned so far. No corresponding "max" -- material scanning
        /// scrolls until it sees a repeat, so the total isn't known in advance.
        /// </summary>
        public int MaterialCount => materialCount;

        /// <summary>
        /// Rough estimate of remaining scan time, based on progress so far across whichever
        /// categories have a known max (weapons/artifacts always; characters only when the user set a
        /// specific count instead of "All"). Null until at least one item with a known max has been
        /// scanned, so there's a rate to extrapolate from.
        /// </summary>
        public TimeSpan? EstimatedTimeRemaining
        {
            get
            {
                int done = 0, total = 0;
                if (weaponMax.HasValue) { done += weaponCount; total += weaponMax.Value; }
                if (artifactMax.HasValue) { done += artifactCount; total += artifactMax.Value; }
                if (characterMax.HasValue) { done += characterCount; total += characterMax.Value; }

                if (total <= 0 || done <= 0) return null;
                if (done >= total) return TimeSpan.Zero;

                double elapsedSeconds = scanStopwatch.Elapsed.TotalSeconds;
                if (elapsedSeconds <= 0) return null;

                double itemsPerSecond = done / elapsedSeconds;
                if (itemsPerSecond <= 0) return null;

                return TimeSpan.FromSeconds((total - done) / itemsPerSecond);
            }
        }

        /// <summary>Raised after <see cref="SetProgramStatus"/> runs.</summary>
        public event Action ProgramStatusChanged;

        /// <summary>
        /// Raised after <see cref="AddError"/> runs, with the newly-added error. Incremental (not a
        /// full re-render) since errors only ever accumulate until <see cref="ResetErrors"/>.
        /// </summary>
        public event Action<string> ErrorAdded;

        /// <summary>Raised after <see cref="ResetErrors"/> runs.</summary>
        public event Action ErrorsReset;

        public string ProgramStatus => programStatus;
        public bool ProgramStatusOk => programStatusOk;

        /// <summary>Raised after the gear (weapon/artifact) image or text changes.</summary>
        public event Action GearChanged;

        /// <summary>
        /// Owned by this instance -- cloned from whatever <see cref="Bitmap"/> scan logic passes in
        /// (matching the original <c>UpdatePictureBox</c>'s defensive clone, so scan logic can dispose
        /// its own copy freely). <see cref="GameScanner"/>'s worker pool runs multiple background
        /// threads concurrently, any of which can call <see cref="SetGear(Bitmap, Weapon)"/>/
        /// <see cref="SetGearPictureBox"/> at the same time, so reading this field directly and handing
        /// it to a <c>PictureBox</c> is unsafe -- a second thread's dispose-and-replace could run between
        /// the read and the paint, leaving the control holding a disposed image (renders as a white box
        /// with red X's). Use <see cref="CloneGearImage"/> for rendering instead; this property exists
        /// for tests that only inspect state on one thread.
        /// </summary>
        public Bitmap GearImage { get { lock (imageLock) return gearImage; } }
        public string GearText => gearText;

        /// <summary>
        /// Thread-safe snapshot for rendering: clones the current gear image under the same lock the
        /// writers use, so the returned <see cref="Bitmap"/> is independently owned by the caller and
        /// can never be disposed out from under it by a concurrent scan-logic thread. Returns null if
        /// there's no current image.
        /// </summary>
        public Bitmap CloneGearImage()
        {
            lock (imageLock)
            {
                return gearImage == null ? null : CloneBitmap(gearImage);
            }
        }

        /// <summary>
        /// Raised after a material is set (<see cref="SetMaterial"/>). Full-replace, not incremental --
        /// <c>MaterialScraper</c> calls <see cref="ResetCharacterDisplay"/> immediately before every
        /// <see cref="SetMaterial"/>, so the original UI only ever showed the most recently scanned
        /// material, never an accumulating log.
        /// </summary>
        public event Action MaterialChanged;

        public string MaterialText => materialText;

        public Bitmap CloneMaterialNameplateImage()
        {
            lock (imageLock)
            {
                return materialNameplateImage == null ? null : CloneBitmap(materialNameplateImage);
            }
        }

        public Bitmap CloneMaterialQuantityImage()
        {
            lock (imageLock)
            {
                return materialQuantityImage == null ? null : CloneBitmap(materialQuantityImage);
            }
        }

        /// <summary>Raised after <see cref="SetMora"/> runs. Same full-replace reasoning as <see cref="MaterialChanged"/>.</summary>
        public event Action MoraChanged;

        public string MoraText => moraText;

        public Bitmap CloneMoraImage()
        {
            lock (imageLock)
            {
                return moraImage == null ? null : CloneBitmap(moraImage);
            }
        }

        /// <summary>
        /// Raised after <see cref="SetNavigation_Image"/> runs. This is a generic "current capture
        /// region" preview called from every scraper (weapons/artifacts/characters/materials), not
        /// specific to any one scan phase.
        /// </summary>
        public event Action NavigationImageChanged;

        public Bitmap CloneNavigationImage()
        {
            lock (imageLock)
            {
                return navigationImage == null ? null : CloneBitmap(navigationImage);
            }
        }

        public void SetWeapon_Max(int value)
        {
            weaponMax = value;
            CountersChanged?.Invoke();
        }

        public void SetArtifact_Max(int value)
        {
            artifactMax = value;
            CountersChanged?.Invoke();
        }

        public void SetCharacter_Max(int value)
        {
            characterMax = value;
            CountersChanged?.Invoke();
        }

        public void IncrementWeaponCount()
        {
            Interlocked.Increment(ref weaponCount);
            CountersChanged?.Invoke();
        }

        public void IncrementArtifactCount()
        {
            Interlocked.Increment(ref artifactCount);
            CountersChanged?.Invoke();
        }

        public void IncrementCharacterCount()
        {
            Interlocked.Increment(ref characterCount);
            CountersChanged?.Invoke();
        }

        public void ResetCounters()
        {
            Volatile.Write(ref weaponCount, 0);
            weaponMax = null;
            Volatile.Write(ref artifactCount, 0);
            artifactMax = null;
            Volatile.Write(ref characterCount, 0);
            characterMax = null;
            Volatile.Write(ref materialCount, 0);
            scanStopwatch.Restart();
            CountersChanged?.Invoke();
        }

        public void ResetAll()
        {
            ResetGearDisplay();
            ResetMaterialAndMoraDisplay();
            UserInterface.ResetCharacterDisplay();
            ResetCounters();
            ResetErrors();
        }

        private void ResetMaterialAndMoraDisplay()
        {
            lock (imageLock)
            {
                materialNameplateImage?.Dispose();
                materialNameplateImage = null;
                materialQuantityImage?.Dispose();
                materialQuantityImage = null;
                moraImage?.Dispose();
                moraImage = null;
                // Not paired with a NavigationImageChanged notification, matching the original
                // UserInterface.ResetAll() -- it never cleared navigation_PictureBox either, so the
                // control keeps showing the last preview until the next SetNavigation_Image call.
                navigationImage?.Dispose();
                navigationImage = null;
            }
            materialText = "";
            moraText = "";
        }

        public void SetProgramStatus(string status, bool ok = true)
        {
            programStatus = status;
            programStatusOk = ok;
            ProgramStatusChanged?.Invoke();
        }

        public void AddError(string error)
        {
            Logger.Error(error);
            ErrorAdded?.Invoke(error);
        }

        public void ResetErrors()
        {
            ErrorsReset?.Invoke();
        }

        public void SetGear(Bitmap bm, Weapon weapon)
        {
            SetGearImage(bm);
            gearText = weapon.ToString();
            GearChanged?.Invoke();
        }

        public void SetGear(Bitmap bm, Artifact artifact)
        {
            SetGearImage(bm);
            gearText = artifact.ToString();
            GearChanged?.Invoke();
        }

        public void SetGearPictureBox(Bitmap bm)
        {
            SetGearImage(bm);
            GearChanged?.Invoke();
        }

        public void SetGearTextBox(string text)
        {
            gearText = text;
            GearChanged?.Invoke();
        }

        public void ResetGearDisplay()
        {
            lock (imageLock)
            {
                gearImage?.Dispose();
                gearImage = null;
            }
            gearText = "";
            GearChanged?.Invoke();
        }

        private void SetGearImage(Bitmap bm)
        {
            var clone = CloneBitmap(bm);
            lock (imageLock)
            {
                gearImage?.Dispose();
                gearImage = clone;
            }
        }

        private static Bitmap CloneBitmap(Bitmap bm)
        {
            var clone = new Bitmap(bm.Width, bm.Height);
            using (var g = Graphics.FromImage(clone)) g.DrawImage(bm, 0, 0);
            return clone;
        }

        public void SetMaterial(Bitmap nameplate, Bitmap quantity, string name, int count)
        {
            var nameplateClone = CloneBitmap(nameplate);
            var quantityClone = CloneBitmap(quantity);
            lock (imageLock)
            {
                materialNameplateImage?.Dispose();
                materialNameplateImage = nameplateClone;
                materialQuantityImage?.Dispose();
                materialQuantityImage = quantityClone;
            }
            materialText = $"Name: {name}\nCount: {count}";
            Interlocked.Increment(ref materialCount);
            MaterialChanged?.Invoke();
            CountersChanged?.Invoke();
        }

        public void SetMora(Bitmap mora, int count)
        {
            var clone = CloneBitmap(mora);
            lock (imageLock)
            {
                moraImage?.Dispose();
                moraImage = clone;
            }
            moraText = $"Mora: {count}";
            MoraChanged?.Invoke();
        }

        public void SetNavigation_Image(Bitmap bm)
        {
            var clone = CloneBitmap(bm);
            lock (imageLock)
            {
                navigationImage?.Dispose();
                navigationImage = clone;
            }
            NavigationImageChanged?.Invoke();
        }

        /// <summary>
        /// Raised by <see cref="RequestCorrection"/> on the calling (scan) thread. Subscribers must
        /// marshal onto the UI thread themselves (matching every other event here) and set
        /// <see cref="OcrCorrectionEventArgs.ResolvedText"/> before returning from that marshaled
        /// call -- typically by showing a modal dialog inside <c>Control.Invoke</c>, whose own
        /// blocking-until-closed behavior is what pauses the scan thread; no separate wait handle is
        /// needed here.
        /// </summary>
        public event Action<OcrCorrectionEventArgs> CorrectionRequested;

        // Weapon/artifact recognition runs on background worker threads pulled from a channel that
        // the main click/scroll loop feeds and moves on from immediately (see ArtifactScraper/
        // WeaponScraper's QueueScan) -- so blocking a worker thread inside RequestCorrection does
        // NOT, by itself, stop the click loop from continuing to drive the game while a correction
        // dialog sits open. correctionsPending/correctionGate close a separate gate the click loops
        // check between items (IScanProgressReporter.WaitIfCorrectionPending), so the game genuinely
        // pauses for as long as any correction is outstanding -- a count, not a single flag, since
        // multiple low-confidence recognitions can be in flight on different workers at once and the
        // gate must stay closed until every one of them resolves, not just the first.
        private int correctionsPending;
        private readonly ManualResetEventSlim correctionGate = new ManualResetEventSlim(true);

        /// <summary>Blocks the calling thread while any inline correction is awaiting user input.</summary>
        public void WaitIfCorrectionPending() => correctionGate.Wait();

        public string RequestCorrection(Bitmap image, string recognizedText, float confidencePercent, string fieldLabel)
        {
            // No subscriber (headless run, unit test) -- degrade to "use the OCR result as-is"
            // instead of raising an event nobody will ever resolve, which would block forever.
            if (CorrectionRequested == null) return recognizedText;

            if (Interlocked.Increment(ref correctionsPending) == 1) correctionGate.Reset();

            var clone = CloneBitmap(image);
            try
            {
                var args = new OcrCorrectionEventArgs(clone, recognizedText, confidencePercent, fieldLabel);
                CorrectionRequested.Invoke(args);
                return args.ResolvedText ?? recognizedText;
            }
            finally
            {
                clone.Dispose();
                if (Interlocked.Decrement(ref correctionsPending) == 0) correctionGate.Set();
            }
        }

        public void SetMainCharacterName(string text) => UserInterface.SetMainCharacterName(text);
        public void SetCharacter_NameAndElement(Bitmap bm, string name, string element) => UserInterface.SetCharacter_NameAndElement(bm, name, element);
        public void SetCharacter_Level(Bitmap bm, int level, int maxLevel) => UserInterface.SetCharacter_Level(bm, level, maxLevel);
        public void SetCharacter_Constellation(int level) => UserInterface.SetCharacter_Constellation(level);
        public void SetCharacter_Constellation(Bitmap bm, int level) => UserInterface.SetCharacter_Constellation(bm, level);
        public void SetCharacter_Talent(Bitmap bm, string text, int i) => UserInterface.SetCharacter_Talent(bm, text, i);
        public void ResetCharacterDisplay() => UserInterface.ResetCharacterDisplay();
    }
}
