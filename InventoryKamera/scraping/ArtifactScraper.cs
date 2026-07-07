using InventoryKamera.game;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static InventoryKamera.Artifact;

namespace InventoryKamera
{
    internal class ArtifactScraper : InventoryScraper
	{
		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		public ArtifactScraper(IOcrService ocrService, IImagePreprocessor imagePreprocessor, IScanSettings scanSettings, IScanProgressReporter progressReporter) : base(ocrService, imagePreprocessor, scanSettings, progressReporter)
		{
			inventoryPage = InventoryPage.Artifacts;
            SortByLevel = scanSettings.MinimumArtifactLevel > 0;
            SortByObtained = scanSettings.SortByObtained;
        }

        /// <summary>
        /// Sets the sort-by-obtained toggle via controller -- same pixel-color detection concept
        /// as the mouse path (screen-reading doesn't depend on input method), but with the user's
        /// own controller-mode region measurement (2026-07-04) rather than reusing the mouse-mode
        /// region -- the mouse-mode position doesn't hold under controller mode's HUD, same lesson as
        /// the item-count region. Toggle action per user: left stick Up moves to the sort-by-obtained
        /// control, B confirms (the established confirm button everywhere else in this codebase --
        /// not independently confirmed for this specific control), left stick Down returns to the grid.
        /// </summary>
        private void SetSortByObtained(GameController controller)
        {
            using (var x = Navigation.CaptureRegion(
                x: (int)(0.6563 * Navigation.GetWidth()),
                y: (int)((Navigation.IsNormal ? 0.1247 : 0.1121) * Navigation.GetHeight()),
                width: (int)(0.0112 * Navigation.GetWidth()),
                height: (int)(0.0218 * Navigation.GetHeight())))
            {
                SaveDebugScreenshot(x, "artifacts/sortbyobtained/region");

                // Reference color measured directly from controller mode's HUD (2026-07-04),
                // replacing mouse-mode's (224, 198, 147) -- that value failed CompareColors' <10-per-
                // channel tolerance against a live sample (212, 188, 142; R diff 12, G diff 10), which
                // is why the toggle wasn't firing: an actually-"on" state was being misread as "off".
                Color sortObtainedTrue = Color.FromArgb(255, 212, 188, 142);
                Color sortObtainedStatus = x.GetPixel(x.Width / 2, x.Height / 2);
                var sortObtained = GenshinProcesor.CompareColors(sortObtainedTrue, sortObtainedStatus);
                Logger.Info("Sort-by-obtained pixel check: sampled={0} reference={1} matched={2} wantObtained={3}",
                    sortObtainedStatus, sortObtainedTrue, sortObtained, SortByObtained > 0);
                // Per user (2026-07-04): base timing set explicitly to 100/200/100/500/100/200.
                if (SortByObtained > 0 ^ sortObtained)
                {
                    Logger.Info("Sort-by-obtained: toggling (currently {0}, want {1}).", sortObtained, SortByObtained > 0);
                    controller.MoveStep(GameController.MenuDirection.Up, holdMs: ScaledControllerDelay(100), settleMs: ScaledControllerDelay(200));
                    controller.TapButton(Xbox360Button.B, holdMs: ScaledControllerDelay(100));
                    Thread.Sleep(ScaledControllerDelay(500));
                    controller.MoveStep(GameController.MenuDirection.Down, holdMs: ScaledControllerDelay(100), settleMs: ScaledControllerDelay(200));
                }
                else
                {
                    Logger.Info("Sort-by-obtained: already {0}, no toggle needed.", sortObtained);
                }
            }
        }

