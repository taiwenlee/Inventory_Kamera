using InventoryKamera.game;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace InventoryKamera
{
    internal class CharacterScraper
	{
		private static NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		protected int NumOfCharToScan;

		private readonly IOcrService ocrService;
		private readonly IImagePreprocessor imagePreprocessor;
		private readonly IScanSettings scanSettings;
		private readonly IScanProgressReporter progressReporter;

        public CharacterScraper(IOcrService ocrService, IImagePreprocessor imagePreprocessor, IScanSettings scanSettings, IScanProgressReporter progressReporter)
		{
			this.ocrService = ocrService;
			this.imagePreprocessor = imagePreprocessor;
			this.scanSettings = scanSettings;
			this.progressReporter = progressReporter;
			NumOfCharToScan = scanSettings.NumOfCharToScan;
		}

		/// <summary>
		/// Childe (Tartaglia) passive buff fix -- his kit grants +1 auto-attack talent level to the
		/// first 4 party members once he's Ascension 4+, which the raw scanned talent level doesn't
		/// reflect. Used by the controller-driven batched path (<see cref="ScanCharacters"/>)
		/// since it only needs the final assembled roster, not how it was scanned.
		/// </summary>
		private void ApplyTartagliaFix(List<Character> Characters)
		{
			for (int i = 0; i < Characters.Count; i++)
			{
				if (Characters[i].NameGOOD.ToLower() == "tartaglia" && Characters[i].Ascension >= 4)
				{
					Logger.Info("Ascension 4+ Tartaglia found at position {0}.", i);
					if (i < 4)
					{
						for (int j = 0; j < 4; j++)
						{
							Characters[j].Talents["auto"] -= 1;
							Logger.Info("Applied Tartaglia auto attack fix to {0} at position {1}.", Characters[j].NameGOOD, j);
						}
						break;
					}
					else
					{
						Characters[i].Talents["auto"] -= 1;
						Logger.Info("Applied Tartaglia auto attack fix to self only.");
						break;
					}
				}
				else if (Characters[i].NameGOOD.ToLower() == "tartaglia") break;
			}
		}

		/// <summary>
		/// Skirk passive buff fix -- per user (2026-07-05), her kit grants +1 Skill talent level to
		/// the whole active team whenever that team (the first 4 scanned characters, matching
		/// <see cref="ApplyTartagliaFix"/>'s own team-position convention) is composed entirely of
		/// Hydro and Cryo characters, which the raw scanned talent level doesn't reflect. Unlike
		/// Tartaglia's fix, there's no ascension requirement, and there's no self-only fallback for an
		/// off-team Skirk -- her buff simply doesn't apply if she isn't one of the first 4. Stacks
		/// additively with any other active team buff on the same talent (only Tartaglia's exists
		/// today, and it targets a different talent, "auto", so there's no actual overlap yet).
		/// </summary>
		private void ApplySkirkFix(List<Character> Characters)
		{
			if (Characters.Count < 4) return;

			int skirkIndex = Characters.FindIndex(c => c.NameGOOD.ToLower() == "skirk");
			if (skirkIndex < 0 || skirkIndex >= 4) return;

			bool wholeTeamHydroOrCryo = Characters.Take(4).All(c =>
				c.Element != null && (c.Element.ToLower() == "hydro" || c.Element.ToLower() == "cryo"));

			if (!wholeTeamHydroOrCryo) return;

			Logger.Info("Skirk found on an all-Hydro/Cryo team -- applying team Skill buff correction.");
			for (int j = 0; j < 4; j++)
			{
				Characters[j].Talents["skill"] -= 1;
				Logger.Info("Applied Skirk skill fix to {0} at position {1}.", Characters[j].NameGOOD, j);
			}
		}

		/// <summary>
		/// Controller-driven character scan (Phase 3 §6c), batched by sub-tab across the whole roster
		/// instead of per-character. Per user design decision (2026-07-05): switching the character
		/// screen's sub-tab (left-stick up/down; full order is Attributes, Weapons, Artifacts,
		/// Constellations, Talents, Profile) pays a real UI transition-animation cost, while advancing
		/// between characters (right shoulder button, mirroring how
		/// <see cref="InventoryScraper.SwitchToTab"/> cycles inventory tabs with LB/RB)
		/// does not. Interleaving sub-tabs per character would pay that animation cost repeatedly per
		/// character; this instead pays it exactly twice for the whole scan (Attributes to
		/// Constellations, a 3-step move past Weapons/Artifacts; Constellations to Talents, 1 step),
		/// independent of roster size -- at the cost of doing 3 full passes over the roster instead of
		/// 1. That trade favors batching since shoulder-tap advance is the cheap, already-fast-tuned
		/// primitive elsewhere (see <see cref="WeaponScraper.ScanWeapons"/>). Weapons and
		/// Artifacts sub-tabs are only transited through, never scanned, from this method (equipped
		/// gear is already covered by <see cref="WeaponScraper"/>/<see cref="ArtifactScraper"/>'s own
		/// controller scans).
		/// No dedicated rewind-to-start step exists between phases (per user, 2026-07-05): a full
		/// scan's cursor is already back on the first character after Phase 1's wraparound detection,
		/// so Phase 2/3 just read forward again (which wraps around for free, since count equals the
		/// whole roster). A partial scan's cursor instead sits one character past the last one
		/// scanned (no wraparound, since the roster has more characters left over) -- Phase 2 reads
		/// backward from there (<see cref="ScanRosterBackward"/>) to reach the same characters a
		/// rewind-then-forward pass would, in half the shoulder taps, which conveniently leaves the
		/// cursor back on the first character for Phase 3 to read forward
		/// (<see cref="ScanRosterForward"/>) same as the full-scan case.
		/// Constellation-node navigation and talent reading each get their own controller-native
		/// method (<see cref="ScanConstellations"/>/<see cref="ScanTalents"/>)
		/// rather than reusing the mouse path's per-node click loop.
		/// <paramref name="Characters"/> is scanned up to <see cref="NumOfCharToScan"/> entries (0 =
		/// whole roster).
		/// </summary>
		public void ScanCharacters(GameController controller, ref List<Character> Characters)
		{
			int maxToScan = NumOfCharToScan;
			if (maxToScan != 0) progressReporter.SetCharacter_Max(maxToScan);
			progressReporter.ResetCharacterDisplay();

			EnterCharacterMenu(controller);

			// Per user (2026-07-05): the Character menu always opens on the Attributes sub-tab
			// regardless of what was open last time, so no reset-to-known-position step is needed
			// here. Also per user: unlike the pause-menu tab bar, this sub-tab control is
			// unbounded/circular (Up from Attributes wraps around to Profile, the last sub-tab, not
			// clamped) -- the clamped-control "over-shoot is harmless" idiom used elsewhere
			// (GameController.MashBack, an earlier version of this method) does NOT apply here and
			// would actively misnavigate.
			// --- Phase 1: Attributes (name, element, level) for the whole roster ---
			string firstName = null;
			var scanned = new HashSet<string>();

			// True once the loop breaks via wraparound (cursor already sitting back on the first
			// character); false if it breaks via the scan-count cap or a cancel request (cursor
			// sitting one character past the last one scanned, since the roster has more characters
			// than were scanned so no wraparound happened). Drives Phase 2's read direction below.
			bool endedAtFirstCharacter = false;

			// Physical shoulder-tap gap between consecutive recorded characters -- NOT assumed to be
			// 1. Per user (2026-07-05, live-tested): a manequin slot (no constellation page to enter)
			// sitting between recorded characters caused Phase 2 to land on it and press confirm,
			// which closed the whole Character menu -- tracing back to Phase 2/3 assuming 1 shoulder
			// tap always equals 1 recorded character, when a manequin (or a duplicate-name repeat)
			// consumes a tap without being recorded into Characters. gapsBeforeEach[i] is the tap
			// count from Characters[i-1] (or the scan's starting position, for i==0, always 0) to
			// reach Characters[i] -- replayed exactly by ScanRosterForward/ScanRosterBackward below
			// instead of a uniform single-tap assumption.
			var gapsBeforeEach = new List<int>();
			int gapSinceLastRecorded = 0;
			int wrapGap = 0; // taps from the last recorded character back to the first, once wrapped
			int unrecognizedCount = 0; // non-manequin slots whose name/element couldn't be read

			while (true)
			{
				string name = null, element = null;
				string rawRead = ScanNameAndElement(ref name, ref element);

				bool isManequin = name == "Manequin1" || name == "Manequin2";
				bool hasValidNameAndElement = !isManequin && !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(element);

				if (hasValidNameAndElement)
				{
					if (Characters.Count > 0 && name == firstName)
					{
						// Wrapped back to the first character -- stop before advancing again so the
						// cursor is left sitting on it.
						wrapGap = gapSinceLastRecorded;
						endedAtFirstCharacter = true;
						break;
					}

					bool ascended = false;
					int level = ScanLevel(ref ascended);
					if (level == -1)
					{
						progressReporter.AddError($"Could not determine {name}'s level. Setting to 1.");
						level = 1;
						ascended = false;
					}

					if (!scanned.Contains(name))
					{
						var character = new Character
						{
							NameGOOD = name,
							Element = element,
							Level = level,
							Ascended = ascended
						};
						Characters.Add(character);
						gapsBeforeEach.Add(gapSinceLastRecorded);
						gapSinceLastRecorded = 0;
						scanned.Add(name);
						progressReporter.IncrementCharacterCount();
						if (Characters.Count == 1) firstName = name;
						Logger.Info("Scanned {0} attributes successfully (controller)", character.NameGOOD);
						LogCharacterScreenshot(character.NameGOOD, "attributes", NameElementRegion());
					}
					else
					{
						Logger.Info("Prevented {0} duplicate scan (controller)", name);
					}
				}
				else if (!isManequin)
				{
					// Name and element are read from a single combined OCR string and always fail
					// together (ScanNameAndElement clears both on any parse/element-mismatch failure),
					// so this is one combined error, not two -- with the raw OCR text so the user can see
					// what was actually read.
					progressReporter.AddError($"Could not determine character element and name (\"{rawRead}\")");

					// Unrecognized, non-manequin slot: it never gets added to Characters, so it's
					// already skipped by the Constellations (Phase 2) and Talents (Phase 3) passes (both
					// only iterate recorded characters). Save one full-window screenshot so the user can
					// identify who was skipped -- it goes in the top-level characters folder (not a
					// per-character subfolder, since we have no name to file it under). Saved regardless
					// of LogScreenshots: an unrecognized character is a failure worth capturing.
					unrecognizedCount++;
					Logger.Warn("Unrecognized character (raw OCR: \"{0}\") -- skipping all passes and saving a full screenshot.", rawRead);
					Directory.CreateDirectory("./logging/characters");
					using (var screenshot = Navigation.CaptureWindow())
						screenshot.Save($"./logging/characters/unrecognized_{unrecognizedCount}.png");
				}

				controller.TapButton(Xbox360Button.RightShoulder, InventoryScraper.ScaledControllerDelay(80));
				Thread.Sleep(InventoryScraper.ScaledControllerDelay(100));
				gapSinceLastRecorded++;

				if (maxToScan != 0 && Characters.Count >= maxToScan) break;
				if (InventoryKamera.CancelRequested)
				{
					Logger.Info("Stopping character scan: cancel requested");
					break;
				}
			}

			int count = Characters.Count;
			if (count == 0) return;

			// Per user (2026-07-05): a cancel request during Phase 1 was respected (the loop above
			// already checks it), but Phase 2/3 never checked it at all, so a cancel mid-scan
			// silently kept running the full Constellations/Talents passes over every character
			// already recorded instead of stopping. If Phase 1 itself was cancelled, skip straight to
			// the Tartaglia fix on whatever was scanned rather than starting Phase 2/3 at all.
			if (InventoryKamera.CancelRequested)
			{
				ApplyTartagliaFix(Characters);
				ApplySkirkFix(Characters);
				return;
			}

			// Local functions/lambdas below can't capture the `ref` parameter Characters directly
			// (CS1628) -- alias it to a plain local. Same List<Character> instance either way.
			List<Character> characterList = Characters;

			// Gap from the last recorded character to wherever the cursor now sits: back at the
			// first character (full scan, via wrapGap) or however many manequin/duplicate slots past
			// the last one scanned (partial scan or cancel, via whatever accrued since).
			int gapAfterLast = endedAtFirstCharacter ? wrapGap : gapSinceLastRecorded;

			// --- Phase 2: Constellations for the whole roster ---
			// Per user (2026-07-05): the full sub-tab order is Attributes, Weapons, Artifacts,
			// Constellations, Talents, Profile -- Constellations is 3 stops down from Attributes, not
			// 1 (Weapons and Artifacts sit between them).
			controller.Move(GameController.MenuDirection.Down, 3,
				holdMs: InventoryScraper.ScaledControllerDelay(150), settleMs: InventoryScraper.ScaledControllerDelay(150));

			void ScanConstellation(Character character)
			{
				// Verify the roster cursor is actually on this character before pressing B -- on a
				// manequin (no constellation page) B closes the whole Character menu and derails the
				// scan. See VerifyOnExpectedCharacter.
				if (!VerifyOnExpectedCharacter(character, "Constellation")) return;

				// Per user (2026-07-05): greedy (C6-first, read backward) mode only for 4-star
				// characters so far -- see IsFourStarCharacter/ScanConstellationsGreedy.
				character.Constellation = IsFourStarCharacter(character)
					? ScanConstellationsGreedy(controller, character)
					: ScanConstellations(controller, character);
				Logger.Info("{0} Constellation: {1}", character.NameGOOD, character.Constellation);
			}

			// Per user (2026-07-05): no rewind step -- a full scan's cursor is already sitting on the
			// first character (read forward, which returns to the start again via wrapGap, gap-aware
			// rather than assuming count taps), and a partial scan's cursor instead sits gapAfterLast
			// characters past the last one scanned, so reading backward from there reaches the same
			// characters a rewind-then-forward pass would, for far fewer shoulder taps.
			if (endedAtFirstCharacter)
				ScanRosterForward(controller, characterList, gapsBeforeEach, gapAfterLast, ScanConstellation);
			else
				ScanRosterBackward(controller, characterList, gapsBeforeEach, gapAfterLast, ScanConstellation);

			if (InventoryKamera.CancelRequested)
			{
				ApplyTartagliaFix(Characters);
				ApplySkirkFix(Characters);
				return;
			}

			// --- Phase 3: Talents for the whole roster ---
			// Talents is immediately adjacent to Constellations in the sub-tab order, so this stays a
			// single step. Both Phase 2 branches above leave the cursor sitting back on the first
			// character (a full lap for the forward read, or the natural endpoint of the backward
			// read), so this phase can always read forward. No trailing gap needed (null) since this
			// is the last phase -- nothing follows that needs the cursor back at the start.
			controller.Move(GameController.MenuDirection.Down, 1,
				holdMs: InventoryScraper.ScaledControllerDelay(150), settleMs: InventoryScraper.ScaledControllerDelay(150));

			ScanRosterForward(controller, characterList, gapsBeforeEach, null, character =>
			{
				// Same identity check as the constellation pass: confirm the cursor is on this
				// character before reading talents, so a drifted cursor doesn't record a manequin's or
				// the wrong character's talent levels.
				if (!VerifyOnExpectedCharacter(character, "Talent")) return;

				character.Talents = ScanTalents(character);
				Logger.Info("{0} Talents: {1}", character.NameGOOD, "{" + string.Join(", ", character.Talents.Select(kv => kv.Key + "=" + kv.Value).ToArray()) + "}");

				ApplyConstellationTalentScaling(character);
			});

			ApplyTartagliaFix(Characters);
			ApplySkirkFix(Characters);
		}

		/// <summary>
		/// Advances <paramref name="taps"/> times in one direction, one shoulder-button tap at a time
		/// (never a single multi-position jump), matching how <see cref="ScanCharacters"/>'s
		/// Phase 1 measured each gap the same way.
		/// </summary>
		private void AdvanceRoster(GameController controller, Xbox360Button button, int taps)
		{
			for (int t = 0; t < taps; t++)
			{
				controller.TapButton(button, InventoryScraper.ScaledControllerDelay(80));
				Thread.Sleep(InventoryScraper.ScaledControllerDelay(100));
			}
		}

		/// <summary>
		/// Runs <paramref name="scanCharacter"/> once per character in <paramref name="characters"/>,
		/// assuming the cursor is already sitting on the first one, advancing forward (right shoulder)
		/// by the exact physical gap to the next character after each read -- <paramref
		/// name="gapsBeforeEach"/>[i] is the gap consumed to reach <paramref name="characters"/>[i]
		/// (see <see cref="ScanCharacters"/>'s Phase 1 for how it's measured), reused
		/// here to advance from character i to i+1. After the last character, advances by <paramref
		/// name="gapAfterLast"/> if given (used when the following phase also needs the cursor back
		/// at the start), or not at all if null (the last phase needs no such trailing move).
		/// </summary>
		private void ScanRosterForward(GameController controller, List<Character> characters, List<int> gapsBeforeEach, int? gapAfterLast, Action<Character> scanCharacter)
		{
			int count = characters.Count;
			for (int i = 0; i < count; i++)
			{
				scanCharacter(characters[i]);

				// Per user (2026-07-05): a cancel request during Phase 2/3 previously went
				// unchecked, silently finishing the whole roster pass instead of stopping.
				if (InventoryKamera.CancelRequested)
				{
					Logger.Info("Stopping character scan: cancel requested");
					break;
				}

				int? gap = i < count - 1 ? gapsBeforeEach[i + 1] : gapAfterLast;
				if (gap.HasValue) AdvanceRoster(controller, Xbox360Button.RightShoulder, gap.Value);
			}
		}

		/// <summary>
		/// Runs <paramref name="scanCharacter"/> once per character in <paramref name="characters"/>,
		/// last to first, assuming the cursor is sitting <paramref name="gapAfterLast"/> physical
		/// positions past the last character, advancing backward (left shoulder) by the exact gap
		/// before each read -- the mirror image of <see cref="ScanRosterForward"/>, reusing the same
		/// <paramref name="gapsBeforeEach"/> measurements since the physical distance between two
		/// characters doesn't depend on which direction it's crossed. Used for a partial scan, where
		/// reading backward from Phase 1's stopping point reaches the same characters a
		/// rewind-to-start-then-forward pass would, without the wasted rewind taps.
		/// </summary>
		private void ScanRosterBackward(GameController controller, List<Character> characters, List<int> gapsBeforeEach, int gapAfterLast, Action<Character> scanCharacter)
		{
			int count = characters.Count;
			for (int i = count - 1; i >= 0; i--)
			{
				int gap = i == count - 1 ? gapAfterLast : gapsBeforeEach[i + 1];
				AdvanceRoster(controller, Xbox360Button.LeftShoulder, gap);
				scanCharacter(characters[i]);

				// Per user (2026-07-05): a cancel request during Phase 2/3 previously went
				// unchecked, silently finishing the whole roster pass instead of stopping.
				if (InventoryKamera.CancelRequested)
				{
					Logger.Info("Stopping character scan: cancel requested");
					break;
				}
			}
		}

		/// <summary>
		/// Opens Genshin's pause menu and navigates the controller-mode tab bar to the Character
		/// screen (grid position [2,1] in the 4-wide x 5-tall tab grid, per MODERNIZATION_PLAN.md §6c,
		/// 2026-07-05), then confirms with B (Genshin's confirm/select button -- A backs out/cancels,
		/// confirmed 2026-07-05).
		/// Unlike the Inventory sub-tab row (which does remember its last-viewed tab, see
		/// <see cref="InventoryScraper.SwitchToTab"/>), the pause menu's own top-level tab bar resets
		/// to [0,0] every time it's opened fresh from gameplay -- so Move(Right,2) then Move(Down,1)
		/// from [0,0] is always correct here, provided this is entered from the unpaused free-roam
		/// state. The caller guarantees that: characters run in the same single controller session as
		/// the inventory phases (Phase 3 §6c, revised 2026-07-07 -- no more per-phase disconnect), and
		/// <c>InventoryKamera.GatherData</c> explicitly backs out to free-roam (MashBack + a generous
		/// settle wait) before this runs when an inventory phase preceded it. The leading Escape +
		/// MashBack below are a belt-and-suspenders reset on top of that.
		/// </summary>
		private void EnterCharacterMenu(GameController controller)
		{
			Navigation.sim.Keyboard.KeyPress(Navigation.escapeKey);
			Navigation.SystemWait(Navigation.Speed.UI);

			// Safety net (same idiom as GameController.ExitControllerMode): the previous
			// controller-driven phase's teardown already backs out of any nested menu, but
			// EnterControllerMode()'s A-press below assumes a clean free-roam state to avoid
			// triggering an unwanted in-game action instead of a scheme-switch nudge. Over-pressing
			// A here is harmless once already at the root (per MashBack's own doc comment).
			controller.MashBack();

			controller.EnterControllerMode();
			Thread.Sleep(InventoryScraper.ScaledControllerDelay(2000));
			controller.OpenMenu();
			Thread.Sleep(InventoryScraper.ScaledControllerDelay(2000));
			controller.Move(GameController.MenuDirection.Right, 2,
				holdMs: InventoryScraper.ScaledControllerDelay(300), settleMs: InventoryScraper.ScaledControllerDelay(300));
			controller.Move(GameController.MenuDirection.Down, 1,
				holdMs: InventoryScraper.ScaledControllerDelay(300), settleMs: InventoryScraper.ScaledControllerDelay(300));
			Thread.Sleep(InventoryScraper.ScaledControllerDelay(600));
			controller.TapButton(Xbox360Button.B, holdMs: InventoryScraper.ScaledControllerDelay(300));
			Thread.Sleep(Math.Max(3000, InventoryScraper.ScaledControllerDelay(2000)));
		}

		/// <summary>
		/// Re-reads the currently-selected slot's name/element header and returns whether it matches
		/// the character we expect to be scanning. Phases 2 (Constellations) and 3 (Talents) both
		/// replay Phase 1's shoulder-tap gaps to land back on each recorded character while skipping
		/// manequin slots; if that replay drifts even one slot (e.g. a constellation enter/exit not
		/// returning the roster cursor to the exact same position), this catches it before we act on
		/// the wrong slot -- pressing confirm on a manequin closes the whole Character menu, and reading
		/// a wrong slot records another character's constellation/talent data. Returns false (and logs)
		/// on any mismatch or unreadable slot, so the caller skips that character rather than corrupting
		/// it. Uses a small retry budget (fails fast on a wrong slot); the name/element header is shown
		/// on every Character sub-tab, so it reads the same region as Phase 1's Attributes read.
		/// </summary>
		private bool VerifyOnExpectedCharacter(Character character, string phaseLabel)
		{
			string currentName = null, currentElement = null;
			ScanNameAndElement(ref currentName, ref currentElement, maxAttempts: 5);
			if (currentName == character.NameGOOD) return true;

			Logger.Warn("{0} scan expected \"{1}\" but the selected slot reads \"{2}\" -- skipping this character.",
				phaseLabel, character.NameGOOD, currentName ?? "(unreadable)");
			return false;
		}

		/// <summary>
		/// The always-visible name/element header region on the Character screen. Shared by
		/// <see cref="ScanNameAndElement"/>'s OCR read and Phase 1's per-character attributes
		/// screenshot. Measured (2026-07-05) with <c>ui/CoordinatePickerForm.cs</c>.
		/// </summary>
		private static Rectangle NameElementRegion() => new RECT(
			Left:   (int)( 0.0941 * Navigation.GetWidth() ),
			Top:    (int)( 0.0430 * Navigation.GetHeight() ),
			Right:  (int)( 0.2442 * Navigation.GetWidth() ),
			Bottom: (int)( 0.0920 * Navigation.GetHeight() ));

		/// <summary>
		/// Saves a screenshot of just <paramref name="region"/> (the section actually being scanned,
		/// not the whole window) under a per-character logging folder, gated on
		/// <see cref="scanSettings"/>'s LogScreenshots setting -- shared by every controller-path
		/// capture point (<see cref="ScanCharacters"/>'s Attributes read,
		/// <see cref="ScanConstellations"/> per node, <see cref="ScanTalents"/>). <paramref
		/// name="relativeName"/> may contain '/' to nest into a subfolder (e.g.
		/// <c>"constellations/constellation_1"</c>).
		/// </summary>
		private void LogCharacterScreenshot(string characterName, string relativeName, Rectangle region)
		{
			if (!scanSettings.LogScreenshots) return;

			string path = $"./logging/characters/{characterName}/{relativeName}.png";
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			using (var screenshot = Navigation.CaptureRegion(region))
				screenshot.Save(path);
		}

		/// <summary>
		/// Applies the constellation-3/5 talent-level discount (Genshin auto-grants a talent level
		/// via certain constellations, which the raw scanned talent level doesn't reflect). Used by
		/// the controller-driven batched path (<see cref="ScanCharacters"/>), since it
		/// only needs the assembled name/element/constellation/talents, not how they were scanned.
		/// </summary>
		private void ApplyConstellationTalentScaling(Character character)
		{
			// Pyro Traveler's own constellation scan can't be trusted (per user, 2026-07-05): the
			// secondary constellations that grant +3 to a talent are visually embedded in the same 6
			// nodes as the base unlocks, so ScanConstellations can misreport a
			// lower-constellation Pyro Traveler as C6. Per user: default to assuming C0 and infer the
			// real constellation instead from the raw scanned talent levels -- a Skill level of 11+
			// is only reachable if the C3-equivalent +3 Skill bonus is active, and a Burst level of
			// 11+ only if the C5-equivalent +3 Burst bonus is active (unlocks are sequential, so
			// Burst>=11 implies the Skill bonus is active too). This replaces, rather than
			// supplements, the generic scanned-Constellation-based discount below for this character.
			if (character.NameGOOD.Contains("Traveler") && character.Element?.ToLower() == "pyro")
			{
				int constellation = 0;

				if (character.Talents.TryGetValue("skill", out int skillLevel) && skillLevel >= 11)
				{
					constellation = 3;
					character.Talents["skill"] -= 3;
					Logger.Info("{0}: scanned Skill level {1} implies at least C3 -- adjusted to {2}.",
						character.NameGOOD, skillLevel, character.Talents["skill"]);
				}

				if (character.Talents.TryGetValue("burst", out int burstLevel) && burstLevel >= 11)
				{
					constellation = 5;
					character.Talents["burst"] -= 3;
					Logger.Info("{0}: scanned Burst level {1} implies at least C5 -- adjusted to {2}.",
						character.NameGOOD, burstLevel, character.Talents["burst"]);
				}

				Logger.Info("{0} (Pyro): constellation corrected from scanned {1} to inferred {2}.",
					character.NameGOOD, character.Constellation, constellation);
				character.Constellation = constellation;
				return;
			}

			if (character.Constellation < 3) return;

			string lookupKey = character.NameGOOD.Contains("Traveler") ? "traveler" : character.NameGOOD.ToLower();

			if (GenshinProcesor.Characters.TryGetValue(lookupKey, out var characterData))
			{
				if (characterData["ConstellationOrder"] == null)
				{
					// Every character in the database should have this field -- its absence
					// means the character data failed to fully download/parse, not that this
					// character legitimately lacks constellation-order data.
					progressReporter.AddError($"{character.NameGOOD}: missing ConstellationOrder data. Talent levels were not adjusted for constellation bonuses.");
					return;
				}

				string talentLeveledAtConst3 = character.NameGOOD.Contains("Traveler")
					? (string)characterData["ConstellationOrder"][character.Element.ToLower()][0]
					: (string)characterData["ConstellationOrder"][0];
				string talentLeveledAtConst5 = character.NameGOOD.Contains("Traveler")
					? (string)characterData["ConstellationOrder"][character.Element.ToLower()][1]
					: (string)characterData["ConstellationOrder"][1];

				if (character.Constellation >= 3)
				{
					Logger.Info("{0} constellation 3+, adjusting scanned {1} level", character.NameGOOD, talentLeveledAtConst3);
					character.Talents[talentLeveledAtConst3] -= 3;
				}

				if (character.Constellation >= 5)
				{
					Logger.Info("{0} constellation 5+, adjusting scanned {1} level", character.NameGOOD, talentLeveledAtConst5);
					character.Talents[talentLeveledAtConst5] -= 3;
				}
			}
		}

		public static string ScanMainCharacterName(IOcrService ocrService, IImagePreprocessor imagePreprocessor, IScanProgressReporter progressReporter)
		{
			var xReference = 1280.0;
			var yReference = 720.0;
			if (Navigation.GetAspectRatio() == new Size(8, 5))
			{
				yReference = 800.0;
			}

			RECT region = new RECT(
				Left:   (int)(185 / xReference * Navigation.GetWidth()),
				Top:    (int)(26  / yReference * Navigation.GetHeight()),
				Right:  (int)(460 / xReference * Navigation.GetWidth()),
				Bottom: (int)(60  / yReference * Navigation.GetHeight()));

			Bitmap nameBitmap = Navigation.CaptureRegion(region);

			//Image Operations
			GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref nameBitmap);
			imagePreprocessor.SetInvert(ref nameBitmap);
			Bitmap n = imagePreprocessor.ConvertToGrayscale(nameBitmap);

			progressReporter.SetNavigation_Image(nameBitmap);

			string text = ocrService.AnalyzeText(n).Trim();
			if (text != "")
			{
				// Only keep a-Z and 0-9
				text = Regex.Replace(text, @"[\W_]", string.Empty).ToLower();

				// Only keep text up until first space
				text = Regex.Replace(text, @"\s+\w*", string.Empty);

			}
			else
			{
				progressReporter.AddError(text);
			}
			n.Dispose();
			nameBitmap.Dispose();
			return text;
		}

		/// <summary>
		/// Reads the always-visible name/element region on the Attributes sub-tab. Region re-measured
		/// (2026-07-05) with <c>ui/CoordinatePickerForm.cs</c> against a two-line wrapped name.
		/// <paramref name="maxAttempts"/> caps the retry loop -- Phase 1's roster read uses the default
		/// 20 (a genuine character should read within that), while the Phase 2 constellation guard
		/// passes a smaller budget so it fails fast on a manequin/wrong slot instead of burning the full
		/// 20 retries before skipping. Returns the raw OCR text from the final attempt (name and
		/// element are read from one combined OCR string, so callers can surface it in a failure
		/// message to show what was actually read).
		/// </summary>
		private string ScanNameAndElement(ref string name, ref string element, int maxAttempts = 20)
		{
			int attempts = 0; // reduced from 75 per user (2026-07-05) -- 75 retries at Speed.Fast was too slow when parsing genuinely fails
			Rectangle region = NameElementRegion();
			string rawText = "";

			do
			{
				using (Bitmap bm = Navigation.CaptureRegion(region))
				{
					Bitmap n = imagePreprocessor.ConvertToGrayscale(bm);
					imagePreprocessor.SetThreshold(110, ref n);
					imagePreprocessor.SetInvert(ref n);

					n = GenshinProcesor.ResizeImage(n, n.Width * 2, n.Height * 2);
					string block = ocrService.AnalyzeText(n, Tesseract.PageSegMode.Auto).ToLower().Trim();
					string line = ocrService.AnalyzeText(n, Tesseract.PageSegMode.SingleLine).ToLower().Trim();

					// Characters with wrapped names will not have a slash
					string nameAndElement = line.Contains("/") ? line : block;
					rawText = nameAndElement;

					if (nameAndElement.Contains("/"))
					{
						var split = nameAndElement.Split('/');

						// Search for element and character name in block

						// Long name characters might look like
						// <Element>   <First Name>
						// /           <Last Name>
						// Per user (2026-07-05, live screenshot): the wrapped case's first line holds
						// BOTH the element and the first word of the name (e.g. "Anemo Yumemizuki"),
						// which the element-extraction below already isolated correctly, but the name
						// search previously used only the text after "/" ("Mizuki"), silently dropping
						// "Yumemizuki" -- fixed by carrying the leftover first-line text forward into
						// the name search instead of discarding it.
						string namePart1 = "";
						if (!split[0].Contains(" "))
						{
							element = GenshinProcesor.FindElementByName(split[0].Trim());
						}
						else
						{
							var firstLineWords = split[0].Split(new[] { ' ' }, 2);
							element = GenshinProcesor.FindElementByName(firstLineWords[0].Trim());
							if (firstLineWords.Length > 1) namePart1 = firstLineWords[1];
						}

						// Find character based on the leftover first-line text (if any) plus the
						// string after /. Long name characters might search by their last name only
						// but it'll still work in the non-wrapped case (namePart1 stays empty).
						name = GenshinProcesor.FindClosestCharacterName(Regex.Replace(namePart1 + split[1], @"[\W]", string.Empty));

						if (!GenshinProcesor.CharacterMatchesElement(name, element)) { name = ""; element = ""; }
                    }
					n.Dispose();

					if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(element))
					{
						Logger.Debug("Scanned character name as {0} with element {1}", name, element);
                        progressReporter.SetCharacter_NameAndElement(bm, name, element);
						return rawText;
					}
					else
                    {
                        // Per-attempt retry: don't dump a screenshot here -- it used to litter the
                        // top-level ./logging/characters folder with hash-named images (one per failed
                        // attempt, ungated). A single full-window screenshot is saved once by the Phase 1
                        // caller if the character stays unrecognized after all retries (unrecognized_N.png).
                        Logger.Debug("Could not parse character name/element (attempt {0}/{1}). Retrying...", attempts+1, maxAttempts);
                    }
				}
				attempts++;
				Navigation.SystemWait(200f);
			} while ( attempts < maxAttempts );
			name = null;
			element = null;
			return rawText;
		}

		/// <summary>
		/// Reads the level/ascension region on the Attributes sub-tab. Region measured (2026-07-05)
		/// with <c>ui/CoordinatePickerForm.cs</c>.
		/// </summary>
		private int ScanLevel(ref bool ascended)
		{
            int attempt = 0;

			Rectangle region = new RECT(
				Left:   (int)( 0.7626 * Navigation.GetWidth() ),
				Top:    (int)( 0.1895 * Navigation.GetHeight() ),
				Right:  (int)( 0.8835 * Navigation.GetWidth() ),
				Bottom: (int)( 0.2247 * Navigation.GetHeight() ));

			do
			{
				Bitmap bm = Navigation.CaptureRegion(region);

				bm = GenshinProcesor.ResizeImage(bm, bm.Width * 2, bm.Height * 2);
				Bitmap n = imagePreprocessor.ConvertToGrayscale(bm);
				imagePreprocessor.SetInvert(ref n);
				// Bug fix (2026-07-05): contrast was being applied to `bm` (the display-only copy
				// passed to progressReporter.SetCharacter_Level below), not `n` (what's actually fed
				// to Tesseract) -- the contrast boost never reached the OCR input at all, likely
				// contributing to level misreads.
				imagePreprocessor.SetContrast(30.0, ref n);

				string text = ocrService.AnalyzeText(n).Trim();
				Logger.Debug("Scanned character level as {0}", text);

				text = Regex.Replace(text, @"(?![0-9/]).", string.Empty);
				Logger.Debug("Filtered scanned text to {0}", text);
				if (text.Contains("/"))
				{
					var values = text.Split('/');
                    if (int.TryParse(values[0], out int level) && int.TryParse(values[1], out int maxLevel))
                    {
                        maxLevel = (int)Math.Round(maxLevel / 10.0, MidpointRounding.AwayFromZero) * 10;
                        ascended = 20 <= level && level < maxLevel;
                        progressReporter.SetCharacter_Level(bm, level, maxLevel);
                        n.Dispose();
                        bm.Dispose();
                        Logger.Debug("Parsed character level as {0}", level);
                        return level;
                    }
				}
				Logger.Debug("Failed to parse character level and ascension from {0} (text), retrying", text);

				attempt++;

                n.Dispose();
                bm.Dispose();
                Navigation.SystemWait(Navigation.Speed.Fast);
			} while (attempt < 20); // reduced from 50 per user (2026-07-05), matching ScanNameAndElement's cap

			return -1;
		}

		/// <summary>
		/// Controller-mode constellation scan. Per user (2026-07-05, live-tested), the interaction
		/// model here is entirely different from the mouse path's per-node click + color-sample: you
		/// press confirm (B) once to enter the constellation detail view (which opens on the first
		/// constellation already focused), then a single left-stick Down step advances focus to the
		/// next constellation -- the same fixed on-screen region always shows whichever constellation
		/// is currently focused, so there is no per-node capture-region math like the mouse path
		/// needed. Unlock state is read via OCR for the literal word "Activated" (a locked
		/// constellation instead prompts "Activate", per user) rather than the mouse path's
		/// white-background color sample. Exits via cancel (A) once done or once a locked
		/// constellation is found. Region measured (2026-07-05) with <c>ui/CoordinatePickerForm.cs</c>.
		/// </summary>
		private int ScanConstellations(GameController controller, Character character)
		{
			Rectangle activatedRegion = new RECT(
				Left:   (int)( 0.1574 * Navigation.GetWidth() ),
				Top:    (int)( 0.8323 * Navigation.GetHeight() ),
				Right:  (int)( 0.2241 * Navigation.GetWidth() ),
				Bottom: (int)( 0.8777 * Navigation.GetHeight() ));

			int constellation;

			controller.TapButton(Xbox360Button.B, InventoryScraper.ScaledControllerDelay(300));
			Thread.Sleep(InventoryScraper.ScaledControllerDelay(600)); // set to 600 per user (2026-07-05)

			for (constellation = 0; constellation < 6; constellation++)
			{
				if (constellation > 0)
				{
					// Per user (2026-07-05): settleMs already sleeps after the stick releases
					// (GameController.MoveStep), so a separate Thread.Sleep on top of it was a
					// redundant double-wait -- folded into settleMs directly instead.
					controller.MoveStep(GameController.MenuDirection.Down,
						holdMs: InventoryScraper.ScaledControllerDelay(100), settleMs: InventoryScraper.ScaledControllerDelay(400));
				}

				LogCharacterScreenshot(character.NameGOOD, $"constellations/constellation_{constellation + 1}", activatedRegion);

				if (!ReadConstellationActivated(activatedRegion)) break;
			}

			controller.TapButton(Xbox360Button.A, InventoryScraper.ScaledControllerDelay(300));
			Thread.Sleep(InventoryScraper.ScaledControllerDelay(250)); // lowered from 400 per user (2026-07-05)

			progressReporter.SetCharacter_Constellation(constellation);
			return constellation;
		}

		/// <summary>
		/// Reads the currently-focused constellation node's unlock state, retrying up to 3 times
		/// (re-capturing a fresh screenshot each time, not just re-OCRing the same bitmap -- a miss is
		/// more likely the node-move animation not having settled yet than a deterministic misread) --
		/// see <see cref="ScanConstellations"/>'s doc comment history for why. Reads via
		/// OCR for the literal word "Activated" (a locked constellation instead prompts "Activate").
		/// Preprocessing is gamma+invert+grayscale (2026-07-05): a live screenshot showed "Activated"
		/// as bold orange/gold text on a blue-green gradient background, the same pattern that broke
		/// contrast-based preprocessing for the weapon card nameplate (see
		/// WeaponScraper/ScanMainCharacterName's doc comments); gamma correction is the proven fix for
		/// that exact text/background combination in this codebase. Shared by
		/// <see cref="ScanConstellations"/> and <see cref="ScanConstellationsGreedy"/>.
		/// </summary>
		private bool ReadConstellationActivated(Rectangle activatedRegion)
		{
			for (int readAttempt = 0; readAttempt < 3; readAttempt++)
			{
				if (readAttempt > 0) Navigation.SystemWait(200f);

				Bitmap bm = Navigation.CaptureRegion(activatedRegion);
				GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref bm);
				imagePreprocessor.SetInvert(ref bm);
				Bitmap n = imagePreprocessor.ConvertToGrayscale(bm);

				string text = ocrService.AnalyzeText(n, Tesseract.PageSegMode.SingleBlock).Trim();
				bool activated = text.ToLower().Contains("activated");

				n.Dispose();
				bm.Dispose();

				if (activated) return true;
			}
			return false;
		}

		/// <summary>
		/// Greedy variant of <see cref="ScanConstellations"/>: instead of walking C1 to
		/// C6 forward and stopping at the first lock, jumps straight to C6 and reads backward. Per
		/// user (2026-07-05): the constellation list is an unbounded/circular scroll (same as the
		/// Character screen's own sub-tab row), so a single Up move from the C1 focus that confirming
		/// (B) opens on wraps straight to C6 instead of needing 5 Down moves to walk there. Since
		/// unlocks are always sequential (C6 unlocked implies C1-C5 are too), if C6 reads Activated
		/// the whole scan is done in a single read instead of up to six. Per user, only used for
		/// 4-star characters for now (<see cref="IsFourStarCharacter"/>) since those are commonly
		/// fully-conned, making the C6-first check likely to pay off; 5-stars stay on the forward scan
		/// where a full-con check failing fast (locked at C1) is the more common case.
		/// NOT YET LIVE-VERIFIED: the circular-scroll-wraps-in-one-Up-move assumption for the
		/// constellation list specifically (confirmed only for the Character screen's own sub-tab row
		/// so far).
		/// </summary>
		private int ScanConstellationsGreedy(GameController controller, Character character)
		{
			Rectangle activatedRegion = new RECT(
				Left:   (int)( 0.1574 * Navigation.GetWidth() ),
				Top:    (int)( 0.8323 * Navigation.GetHeight() ),
				Right:  (int)( 0.2241 * Navigation.GetWidth() ),
				Bottom: (int)( 0.8777 * Navigation.GetHeight() ));

			controller.TapButton(Xbox360Button.B, InventoryScraper.ScaledControllerDelay(300));
			Thread.Sleep(InventoryScraper.ScaledControllerDelay(600));

			controller.MoveStep(GameController.MenuDirection.Up,
				holdMs: InventoryScraper.ScaledControllerDelay(100), settleMs: InventoryScraper.ScaledControllerDelay(400));

			int constellation = 0;
			for (int node = 5; node >= 0; node--)
			{
				LogCharacterScreenshot(character.NameGOOD, $"constellations/constellation_greedy_{node + 1}", activatedRegion);

				if (ReadConstellationActivated(activatedRegion))
				{
					constellation = node + 1;
					break;
				}

				if (node > 0)
				{
					controller.MoveStep(GameController.MenuDirection.Up,
						holdMs: InventoryScraper.ScaledControllerDelay(100), settleMs: InventoryScraper.ScaledControllerDelay(400));
				}
			}

			controller.TapButton(Xbox360Button.A, InventoryScraper.ScaledControllerDelay(300));
			Thread.Sleep(InventoryScraper.ScaledControllerDelay(250));

			progressReporter.SetCharacter_Constellation(constellation);
			return constellation;
		}

		/// <summary>
		/// Whether <paramref name="character"/>'s database entry has Rarity == 4. Missing Rarity
		/// (e.g. a characters.json predating the 2026-07-05 DatabaseManager change that added it, not
		/// yet refreshed via Update Database) is treated as "unknown" (false) rather than assumed, so
		/// callers fall back to the safe non-greedy scan instead of guessing.
		/// </summary>
		private bool IsFourStarCharacter(Character character)
		{
			string lookupKey = character.NameGOOD.Contains("Traveler") ? "traveler" : character.NameGOOD.ToLower();
			return GenshinProcesor.Characters.TryGetValue(lookupKey, out var data)
				&& data["Rarity"] != null
				&& data["Rarity"].ToObject<int>() == 4;
		}

		/// <summary>
		/// Controller-mode talent scan. Per user (2026-07-05, live-tested), the Talents sub-tab
		/// already displays every talent's level simultaneously as "Lv. XX" rows (plus other
		/// descriptive text sharing the same region) -- no per-icon click/capture loop is needed the
		/// way the mouse path requires. Parses every "Lv. XX" match out of the single captured region,
		/// in on-screen top-to-bottom order, and assigns the first three as auto/skill/burst.
		/// Per user (2026-07-05): the mouse path's Mona/Ayaka movement-talent row skip does not apply
		/// here -- removed after a live test got stuck retrying (found 3 "Lv. XX" rows, wanted 4)
		/// rather than just taking the 3 that were actually there. Retries (matching the mouse path's
		/// per-icon retry loop) until at least 3 "Lv. XX" rows are found. Region measured (2026-07-05)
		/// with <c>ui/CoordinatePickerForm.cs</c>.
		/// </summary>
		private Dictionary<string, int> ScanTalents(Character character)
		{
			var talents = new Dictionary<string, int>
			{
				{ "auto" , -1 },
				{ "skill", -1 },
				{ "burst", -1 }
			};

			Rectangle region = new RECT(
				Left:   (int)( 0.8290 * Navigation.GetWidth() ),
				Top:    (int)( 0.1248 * Navigation.GetHeight() ),
				Right:  (int)( 0.8743 * Navigation.GetWidth() ),
				Bottom: (int)( 0.5687 * Navigation.GetHeight() ));

			const int requiredRows = 3;
			int attempt = 0;

			while (attempt < 20) // reduced from 50 per user (2026-07-05), matching ScanNameAndElement/ScanLevel's cap
			{
				Bitmap bm = Navigation.CaptureRegion(region);
				Bitmap resized = GenshinProcesor.ResizeImage(bm, bm.Width * 2, bm.Height * 2);
				Bitmap n = imagePreprocessor.ConvertToGrayscale(resized);
				imagePreprocessor.SetContrast(60, ref n);
				imagePreprocessor.SetInvert(ref n);

				var lines = ocrService.AnalyzeText(n, Tesseract.PageSegMode.SingleBlock).Trim().Split('\n');

				var levels = new List<int>();
				foreach (var line in lines)
				{
					var match = Regex.Match(line, @"[Ll][Vv]\.?\s*(\d+)");
					if (match.Success && int.TryParse(match.Groups[1].Value, out int lvl) && lvl >= 1 && lvl <= 15)
						levels.Add(lvl);
				}

				if (levels.Count >= requiredRows)
				{
					talents["auto"] = levels[0];
					talents["skill"] = levels[1];
					talents["burst"] = levels[2];

					progressReporter.SetCharacter_Talent(bm, talents["auto"].ToString(), 0);
					progressReporter.SetCharacter_Talent(bm, talents["skill"].ToString(), 1);
					progressReporter.SetCharacter_Talent(bm, talents["burst"].ToString(), 2);
					LogCharacterScreenshot(character.NameGOOD, "talents", region);

					n.Dispose();
					bm.Dispose();
					break;
				}

				Logger.Debug("Controller talent scan: found {0}/{1} \"Lv. XX\" rows, retrying", levels.Count, requiredRows);
				n.Dispose();
				bm.Dispose();
				attempt++;
				Navigation.SystemWait(Navigation.Speed.Fast);
			}

			return talents;
		}
	}
}