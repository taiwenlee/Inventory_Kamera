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

		public void ScanCharacters(ref List<Character> Characters)
		{
			int viewed = 0;
			int counter = 0;
			string first = null;
			HashSet<string> scanned = new HashSet<string>();

			if (NumOfCharToScan != 0) progressReporter.SetCharacter_Max(NumOfCharToScan);

			progressReporter.ResetCharacterDisplay();

			while (true)
			{
				var character = ScanCharacter(first);
				if(character.NameGOOD != "manequin")
				{
                    if (Characters.Count > 0 && character.NameGOOD == Characters.ElementAt(0).NameGOOD) break;
                    if (character.IsValid())
                    {
                        if (!scanned.Contains(character.NameGOOD))
                        {
                            Characters.Add(character);
                            progressReporter.IncrementCharacterCount();
                            counter++;
                            Logger.Info("Scanned {0} successfully", character.NameGOOD);
                            if (Characters.Count == 1) first = character.NameGOOD;
                        }
                        else
                        {
                            Logger.Info("Prevented {0} duplicate scan", character.NameGOOD);
                        }
                    }
                    else
                    {
                        string error = "";
                        if (!character.HasValidName()) error += "Invalid character name\n";
                        if (!character.HasValidLevel()) error += "Invalid level\n";
                        if (!character.HasValidElement()) error += "Invalid element\n";
                        if (!character.HasValidConstellation()) error += "Invalid constellation\n";
                        if (!character.HasValidTalents()) error += "Invalid talents\n";
                        Logger.Error("Failed to scan character\n" + error + character);
                    }
                }

                Navigation.SelectNextCharacter();
				progressReporter.ResetCharacterDisplay();

				if ((++viewed > 3 && Characters.Count < 1) || (NumOfCharToScan !=0 && counter >= NumOfCharToScan)) break;
				if (InventoryKamera.CancelRequested)
				{
					Logger.Info("Stopping character scan: cancel requested");
					break;
				}
			}

			ApplyTartagliaFix(Characters);
		}

		/// <summary>
		/// Childe (Tartaglia) passive buff fix -- his kit grants +1 auto-attack talent level to the
		/// first 4 party members once he's Ascension 4+, which the raw scanned talent level doesn't
		/// reflect. Shared by the mouse path (<see cref="ScanCharacters"/>) and the controller-driven
		/// batched path (<see cref="ScanCharactersViaController"/>) since it only needs the final
		/// assembled roster, not how it was scanned.
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
		/// Controller-driven character scan (Phase 3 §6c), batched by sub-tab across the whole roster
		/// instead of per-character. Per user design decision (2026-07-05): switching the character
		/// screen's sub-tab (left-stick up/down; full order is Attributes, Weapons, Artifacts,
		/// Constellations, Talents, Profile) pays a real UI transition-animation cost, while advancing
		/// between characters (right shoulder button, mirroring how
		/// <see cref="InventoryScraper.SwitchToTabViaController"/> cycles inventory tabs with LB/RB)
		/// does not. Interleaving sub-tabs per character would pay that animation cost repeatedly per
		/// character; this instead pays it exactly twice for the whole scan (Attributes to
		/// Constellations, a 3-step move past Weapons/Artifacts; Constellations to Talents, 1 step),
		/// independent of roster size -- at the cost of doing 3 full passes over the roster instead of
		/// 1. That trade favors batching since shoulder-tap advance is the cheap, already-fast-tuned
		/// primitive elsewhere (see <see cref="WeaponScraper.ScanWeaponsViaController"/>). Weapons and
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
		/// method (<see cref="ScanConstellationsViaController"/>/<see cref="ScanTalentsViaController"/>)
		/// rather than reusing the mouse path's per-node click loop.
		/// <paramref name="Characters"/> is scanned up to <see cref="NumOfCharToScan"/> entries (0 =
		/// whole roster), same partial-scan behavior as <see cref="ScanCharacters"/>.
		/// NOT YET LIVE-VERIFIED: <see cref="EnterCharacterMenuViaController"/>'s pause-menu tab
		/// position/timing, and whether the sub-tab step counts above are exactly right.
		/// </summary>
		public void ScanCharactersViaController(GameController controller, ref List<Character> Characters)
		{
			int maxToScan = NumOfCharToScan;
			if (maxToScan != 0) progressReporter.SetCharacter_Max(maxToScan);
			progressReporter.ResetCharacterDisplay();

			EnterCharacterMenuViaController(controller);

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

			while (true)
			{
				string name = null, element = null;
				ScanNameAndElement(ref name, ref element, viaController: true);

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
					int level = ScanLevel(ref ascended, viaController: true);
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
						LogCharacterScreenshot(character.NameGOOD, "attributes");
					}
					else
					{
						Logger.Info("Prevented {0} duplicate scan (controller)", name);
					}
				}
				else if (!isManequin)
				{
					if (string.IsNullOrWhiteSpace(name)) progressReporter.AddError("Could not determine character's name");
					if (string.IsNullOrWhiteSpace(element)) progressReporter.AddError("Could not determine character's element");
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
				// Per user (2026-07-05): greedy (C6-first, read backward) mode only for 4-star
				// characters so far -- see IsFourStarCharacter/ScanConstellationsGreedyViaController.
				character.Constellation = IsFourStarCharacter(character)
					? ScanConstellationsGreedyViaController(controller, character)
					: ScanConstellationsViaController(controller, character);
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
				character.Talents = ScanTalentsViaController(character);
				Logger.Info("{0} Talents: {1}", character.NameGOOD, "{" + string.Join(", ", character.Talents.Select(kv => kv.Key + "=" + kv.Value).ToArray()) + "}");

				ApplyConstellationTalentScaling(character);
			});

			ApplyTartagliaFix(Characters);
		}

		/// <summary>
		/// Advances <paramref name="taps"/> times in one direction, one shoulder-button tap at a time
		/// (never a single multi-position jump), matching how <see cref="ScanCharactersViaController"/>'s
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
		/// (see <see cref="ScanCharactersViaController"/>'s Phase 1 for how it's measured), reused
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
		/// screen (grid position [2,1], live-verified reachable via Move(Right,2)+Move(Down,1) per
		/// MODERNIZATION_PLAN.md §6c, 2026-07-05), then confirms with B (Genshin's confirm/select
		/// button -- A backs out/cancels, confirmed 2026-07-05). Mirrors
		/// <see cref="InventoryScraper.EnterInventoryViaController"/>'s structure/timing reasoning for
		/// the same pause-menu tab bar, just a different destination tab.
		/// NOT YET LIVE-VERIFIED.
		/// </summary>
		private void EnterCharacterMenuViaController(GameController controller)
		{
			Navigation.sim.Keyboard.KeyPress(Navigation.escapeKey);
			Navigation.SystemWait(Navigation.Speed.UI);

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

		private Character ScanCharacter(string firstCharacter)
		{
			var character = new Character();
			Navigation.SelectCharacterAttributes();
			string name = null;
			string element = null;

			// Scan the Name and element of Character. Attempt 75 times max.
			ScanNameAndElement(ref name, ref element);

			if(name == "Manequin1" || name == "Manequin2")
			{
                character.NameGOOD = name;
				return character;
            }

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(element))
			{
				if (string.IsNullOrWhiteSpace(name)) progressReporter.AddError("Could not determine character's name");
				if (string.IsNullOrWhiteSpace(element)) progressReporter.AddError("Could not determine character's element");
				return character;
			}

			character.NameGOOD = name;
			character.Element = element;

			// Check if character was first scanned
			if (character.NameGOOD != firstCharacter)
			{
				bool ascended = false;
				// Scan Level and ascension
				int level = ScanLevel(ref ascended);
				if (level == -1)
				{
					progressReporter.AddError($"Could not determine {character.NameGOOD}'s level. Setting to 1.");
					level = 1;
					ascended = false;
				}
				character.Level = level;
				character.Ascended = ascended;

				Logger.Info("{0} Level: {1}", character.NameGOOD, character.Level);
				Logger.Info("{0} Ascended: {1}", character.NameGOOD, character.Ascended);

				// Scan Experience
				//experience = ScanExperience();
				//Navigation.SystemRandomWait(Navigation.Speed.Normal);

				// Scan Constellation
				Navigation.SelectCharacterConstellation();
				character.Constellation = ScanConstellations(character);
				Logger.Info("{0} Constellation: {1}", character.NameGOOD, character.Constellation);
				Navigation.SystemWait(Navigation.Speed.Normal);

				// Scan Talents
				Navigation.SelectCharacterTalents();
				character.Talents = ScanTalents(character);
				Logger.Info("{0} Talents: {1}", character.NameGOOD, "{" + string.Join(", ", character.Talents.Select(kv => kv.Key + "=" + kv.Value).ToArray()) + "}");
				Navigation.SystemWait(Navigation.Speed.Normal);

				// Scale down talents due to constellations
				ApplyConstellationTalentScaling(character);

				return character;
			}
			Logger.Info("Repeat character {0} detected. Finishing character scan...", name);
			return character;
		}

		/// <summary>
		/// Saves a full-window screenshot under a per-character logging folder, gated on
		/// <see cref="scanSettings"/>'s LogScreenshots setting -- shared by every controller-path
		/// capture point (<see cref="ScanCharactersViaController"/>'s Attributes read,
		/// <see cref="ScanConstellationsViaController"/> per node, <see cref="ScanTalentsViaController"/>)
		/// so debugging a bad capture region has a full-page reference image alongside the small OCR
		/// crop, per character, per step.
		/// </summary>
		private void LogCharacterScreenshot(string characterName, string fileName)
		{
			if (!scanSettings.LogScreenshots) return;

			Directory.CreateDirectory($"./logging/characters/{characterName}");
			using (var screenshot = Navigation.CaptureWindow())
				screenshot.Save($"./logging/characters/{characterName}/{fileName}.png");
		}

		/// <summary>
		/// Applies the constellation-3/5 talent-level discount (Genshin auto-grants a talent level
		/// via certain constellations, which the raw scanned talent level doesn't reflect). Shared by
		/// the mouse path (<see cref="ScanCharacter"/>) and the controller-driven batched path
		/// (<see cref="ScanCharactersViaController"/>), since it only needs the assembled
		/// name/element/constellation/talents, not how they were scanned.
		/// </summary>
		private void ApplyConstellationTalentScaling(Character character)
		{
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
		/// Reads the always-visible name/element region on the Attributes sub-tab.
		/// <paramref name="viaController"/> selects the controller-mode capture region -- per user
		/// (2026-07-05, live-tested): controller mode's Character screen layout differs from mouse
		/// mode's ("everything shifts"), the same reason WeaponScraper needed its own
		/// coordinate-picker-measured card region instead of reusing the mouse-popup one.
		/// Controller-mode region measured (2026-07-05) with <c>ui/CoordinatePickerForm.cs</c>.
		/// </summary>
		private void ScanNameAndElement(ref string name, ref string element, bool viaController = false)
		{
			int attempts = 0;
			int maxAttempts = 20; // reduced from 75 per user (2026-07-05) -- 75 retries at Speed.Fast was too slow when parsing genuinely fails
			Rectangle region = viaController
				? new RECT( // re-measured (2026-07-05) against a two-line wrapped name
					Left:   (int)( 0.0941 * Navigation.GetWidth() ),
					Top:    (int)( 0.0430 * Navigation.GetHeight() ),
					Right:  (int)( 0.2442 * Navigation.GetWidth() ),
					Bottom: (int)( 0.0920 * Navigation.GetHeight() ))
				: new RECT(
					Left:   (int)( 85  / 1280.0 * Navigation.GetWidth() ),
					Top:    (int)( 10  / 720.0 * Navigation.GetHeight() ),
					Right:  (int)( 305 / 1280.0 * Navigation.GetWidth() ),
					Bottom: (int)( 55  / 720.0 * Navigation.GetHeight() ));

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
						return;
					}
					else
                    {
                        Logger.Debug("Could not parse character name/element (Atempt {0}/{1}). Retrying...", attempts+1, maxAttempts);
                        bm.Save($"./logging/characters/{bm.GetHashCode()}.png");
                    }
				}
				attempts++;
				Navigation.SystemWait(200f);
			} while ( attempts < maxAttempts );
			name = null;
			element = null;
		}

		/// <summary>
		/// Reads the level/ascension region on the Attributes sub-tab. <paramref name="viaController"/>
		/// selects the controller-mode capture region -- see <see cref="ScanNameAndElement"/>'s doc
		/// comment for why these differ. Controller-mode region measured (2026-07-05) with
		/// <c>ui/CoordinatePickerForm.cs</c>.
		/// </summary>
		private int ScanLevel(ref bool ascended, bool viaController = false)
		{
            int attempt = 0;

            var xRef = 1280.0;
			var yRef = 720.0;
			if (Navigation.GetAspectRatio() == new Size(8, 5))
			{
				yRef = 800.0;
			}

			Rectangle region = viaController
				? new RECT(
					Left:   (int)( 0.7626 * Navigation.GetWidth() ),
					Top:    (int)( 0.1895 * Navigation.GetHeight() ),
					Right:  (int)( 0.8835 * Navigation.GetWidth() ),
					Bottom: (int)( 0.2247 * Navigation.GetHeight() ))
				: new RECT(
					Left:   (int)( 960  / xRef * Navigation.GetWidth() ),
					Top:    (int)( 135  / yRef * Navigation.GetHeight() ),
					Right:  (int)( 1125 / xRef * Navigation.GetWidth() ),
					Bottom: (int)( 163  / yRef * Navigation.GetHeight() ));

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

		private int ScanExperience()
		{
			int experience = 0;

			int xOffset = 1117;
			int yOffset = 151;
			Bitmap bm = new Bitmap(90, 10);
			Graphics g = Graphics.FromImage(bm);
			int screenLocation_X = Navigation.GetPosition().Left + xOffset;
			int screenLocation_Y = Navigation.GetPosition().Top + yOffset;
			g.CopyFromScreen(screenLocation_X, screenLocation_Y, 0, 0, bm.Size);

			//Image Operations
			bm = GenshinProcesor.ResizeImage(bm, bm.Width * 6, bm.Height * 6);
			//Scraper.ConvertToGrayscale(ref bm);
			//Scraper.SetInvert(ref bm);
			imagePreprocessor.SetContrast(30.0, ref bm);

			string text = ocrService.AnalyzeText(bm);
			text = text.Trim();
			text = Regex.Replace(text, @"(?![0-9\s/]).", string.Empty);

			if (Regex.IsMatch(text, "/"))
			{
				string[] temp = text.Split('/');
				experience = Convert.ToInt32(temp[0]);
			}
			else
			{
				Debug.Print("Error: Found " + experience + " instead of experience");
				progressReporter.AddError("Found " + experience + " instead of experience");
			}

			return experience;
		}

		/// <summary>
		/// Clicks through each constellation node and checks its unlock state. Constellation-node
		/// selection stays mouse-driven; the controller path is a separate method,
		/// <see cref="ScanConstellationsViaController"/>, since it navigates with button presses
		/// instead of mouse clicks.
		/// </summary>
		private int ScanConstellations(Character character)
		{
			double yReference = 720.0;
			int constellation;

			if (Navigation.GetAspectRatio() == new Size(8, 5))
			{
				yReference = 800.0;
			}

			Rectangle constActivate = new RECT(
				Left:   (int)( 70 / 1280.0 * Navigation.GetWidth() ),
				Top:    (int)( 665 / 720.0 * Navigation.GetHeight() ),
				Right:  (int)( 100 / 1280.0 * Navigation.GetWidth() ),
				Bottom: (int)( 695 / 720.0 * Navigation.GetHeight() ));

			for (constellation = 0; constellation < 6; constellation++)
			{
				// Select Constellation
				int yOffset = (int)( ( 180 + ( constellation * 75 ) ) / yReference * Navigation.GetHeight() );

				if (Navigation.GetAspectRatio() == new Size(8, 5))
				{
					yOffset = (int)( ( 225 + ( constellation * 75 ) ) / yReference * Navigation.GetHeight() );
				}

				Navigation.SetCursor((int)( 1130 / 1280.0 * Navigation.GetWidth() ), yOffset);
				Navigation.Click();

				var pause = constellation == 0 ? 700 : 550;
				Navigation.SystemWait(pause);

				if (scanSettings.LogScreenshots)
				{
					var screenshot = Navigation.CaptureWindow();
					Directory.CreateDirectory($"./logging/characters/{character.NameGOOD}");
					screenshot.Save($"./logging/characters/{character.NameGOOD}/constellation_{constellation + 1}.png");
				}

				// Grab Color
				using (Bitmap region = Navigation.CaptureRegion(constActivate))
				{
					// Check a small region next to the text "Activate"
					// for a mostly white backround
					var statistics = imagePreprocessor.AverageColor(region);
					if (statistics.R >= 190 && statistics.G >= 190 && statistics.B >= 190)
						break;

				}
			}

			Navigation.sim.Keyboard.KeyPress(WindowsInput.Native.VirtualKeyCode.ESCAPE);
			progressReporter.SetCharacter_Constellation(constellation);
			return constellation;
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
		private int ScanConstellationsViaController(GameController controller, Character character)
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

				LogCharacterScreenshot(character.NameGOOD, $"constellation_{constellation + 1}");

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
		/// see <see cref="ScanConstellationsViaController"/>'s doc comment history for why. Reads via
		/// OCR for the literal word "Activated" (a locked constellation instead prompts "Activate").
		/// Preprocessing is gamma+invert+grayscale (2026-07-05): a live screenshot showed "Activated"
		/// as bold orange/gold text on a blue-green gradient background, the same pattern that broke
		/// contrast-based preprocessing for the weapon card nameplate (see
		/// WeaponScraper/ScanMainCharacterName's doc comments); gamma correction is the proven fix for
		/// that exact text/background combination in this codebase. Shared by
		/// <see cref="ScanConstellationsViaController"/> and <see cref="ScanConstellationsGreedyViaController"/>.
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
		/// Greedy variant of <see cref="ScanConstellationsViaController"/>: instead of walking C1 to
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
		private int ScanConstellationsGreedyViaController(GameController controller, Character character)
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
				LogCharacterScreenshot(character.NameGOOD, $"constellation_greedy_{node + 1}");

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
		/// Clicks through each talent icon and reads its level. The controller path is a separate
		/// method, <see cref="ScanTalentsViaController"/>, since it reads all three talent levels from
		/// a single capture instead of clicking through icons one at a time.
		/// </summary>
		private Dictionary<string, int> ScanTalents(Character character)
		{
			var talents = new Dictionary<string, int>
			{
				{ "auto" , -1 },
				{ "skill", -1 },
				{ "burst", -1 }
			};

			int specialOffset = 0;

			// Check if character has a movement talent like
			// Mona or Ayaka
			if (character.NameGOOD.Contains("Mona") || character.NameGOOD.Contains("Ayaka")) specialOffset = 1;

			var xRef = 1280.0;
			var yRef = 720.0;

			if (Navigation.GetAspectRatio() == new Size(8, 5)) yRef = 800.0;

			Rectangle region = new Rectangle(
				x:		(int)((Navigation.IsNormal ? 0.0003 : 0.0003) * Navigation.GetWidth() ),
				y:		(int)((Navigation.IsNormal ? 0.1278 : 0.1078) * Navigation.GetHeight() ),
				width:	(int)((Navigation.IsNormal ? 0.2913 : 0.2913) * Navigation.GetWidth() ),
				height:	(int)((Navigation.IsNormal ? 0.0711 : 0.0711) * Navigation.GetHeight() ));

			for (int i = 0; i < 3; i++)
			{
				string talent;
				// Change y-offset for talent clicking
				int yOffset = (int)( 110 / yRef * Navigation.GetHeight() ) + ( i + ( ( i == 2 ) ? specialOffset : 0 ) ) * (int)(60 / yRef * Navigation.GetHeight() );

				Navigation.SetCursor((int)(1130 / xRef * Navigation.GetWidth()), yOffset);
				Navigation.Click();
				int pause = i == 0 ? 700 : 550;
				Navigation.SystemWait(pause);
                switch (i)
                {
					default:
						talent = "auto";
						break;
					case 1:
						talent = "skill";
						break;
					case 2:
						talent = "burst";
						break;
                }

                while (talents[talent] < 1 || talents[talent] > 15)
				{
					Bitmap talentLevel = Navigation.CaptureRegion(region);

					talentLevel = GenshinProcesor.ResizeImage(talentLevel, talentLevel.Width * 2, talentLevel.Height * 2);

					Bitmap n = imagePreprocessor.ConvertToGrayscale(talentLevel);
					imagePreprocessor.SetContrast(60, ref n);
					imagePreprocessor.SetInvert(ref n);

					var text = ocrService.AnalyzeText(n, Tesseract.PageSegMode.SingleBlock).Trim().Split('\n').ToList();

					if (int.TryParse(Regex.Replace(text.Last(), @"\D", string.Empty), out int level))
					{
						if (level >= 1 && level <= 15)
						{
							talents[talent] = level;
							progressReporter.SetCharacter_Talent(talentLevel, level.ToString(), i);
						}
					}

					n.Dispose();
					talentLevel.Dispose();
				}
			}

			Navigation.sim.Keyboard.KeyPress(WindowsInput.Native.VirtualKeyCode.ESCAPE);
			return talents;
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
		private Dictionary<string, int> ScanTalentsViaController(Character character)
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
					LogCharacterScreenshot(character.NameGOOD, "talents");

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