        /// <summary>
        /// Clears active artifact filters via controller -- filter-active detection reused
        /// unchanged (screen-reading, not input-method-dependent), region re-measured by the user
        /// (2026-07-04) for controller mode's HUD. Reset action per user: D-pad Left, then L3 (left
        /// stick click), then A to close the panel -- confirmed live (2026-07-04) that the physical
        /// Xbox Back/View button does nothing in Genshin's UI; this game's own back/cancel action is
        /// A, not Back (established convention throughout this codebase: "A backs out, B confirms,
        /// everywhere").
        /// </summary>
        private void ClearFilters(GameController controller)
        {
            using (var x = Navigation.CaptureRegion(
                x: (int)(0.0740 * Navigation.GetWidth()),
                y: (int)((Navigation.IsNormal ? 0.8499 : 0.8658) * Navigation.GetHeight()),
                width: (int)(0.2032 * Navigation.GetWidth()),
                height: (int)(0.0306 * Navigation.GetHeight())))
            {
                SaveDebugScreenshot(x, "artifacts/filters/indicator");
                var t = ocrService.AnalyzeText(x).Trim().ToLower();
                bool filterActive = t != null && t.Contains("filter");
                Logger.Info("Filter-active OCR: rawText=\"{0}\" containsFilter={1}", t, filterActive);

                if (filterActive)
                {
                    Logger.Info("Artifact filters active -- clearing.");
                    using (var before = Navigation.CaptureWindow()) SaveDebugScreenshot(before, "artifacts/filters/reset_0_before");

                    // Per user (2026-07-04): base timing set explicitly to 100/200/100/500/100/200.
                    controller.TapButton(Xbox360Button.Left, holdMs: ScaledControllerDelay(100));
                    Thread.Sleep(ScaledControllerDelay(200));
                    using (var afterLeft = Navigation.CaptureWindow()) SaveDebugScreenshot(afterLeft, "artifacts/filters/reset_1_afterleft");

                    controller.TapButton(Xbox360Button.LeftThumb, holdMs: ScaledControllerDelay(100));
                    Thread.Sleep(ScaledControllerDelay(500));
                    using (var afterLS = Navigation.CaptureWindow()) SaveDebugScreenshot(afterLS, "artifacts/filters/reset_2_afterls");

                    controller.TapButton(Xbox360Button.A, holdMs: ScaledControllerDelay(100));
                    Thread.Sleep(ScaledControllerDelay(200));
                    using (var afterBack = Navigation.CaptureWindow()) SaveDebugScreenshot(afterBack, "artifacts/filters/reset_3_afterback");
                }
                else
                {
                    Logger.Info("No active artifact filters -- nothing to clear.");
                }
            }
        }

        /// <summary>Extracts a bitmap copy of an artifact's gear-slot icon, measured with the
        /// coordinate-picker tool (2026-07-04).</summary>
        private Bitmap GetGearSlotBitmap(Bitmap card)
        {
            return GenshinProcesor.CopyBitmap(card, new Rectangle(
                x: (int)(card.Width * 0.0492),
                y: (int)(card.Height * (Navigation.IsNormal ? 0.0676 : 0.0586)),
                width: (int)(card.Width * 0.5009),
                height: (int)(card.Height * 0.0333)));
        }

        /// <summary>Extracts a bitmap copy of an artifact's main stat readout, measured with the
        /// coordinate-picker tool (2026-07-04).</summary>
        private Bitmap GetMainStatBitmap(Bitmap card)
        {
            return GenshinProcesor.CopyBitmap(card, new Rectangle(
                x: (int)(card.Width * 0.0528),
                y: (int)(card.Height * (Navigation.IsNormal ? 0.1500 : 0.1319)),
                width: (int)(card.Width * 0.4663),
                height: (int)(card.Height * 0.0306)));
        }

        // Per user (2026-07-04): the sanctify shift equals the sanctify banner's own height, since a
        // sanctified card simply inserts the banner above Level/Substats/Locked, pushing them all
        // down by exactly that much. Shared by GetLevelBitmap/GetSubstatsBitmap/
        // GetLockedBitmap and DetectSanctified's own region height.
        // Re-measured 2026-07-04 (0.0491 -> 0.0426): the first pass over-estimated the banner height,
        // which pushed Substats' crop far enough to catch the set-bonus description line below the
        // real stat list -- tighter measurement here fixes that overshoot.
        private double SanctifyShift = Navigation.IsNormal ? 0.0426 : 0.0349;

        /// <summary>Extracts a bitmap copy of an artifact's level readout, measured with the
        /// coordinate-picker tool (2026-07-04). <paramref name="isSanctified"/> shifts the crop down
        /// by the sanctify banner's own height (see <see cref="SanctifyShift"/>).</summary>
        private Bitmap GetLevelBitmap(Bitmap card, bool isSanctified = false)
        {
            double yShift = isSanctified ? SanctifyShift : 0.0;
            return GenshinProcesor.CopyBitmap(card, new Rectangle(
                x: (int)(card.Width * 0.0565),
                y: (int)(card.Height * ((Navigation.IsNormal ? 0.3120 : 0.2735) + yShift)),
                width: (int)(card.Width * 0.1184),
                height: (int)(card.Height * 0.0306)));
        }

        /// <summary>Extracts a bitmap copy of an artifact's substats block, measured with the
        /// coordinate-picker tool (2026-07-04). Same sanctify-shift handling as
        /// <see cref="GetLevelBitmap"/>.</summary>
        private Bitmap GetSubstatsBitmap(Bitmap card, bool isSanctified = false)
        {
            double yShift = isSanctified ? SanctifyShift : 0.0;
            return GenshinProcesor.CopyBitmap(card, new Rectangle(
                x: (int)(card.Width * 0.0437),
                y: (int)(card.Height * ((Navigation.IsNormal ? 0.3611 : 0.3150) + yShift)),
                width: (int)(card.Width * 0.8397),
                height: (int)(card.Height * 0.1991)));
        }

        /// <summary>Extracts a bitmap copy of an item card's lock status icon, measured with the
        /// coordinate-picker tool (2026-07-04). Same sanctify-shift handling as
        /// <see cref="GetLevelBitmap"/>.</summary>
        private Bitmap GetLockedBitmap(Bitmap card, bool isSanctified = false)
        {
            double yShift = isSanctified ? SanctifyShift : 0.0;
            return GenshinProcesor.CopyBitmap(card, new Rectangle(
                x: (int)(card.Width * 0.6321),
                y: (int)(card.Height * ((Navigation.IsNormal ? 0.3056 : 0.2650) + yShift)),
                width: (int)(card.Width * 0.0893),
                height: (int)(card.Height * 0.0435)));
        }

        /// <summary>
        /// Tight crop of just the sanctify icon/sigil (2026-07-04, replacing the earlier wide banner
        /// span) -- measured with the coordinate-picker tool, matching the lock-icon crop's
        /// tightly-cropped-icon pattern rather than the wide-banner OCR approach this superseded.
        /// </summary>
        private Bitmap GetSanctifyIconBitmap(Bitmap card)
        {
            return GenshinProcesor.CopyBitmap(card, new Rectangle(
                x: (int)(card.Width * 0.0055),
                y: (int)(card.Height * (Navigation.IsNormal ? 0.2907 : 0.2515)),
                width: (int)(card.Width * 0.0473),
                height: (int)(card.Height * 0.0398)));
        }

        /// <summary>
        /// Detects sanctify status from an already-captured <see cref="GetSanctifyIconBitmap"/>
        /// crop via a single-pixel color sample (2026-07-04) -- replaces the earlier full-text OCR
        /// check, which was the single most expensive per-item operation in the artifact scan loop
        /// (OCR is much slower than a pixel sample, and it ran on every single item). Reuses the
        /// mouse path's own sanctify reference color (<see cref="QueueScan"/>'s <c>sanctifiedColor</c>,
        /// (220, 192, 255)) since it's the same in-game icon asset, just captured from a different
        /// on-screen position -- the same reasoning already confirmed correct for the lock-icon reuse.
        /// Logs the sampled color in case this reference doesn't hold under controller mode's
        /// rendering, same lesson as sort-by-obtained's reference color needing remeasurement.
        /// </summary>
        private bool DetectSanctifiedFromBitmap(Bitmap sanctifyIcon)
        {
            Color sanctifiedColor = Color.FromArgb(255, 220, 192, 255);
            Color sampled = sanctifyIcon.GetPixel(sanctifyIcon.Width / 2, sanctifyIcon.Height / 2);
            bool sanctified = GenshinProcesor.CompareColors(sanctifiedColor, sampled);
            Logger.Debug("Sanctify icon pixel check: sampled={0} reference={1} matched={2}", sampled, sanctifiedColor, sanctified);
            return sanctified;
        }

        /// <summary>Captures and detects sanctify status in one step, disposing the capture
        /// afterward -- for callers (like the single-item debug read) that don't need to keep the
        /// bitmap around. <see cref="QueueScan"/> instead keeps the bitmap alive since it
        /// needs to hand it into the worker queue.</summary>
        private bool DetectSanctified(Bitmap card)
        {
            using (var region = GetSanctifyIconBitmap(card))
            {
                SaveDebugScreenshot(region, "SelectedArtifactDetailsSanctifyIcon");
                return DetectSanctifiedFromBitmap(region);
            }
        }

        /// <summary>
        /// Ad-hoc single-item read for Phase 3 §6c artifact controller-scan development -- reads every
        /// field the mouse path's <see cref="QueueScan"/> captures for whichever artifact is currently
        /// selected, without any mouse clicking. Not yet wired into a full scan loop (see
        /// <c>WeaponScraper.ScanWeapons</c> for the pattern once this is proven). Caller
        /// must already be on the Artifacts tab. Detects sanctify status first and passes it to
        /// Level/Substats/Locked so their shifted positions (see <see cref="SanctifyShift"/>) are used
        /// automatically -- live-verify this actually lands correctly on a sanctified artifact.
        /// </summary>
        public (string SetName, string GearSlot, string MainStat, int Level, bool Sanctified, string Equipped, bool Locked, string SubStatsSummary) ReadSelectedArtifactDetails()
        {
            using (var card = GetItemCard())
            {
                SaveDebugScreenshot(card, "SelectedArtifactDetailsCard");

                string setName;
                using (var nameBitmap = GetItemNameBitmap(card))
                {
                    SaveDebugScreenshot(nameBitmap, "SelectedArtifactDetailsName");
                    setName = ScanArtifactSet(nameBitmap);
                }

                string gearSlot;
                using (var gearSlotBitmap = GetGearSlotBitmap(card))
                {
                    SaveDebugScreenshot(gearSlotBitmap, "SelectedArtifactDetailsGearSlot");
                    gearSlot = ScanArtifactGearSlot(gearSlotBitmap);
                }

                string mainStat;
                using (var mainStatBitmap = GetMainStatBitmap(card))
                {
                    SaveDebugScreenshot(mainStatBitmap, "SelectedArtifactDetailsMainStat");
                    mainStat = ScanArtifactMainStat(mainStatBitmap, gearSlot);
                }

                bool sanctified = DetectSanctified(card);

                int level;
                using (var levelBitmap = GetLevelBitmap(card, sanctified))
                {
                    SaveDebugScreenshot(levelBitmap, "SelectedArtifactDetailsLevel");
                    level = ScanArtifactLevel(levelBitmap);
                }

                string subStatsSummary;
                using (var subStatsBitmap = GetSubstatsBitmap(card, sanctified))
                {
                    SaveDebugScreenshot(subStatsBitmap, "SelectedArtifactDetailsSubstats");
                    var (active, unactivated) = ScanArtifactSubStats(subStatsBitmap);
                    subStatsSummary = string.Join(", ", active.Select(s => $"{s.stat}:{s.value}"));
                    if (unactivated.Count > 0)
                        subStatsSummary += $" (unactivated: {string.Join(", ", unactivated.Select(s => $"{s.stat}:{s.value}"))})";
                }

                string equipped;
                using (var equippedBitmap = GetEquippedBitmap(card))
                {
                    SaveDebugScreenshot(equippedBitmap, "SelectedArtifactDetailsEquipped");
                    equipped = ScanArtifactEquippedCharacter(equippedBitmap);
                }

                bool locked;
                using (var lockedBitmap = GetLockedBitmap(card, sanctified))
                {
                    SaveDebugScreenshot(lockedBitmap, "SelectedArtifactDetailsLocked");
                    Color lockedColor = Color.FromArgb(255, 70, 80, 100);
                    Color lockStatus = lockedBitmap.GetPixel(10, 10);
                    locked = GenshinProcesor.CompareColors(lockedColor, lockStatus);
                }

                return (setName, gearSlot, mainStat, level, sanctified, equipped, locked, subStatsSummary);
            }
        }

        /// <summary>
        /// Controller-mode equivalent of <see cref="QueueScan"/>: takes an already-captured card
        /// (from <see cref="ScanArtifacts"/>'s navigation loop) instead of re-capturing
        /// via the mouse-hover-popup region. Same field set, index order, filtering, and queue
        /// dispatch as the mouse path so both feed the identical worker/cataloguing pipeline.
        /// </summary>
        private void QueueScan(Bitmap card, int id)
        {
            Bitmap name = GetItemNameBitmap(card);
            Bitmap gearSlot = GetGearSlotBitmap(card);
            Bitmap mainStat = GetMainStatBitmap(card);
            Bitmap sanctify = GetSanctifyIconBitmap(card);
            bool sanctified = DetectSanctifiedFromBitmap(sanctify);

            Bitmap level = GetLevelBitmap(card, sanctified);
            Bitmap subStats = GetSubstatsBitmap(card, sanctified);
            Bitmap equipped = GetEquippedBitmap(card);
            Bitmap locked = GetLockedBitmap(card, sanctified);

            List<Bitmap> artifactImages = new List<Bitmap>
            {
                name, //0
                gearSlot,
                mainStat,
                level,
                subStats,
                equipped, //5
                locked,
                sanctify,
                card
            };

            bool belowRarity = GetRarity(name) < scanSettings.MinimumArtifactRarity;
            bool belowLevel = ScanArtifactLevel(level) < scanSettings.MinimumArtifactLevel;

            // Never sets StopScanning here -- unlike WeaponScraper's controller path, artifact
            // sort-mode selection hasn't been ported (mouse mode's artifact sort, SetSort/
            // SortByObtained, is a different mechanism than weapons' Level/Quality/Type dropdown, and
            // hasn't been confirmed reachable via controller at all). Filtering (not queuing) a
            // disqualified item is still safe on an unsorted grid; stopping the whole scan early is
            // not, since it'd risk silently skipping later qualifying artifacts.
            if (belowRarity || belowLevel)
            {
                Logger.Info("Artifact scan #{0}: filtered out (belowRarity={1}, belowLevel={2}, sanctified={3}).",
                    id, belowRarity, belowLevel, sanctified);
                // Filtered-out items never reach ProcessImageCollectionAsync (InventoryKamera.cs),
                // the only place that normally saves per-region crops -- without this, a wrong
                // belowLevel/belowRarity read (e.g. a miscalibrated crop) filtered an item with zero
                // visual evidence of what was actually captured.
                SaveDebugScreenshot(name, $"artifacts/artifact{id}/name/name");
                SaveDebugScreenshot(level, $"artifacts/artifact{id}/level/level");
                SaveDebugScreenshot(card, $"artifacts/artifact{id}/card");
                artifactImages.ForEach(i => i.Dispose());
                return;
            }

            Logger.Info("Artifact scan #{0}: queued for cataloguing (sanctified={1}).", id, sanctified);
            InventoryKamera.workerChannel.Writer.TryWrite(new OCRImageCollection(artifactImages, "artifact", id));
        }

        /// <summary>
        /// Controller-driven artifact scan (Phase 3 §6c) -- real replacement for <see cref="ScanArtifacts"/>'s
        /// mouse click/scroll loop, wired into <c>InventoryKamera.GatherData</c>. Takes an
        /// already-connected <paramref name="controller"/> that's already inside Inventory (per user,
        /// 2026-07-04: switching between Weapons/Artifacts tabs shouldn't back all the way out to the
        /// unpaused game state and re-enter -- see <c>WeaponScraper.ScanWeapons</c>'s doc
        /// comment for the full reasoning; <c>GatherData</c> now owns the single controller/entry
        /// spanning both phases). Switches to Artifacts, sets sort-by-obtained and clears filters
        /// (<see cref="SetSortByObtained"/>/<see cref="ClearFilters"/>, both
        /// live-verified 2026-07-04), then repeatedly reads the selected item's card and advances with
        /// a single left-stick push to the Right (per user, confirmed to work the same way as weapons).
        /// UNVERIFIED assumptions carried over from weapons without being independently reconfirmed for
        /// Artifacts: end-of-list behavior, and that <see cref="InventoryScraper.ScanItemCount"/>'s
        /// weapon-measured region also correctly reads the Artifacts count label (plausible since it's
        /// a shared HUD element, but not directly confirmed). No sort-MODE selection, i.e. Level/
        /// Quality/Type (see <see cref="QueueScan"/>'s own comment) -- scans in whatever
        /// order the grid is currently in, filtering without early-stop.
        /// </summary>
        /// <param name="knownCurrentTab">See <see cref="InventoryScraper.SwitchToTab"/> --
        /// pass the previous controller-driven phase's returned tab (e.g. "Weapons") to skip
        /// re-detecting via OCR.</param>
        public string ScanArtifacts(GameController controller, int count = 0, string knownCurrentTab = null)
        {
            StopScanning = false;

            string currentTab = SwitchToTab(controller, "Artifacts", knownCurrentTab);
            SetSortByObtained(controller);
            ClearFilters(controller);

            int artifactCount = count == 0 ? ScanItemCount() : count;

            // Mouse-mode's ScanArtifacts() caps the scan to SortByObtained * fullPage items when
            // that setting is active (fullPage = cols*rows from GetPageOfItems' blob detection,
            // "only scan the N most-recently-obtained pages"). Controller mode doesn't do that
            // grid detection, so per user (2026-07-04): fullPage is replaced with a fixed 10
            // (controller mode's per-row artifact count), i.e. SortByObtained now means "N rows of
            // most-recently-obtained artifacts" rather than "N full multi-row screens".
            const int artifactsPerRow = 10;
            if (SortByObtained > 0 && SortByObtained * artifactsPerRow <= artifactCount)
            {
                artifactCount = SortByObtained * artifactsPerRow;
            }

            progressReporter.SetArtifact_Max(artifactCount);

            int scanned = 0;

            while (scanned < artifactCount && !InventoryKamera.CancelRequested && !StopScanning)
            {
                progressReporter.WaitIfCorrectionPending();

                // Not wrapped in `using` -- QueueScan hands `card` into
                // artifactImages, which either gets disposed immediately (filtered out) or flows
                // into the worker channel for async cataloguing/disposal, matching QueueScan's
                // mouse-path pattern. Disposing it here would race the worker thread.
                Bitmap card = GetItemCard();
                QueueScan(card, scanned);
                scanned++;

                if (scanned >= artifactCount) break;

                // Per user (2026-07-04): bumped from 80/100 -> 100/150 -- after the sanctify
                // check switched from OCR to a pixel sample, the removed OCR time turned out to
                // have been accidentally giving Genshin's selection-change animation enough room
                // to finish, and items started getting skipped without it.
                controller.MoveStep(GameController.MenuDirection.Right,
                    holdMs: ScaledControllerDelay(100), settleMs: ScaledControllerDelay(150));
            }

            Logger.Info("Controller artifact scan finished: {0} of {1} scanned (cancelled={2}, stopped={3})",
                scanned, artifactCount, InventoryKamera.CancelRequested, StopScanning);

            // Always report "Artifacts" here, not whatever SwitchToTab returned -- see
            // WeaponScraper.ScanWeapons's matching comment: by the time we get here the
            // inventory is on Artifacts regardless of whether the pre-switch OCR detection succeeded,
            // and reporting the real value lets the next phase skip re-running that same flaky OCR.
            return "Artifacts";
        }

        public async Task<Artifact> CatalogueFromBitmapsAsync(List<Bitmap> bm, int id)
		{
			// Init Variables
			string gearSlot = null;
			string mainStat = null;
			string setName = null;
			string equippedCharacter = null;
			List<SubStat> subStats = new List<SubStat>();
            List<SubStat> unactivatedSubStats = new List<SubStat>();
            int rarity = 0;
			int level = 0;
			bool _lock = false;

			if (bm.Count >= 6)
			{
				int a_name = 0; int a_gearSlot = 1; int a_mainStat = 2; int a_level = 3; int a_subStats = 4; int a_equippedCharacter = 5; int a_lock = 6; 
				// Get Rarity
				rarity = GetRarity(bm[a_name]);

				// Check for equipped color
				Color equippedColor = Color.FromArgb(255, 255, 231, 187);
				Color equippedStatus = bm[a_equippedCharacter].GetPixel(5, 5);
				bool b_equipped = GenshinProcesor.CompareColors(equippedColor, equippedStatus);

				// Check for lock color
				Color lockedColor = Color.FromArgb(255, 70, 80, 100); // Dark area around red lock
				Color lockStatus = bm[a_lock].GetPixel(10, 10);
				_lock = GenshinProcesor.CompareColors(lockedColor, lockStatus);

				// Improved Scanning using multi threading
				List<Task> tasks = new List<Task>();

				var taskGear  = Task.Run(() => gearSlot = ScanArtifactGearSlot(bm[a_gearSlot]));
				var taskMain  = taskGear.ContinueWith( (antecedent) => mainStat = ScanArtifactMainStat(bm[a_mainStat], antecedent.Result));
				var taskLevel = Task.Run(() => level = ScanArtifactLevel(bm[a_level]));
				var taskSubs  = Task.Run(() => (subStats, unactivatedSubStats) = ScanArtifactSubStats(bm[a_subStats]));
				var taskEquip = Task.Run(() => equippedCharacter = ScanArtifactEquippedCharacter(bm[a_equippedCharacter]));
				var taskName = Task.Run(() => setName = ScanArtifactSet(bm[a_name]));

				tasks.Add(taskGear);
				tasks.Add(taskMain);
				tasks.Add(taskLevel);
				tasks.Add(taskSubs);
				tasks.Add(taskName);
				if (b_equipped)
				{
					tasks.Add(taskEquip);
				}

				await Task.WhenAll(tasks.ToArray());
			}
			return new Artifact(setName, rarity, level, gearSlot, mainStat, subStats, unactivatedSubStats, equippedCharacter, id, _lock);
		}

		private int GetRarity(Bitmap bm)
		{
			var avg = imagePreprocessor.AverageColor(bm);
			var averageColor = Color.FromArgb((int)avg.R, (int)avg.G, (int)avg.B);

			Color fiveStar = Color.FromArgb(255, 188, 105, 50);
			Color fourStar = Color.FromArgb(255, 161, 86, 224);
			Color threeStar = Color.FromArgb(255, 81, 127, 203);
			Color twoStar = Color.FromArgb(255, 42, 143, 114);
			Color oneStar = Color.FromArgb(255, 114, 119, 138);

			var colors = new List<Color> { Color.Black, oneStar, twoStar, threeStar, fourStar, fiveStar };

			var c = GenshinProcesor.ClosestColor(colors, averageColor);

			return colors.IndexOf(c);
		}

		public bool IsEnhancementMaterial(Bitmap card)
		{
			RECT reference = Navigation.GetAspectRatio() == new Size(16, 9) ?
				new RECT(new Rectangle(862, 80, 327, 560)) : (RECT)new Rectangle(862, 80, 328, 640);
			Bitmap nameBitmap = card.Clone(new RECT(
				Left: 0,
				Top: 0,
				Right: card.Width,
				Bottom: (int)( 38.0 / reference.Height * card.Height )), card.PixelFormat);
			string material = ScanEnhancementMaterialName(nameBitmap);
			return !string.IsNullOrWhiteSpace(material) && GenshinProcesor.enhancementMaterials.Contains(material.ToLower());
		}

		private string ScanEnhancementMaterialName(Bitmap bm)
		{
			GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref bm);
			Bitmap n = imagePreprocessor.ConvertToGrayscale(bm);
			imagePreprocessor.SetInvert(ref n);

			// Analyze
			string name = Regex.Replace(ocrService.AnalyzeText(n).ToLower(), @"[\W]", string.Empty);
			name = GenshinProcesor.FindClosestMaterialName(name);
			n.Dispose();

			return name;
		}

		#region Task Methods

		private string ScanArtifactGearSlot(Bitmap bm)
		{
			// Process Img
			Bitmap n = imagePreprocessor.ConvertToGrayscale(bm);
			imagePreprocessor.SetContrast(80.0, ref n);
			imagePreprocessor.SetInvert(ref n);

			string gearSlot = ocrService.AnalyzeText(n).Trim().ToLower();
			gearSlot = Regex.Replace(gearSlot, @"[\W_]", string.Empty);
			gearSlot = GenshinProcesor.FindClosestGearSlot(gearSlot);
			n.Dispose();
			return gearSlot;
		}

		private string ScanArtifactMainStat(Bitmap bm, string gearSlot)
		{
			switch (gearSlot)
			{
				// Flower of Life. Flat HP
				case "flower":
					return GenshinProcesor.Stats["hp"];

				// Plume of Death. Flat ATK
				case "plume":
					return GenshinProcesor.Stats["atk"];

				// Otherwise it's either sands, goblet or circlet.
				default:
					Bitmap copy = (Bitmap)bm.Clone();
					imagePreprocessor.SetContrast(100.0, ref copy);
					Bitmap n = imagePreprocessor.ConvertToGrayscale(copy);
					
					imagePreprocessor.SetThreshold(135, ref n);
					imagePreprocessor.SetInvert(ref n);

					// Get Main Stat
					string mainStat = ocrService.AnalyzeText(n).ToLower().Trim();
					

					// Remove anything not a-z as well as removes spaces/underscores
					mainStat = Regex.Replace(mainStat, @"[\W_0-9]", string.Empty);

					mainStat = GenshinProcesor.FindClosestStat(mainStat, 80);

					if (mainStat == "def" || mainStat == "atk" || mainStat == "hp")
					{
						mainStat += "_";
					}
					n.Dispose();
					copy.Dispose();
					return mainStat;
			}
		}

		private int ScanArtifactLevel(Bitmap bm)
		{
			// Process Img
			Bitmap n = imagePreprocessor.ConvertToGrayscale(bm);
			imagePreprocessor.SetContrast(80.0, ref n);
			imagePreprocessor.SetInvert(ref n);

			// numbersOnly = true => seems to interpret the '+' as a '4'
			string text = ocrService.AnalyzeText(n, Tesseract.PageSegMode.SingleWord).Trim().ToLower();
			n.Dispose();

			// Get rid of all non digits
			text = Regex.Replace(text, @"[\D]", string.Empty);

			return int.TryParse(text, out int level) ? level : -1;
		}

		private (List<SubStat> active, List<SubStat> unactivated) ScanArtifactSubStats(Bitmap artifactImage)
        {
            Bitmap bm = (Bitmap)artifactImage.Clone();
			List<string> lines = new List<string>();
			List<SubStat> substats = new List<SubStat>();
			List<SubStat> unactivated = new List<SubStat>();
			string text;
            GenshinProcesor.SetBrightness(-30, ref bm);
            imagePreprocessor.SetContrast(85, ref bm);
			bool hasUnactivated = false;
			using (var n = imagePreprocessor.ConvertToGrayscale(bm))
			{
				text = ocrService.AnalyzeText(n, Tesseract.PageSegMode.Auto).ToLower();
			}

			if(text.Contains("(unactivated)"))
			{
				hasUnactivated = true;
			}

            lines = new List<string>(text.Split('\n'));
            lines.RemoveAll(line => string.IsNullOrWhiteSpace(line));

            var index = lines.FindIndex(line =>
				Regex.IsMatch(line, @"(piece|set|2-)") ||
				Regex.IsMatch(line.Trim(), @"^[A-Za-z\s]+:$")
			);
            if (index >= 0)
			{
				lines.RemoveRange(index, lines.Count - index);
			}

            bm.Dispose();
			for (int i = 0; i < lines.Count; i++)
			{
				var line = Regex.Replace(lines[i], @"(?:^[^a-zA-Z]*)", string.Empty).Replace(" ", string.Empty);

				if (line.Any(char.IsDigit))
				{
					Logger.Debug("Parsing artifact substat: {0}", line);

					SubStat substat = new SubStat();
					Regex re = new Regex(@"^(.*?)(\d+.*)");
					var result = re.Match(line);
					var stat = Regex.Replace(result.Groups[1].Value, @"[^\w]", string.Empty);
					var value = result.Groups[2].Value;

					string name = line.Contains("%") ? stat + "%" : stat;

					substat.stat = GenshinProcesor.FindClosestStat(name, 80) ?? "";

					// Remove any non digits.
					value = Regex.Replace(value, @"[^0-9]", string.Empty);

					// Try to parse number
					if (!decimal.TryParse(value, out substat.value))
					{
						Logger.Debug("Failed to parse stat value from: {0}", line);
						substat.value = -1;
					}

					if (substat.value != -1 && substat.stat.Contains("_"))
					{
						substat.value /= 10;
					}

					if (string.IsNullOrWhiteSpace(substat.stat) || substat.value == -1)
					{
						Logger.Debug("Failed to parse stat from: {0}", line);
					}

					// Was substats.Insert(i, substat) -- i is the outer loop's index into *all* lines,
					// but substats only grows for lines that contain a digit (see the `if` above), so
					// i drifts past substats.Count as soon as any earlier line gets skipped, throwing
					// ArgumentOutOfRangeException on Insert. The intent is clearly to append parsed
					// substats in encountered order, which Add does correctly regardless of how many
					// lines were skipped.
					substats.Add(substat);
				}
			}

            if (substats.Count == 0)
			{
				Logger.Debug("Failed to obtain substats");
			}

			//if theres an unactivated substat, moves the last one (should be the only unactivated) to the unactivated list
			if (hasUnactivated && substats.Count > 0)
			{
				SubStat lastSubstat = substats[substats.Count - 1];
				unactivated.Insert(0, lastSubstat);
				substats.Remove(lastSubstat);
			}

            return (substats, unactivated);
        }

        private string ScanArtifactEquippedCharacter(Bitmap bm)
		{
			Bitmap n = imagePreprocessor.ConvertToGrayscale(bm);
			imagePreprocessor.SetContrast(60.0, ref n);

			string equippedCharacter = ocrService.AnalyzeText(n).ToLower();
			n.Dispose();

			if (equippedCharacter != "")
			{
				if (equippedCharacter.Contains("equipped") && equippedCharacter.Contains(":"))
				{
					equippedCharacter = Regex.Replace(equippedCharacter.Split(':')[1], @"[\W]", string.Empty);
					equippedCharacter = GenshinProcesor.FindClosestCharacterName(equippedCharacter);

					return equippedCharacter;
				}
			}
			// artifact has no equipped character
			return null;
		}

		private string ScanArtifactSet(Bitmap itemName)
        {
            GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref itemName);
            Bitmap grayscale = imagePreprocessor.ConvertToGrayscale(itemName);
            imagePreprocessor.SetInvert(ref grayscale);

            // Analyze
            using (Bitmap padded = new Bitmap((int)(grayscale.Width + grayscale.Width * .1), grayscale.Height + (int)(grayscale.Height * .5)))
            {
                using (Graphics g = Graphics.FromImage(padded))
                {
                    g.Clear(Color.White);
                    g.DrawImage(grayscale, (padded.Width - grayscale.Width) / 2, (padded.Height - grayscale.Height) / 2);

                    var (rawText, confidence) = ocrService.AnalyzeTextWithConfidence(grayscale, Tesseract.PageSegMode.Auto);
                    var scannedText = rawText.ToLower().Replace("\n", " ");
                    string text = Regex.Replace(scannedText, @"[\W]", string.Empty);
                    string setName = GenshinProcesor.FindClosestArtifactSetFromArtifactName(text);

                    float confidencePercent = confidence * 100;
                    Logger.Debug("Artifact set name OCR: rawText=\"{0}\" matchedSet=\"{1}\" confidence={2:0.0}% threshold={3}%", text, setName, confidencePercent, scanSettings.OcrConfidenceThreshold);
                    // No fuzzy-match hit at all (setName null) is always worth a correction regardless
                    // of OCR confidence -- Tesseract can be very confident about text that still
                    // doesn't resemble any known artifact set.
                    if (string.IsNullOrWhiteSpace(setName) || confidencePercent < scanSettings.OcrConfidenceThreshold)
                    {
                        Logger.Debug("Artifact set name below confidence threshold or no fuzzy match -- requesting inline correction");
                        string corrected = progressReporter.RequestCorrection(grayscale, text, confidencePercent, "Artifact set name");
                        if (!string.IsNullOrWhiteSpace(corrected))
                        {
                            string normalized = Regex.Replace(corrected.ToLower(), @"[\W]", string.Empty);
                            setName = GenshinProcesor.FindClosestArtifactSetFromArtifactName(normalized) ?? corrected;
                        }
                    }

					grayscale.Dispose();

					return setName;
                }
            }
        }

        #endregion Task Methods
    }
}