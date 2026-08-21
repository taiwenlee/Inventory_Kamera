using Accord.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace InventoryKamera
{
    internal class CharacterScraper
	{
		private static NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		protected int NumOfCharToScan;

        public CharacterScraper()
		{
			NumOfCharToScan = Properties.Settings.Default.NumOfCharToScan;
		}

		// ---------------------------------------------------------------------------
		// Roster identity vs. Export identity
		// ---------------------------------------------------------------------------
		// GetRosterKey() returns a STABLE string used ONLY for wrap-around detection.
		// For Traveler / Wanderer / Manekin the element is intentionally excluded so
		// that OCR element noise (null, wrong value) does not cause a missed wrap.
		//
		// The GOOD export key (Character.NameGOOD) is computed separately by the
		// Character object itself (e.g. "TravelerPyro") and is never affected here.
		//
		// Test cases (verified as comments, not runtime assertions):
		//   GetRosterKey("Traveler", "pyro",   ...)  => "traveler"   // Case 1
		//   GetRosterKey("Traveler", null,     ...)  => "traveler"   // Case 2
		//   GetRosterKey("Traveler", "hydro",  ...)  => "traveler"   // Case 2 alt
		//   GetRosterKey("Wanderer",  "anemo", ...)  => "wanderer"   // Case 3 (differs from Traveler key)
		//   GetRosterKey("manequin1", null,    ...)  => "manequin1"  // Case 4a
		//   GetRosterKey("manequin2", null,    ...)  => "manequin2"  // Case 4b
		//   GetRosterKey("Amber",     "pyro",  ...)  => "amber|pyro" // Case 5
		//   GetRosterKey("Fischl",    "electro",...)  => "fischl|electro" // Case 6 (differs from Amber)
		/// <summary>
		/// Returns an element-agnostic roster key for special characters
		/// (Traveler, Wanderer, Manekin) so that OCR element failures do not
		/// prevent correct wrap-around detection.  Ordinary characters include
		/// their canonical key and element so two characters of the same element
		/// are never confused.
		/// </summary>
		/// <param name="nameGood">The resolved name / canonical key (case-insensitive).</param>
		/// <param name="element">The OCR-detected element; may be null.</param>
		/// <param name="travelerAlias">Lower-cased configured TravelerName.</param>
		/// <param name="wandererAlias">Lower-cased configured WandererName.</param>
		/// <param name="maleManekinAlias">Lower-cased Manequin1Name.</param>
		/// <param name="femaleManekinAlias">Lower-cased Manequin2Name.</param>
		internal static string GetRosterKey(
			string nameGood,
			string element,
			string travelerAlias,
			string wandererAlias,
			string maleManekinAlias,
			string femaleManekinAlias)
		{
			if (string.IsNullOrWhiteSpace(nameGood)) return string.Empty;

			string nameLower = nameGood.Trim().ToLowerInvariant();
			string travelerLower = travelerAlias?.Trim().ToLowerInvariant() ?? "";
			string wandererLower = wandererAlias?.Trim().ToLowerInvariant() ?? "";
			string maleManekinLower = maleManekinAlias?.Trim().ToLowerInvariant() ?? "";
			string femaleManekinLower = femaleManekinAlias?.Trim().ToLowerInvariant() ?? "";

			// Traveler: any element suffix → same roster slot
			if (nameLower == "traveler"
				|| nameLower.StartsWith("traveler")
				|| (!string.IsNullOrWhiteSpace(travelerLower) && nameLower == travelerLower))
				return "traveler";

			// Wanderer: element-agnostic (always Anemo but guard against OCR noise)
			if (nameLower == "wanderer"
				|| (!string.IsNullOrWhiteSpace(wandererLower) && nameLower == wandererLower))
				return "wanderer";

			// Manekin: two distinct roster slots, no element required
			if (nameLower == "manequin1"
				|| (!string.IsNullOrWhiteSpace(maleManekinLower) && nameLower == maleManekinLower))
				return "manequin1";

			if (nameLower == "manequin2"
				|| (!string.IsNullOrWhiteSpace(femaleManekinLower) && nameLower == femaleManekinLower))
				return "manequin2";

			// Ordinary character: just canonical name
			return nameLower;
		}

		public void ScanCharacters(ref List<Character> Characters)
		{
			int viewed    = 0;
			int counter   = 0;
			HashSet<string> scanned = new HashSet<string>();

			// Resolve all configured aliases once
			string travelerAlias      = Properties.Settings.Default.TravelerName;
			string wandererAlias      = Properties.Settings.Default.WandererName;
			string maleManekinAlias   = Properties.Settings.Default.Manequin1Name;
			string femaleManekinAlias = Properties.Settings.Default.Manequin2Name;

			UserInterface.ResetCharacterDisplay();

			// ---------------------------------------------------------------
			// STEP 1a: Initialize first roster key with 2-out-of-3 consensus
			// ---------------------------------------------------------------
			string firstRosterKey = null;
			List<string> probedKeys = new List<string>();

			for (int i = 0; i < 3; i++)
			{
				string pName = null, pElement = null;
				ScanNameAndElement(ref pName, ref pElement, travelerAlias, wandererAlias, maleManekinAlias, femaleManekinAlias);
				string key = GetRosterKey(pName, pElement, travelerAlias, wandererAlias, maleManekinAlias, femaleManekinAlias);

				probedKeys.Add(string.IsNullOrEmpty(key) ? "empty" : key);

				if (!string.IsNullOrEmpty(key) && probedKeys.Count(k => k == key) >= 2)
				{
					firstRosterKey = key;
					break;
				}
				Logger.Warn("[ScanLoop] First slot consensus not yet reached (Attempt {0}/3).", i + 1);
			}

			Logger.Info("[ScanLoop] First slot probes: [{0}] -> consensus key: {1}",
				string.Join(", ", probedKeys), firstRosterKey ?? "none");

			if (string.IsNullOrEmpty(firstRosterKey))
			{
				Logger.Error("[ScanLoop] Aborting character scan: could not establish a 2-out-of-3 consensus for the first character.");
				UserInterface.AddError("Character scan aborted: could not identify the first character.");
				return;
			}

			while (true)
			{
				Logger.Debug("[ScanLoop] scan_index={0} first_roster_key={1}",
					counter, firstRosterKey);

				// ---------------------------------------------------------------
				// STEP 1b: Fast name probe for current slot
				// ---------------------------------------------------------------
				string probeName    = null;
				string probeElement = null;
				ScanNameAndElement(ref probeName, ref probeElement,
					travelerAlias, wandererAlias, maleManekinAlias, femaleManekinAlias);

				Logger.Debug("[ScanLoop] Probe: name='{0}' element='{1}'",
					probeName ?? "<null>", probeElement ?? "<null>");

				string currentRosterKey = GetRosterKey(
					probeName, probeElement,
					travelerAlias, wandererAlias,
					maleManekinAlias, femaleManekinAlias);

				// Record the very first roster slot before any detail scan (already done, but keeping currentRosterKey check robust)

				// ---------------------------------------------------------------
				// STEP 2: Wrap-around check
				// ---------------------------------------------------------------
				if (NumOfCharToScan == 0
					&& counter > 0
					&& firstRosterKey != null
					&& string.Equals(currentRosterKey, firstRosterKey, StringComparison.OrdinalIgnoreCase))
				{
					Logger.Info("[ScanLoop] Wrap-around detected: current roster key '{0}' matches first '{1}'. " +
						"Stopping before detail scan, export, or Next.",
						currentRosterKey, firstRosterKey);
					break;
				}

				// ---------------------------------------------------------------
				// STEP 3: Full detail scan for this character slot.
				// We already know name/element from the probe; pass them in so
				// ScanCharacter does not need to re-OCR the name region.
				// ---------------------------------------------------------------
				var character = ScanCharacterDetails(
					probeName, probeElement,
					maleManekinAlias, femaleManekinAlias);

				Logger.Debug("[ScanLoop] Detail result: key='{0}' element='{1}'",
					character.NameGOOD ?? "<null>", character.Element ?? "<null>");

				bool isManekin = (character.NameGOOD == "manequin1" || character.NameGOOD == "manequin2");

				if (isManekin)
				{
					// Manekin: counted and logged, but never exported to GOOD JSON.
					if (!scanned.Contains(character.NameGOOD))
					{
						scanned.Add(character.NameGOOD);
						counter++;
						Logger.Info("[ScanLoop] Manekin '{0}' logged but omitted from GOOD export (scan_index={1}). " +
							"GOOD format has no canonical key for Manekin characters.",
							character.NameGOOD, counter);
					}
					else
					{
						Logger.Info("[ScanLoop] Prevented duplicate Manekin scan of '{0}'", character.NameGOOD);
					}
				}
				else if (character.IsValid())
				{
					if (!scanned.Contains(character.NameGOOD))
					{
						Characters.Add(character);
						scanned.Add(character.NameGOOD);
						UserInterface.IncrementCharacterCount();
						counter++;
						Logger.Info("[ScanLoop] Scanned '{0}' successfully (scan_index={1})",
							character.NameGOOD, counter);
					}
					else
					{
						Logger.Info("[ScanLoop] Prevented duplicate scan of '{0}'", character.NameGOOD);
					}
				}
				else
				{
					string error = "";
					if (!character.HasValidName())         error += "Invalid character name\n";
					if (!character.HasValidLevel())        error += "Invalid level\n";
					if (!character.HasValidElement())      error += "Invalid element\n";
					if (!character.HasValidConstellation()) error += "Invalid constellation\n";
					if (!character.HasValidTalents())      error += "Invalid talents\n";
					Logger.Error("[ScanLoop] Failed to scan character\n" + error + character);
				}

				// Safety exit for exact count met.
				if (NumOfCharToScan != 0 && counter >= NumOfCharToScan)
				{
					Logger.Info("[ScanLoop] Ending scan: exact count reached ({0})", NumOfCharToScan);
					break;
				}

				// ---------------------------------------------------------------
				// STEP 4: Advance to the next roster slot.
				// ---------------------------------------------------------------
				Navigation.SelectNextCharacter();
				UserInterface.ResetCharacterDisplay();

				// Safety exit: if nothing resolved after several views.
				if (++viewed > 3 && Characters.Count < 1 && counter < 1)
				{
					Logger.Info("[ScanLoop] Ending scan: viewed={0} resolved={1} exported={2}",
						viewed, counter, Characters.Count);
					break;
				}
			}

			// Childe passive buff fix
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
							Logger.Info("Applied Tartaglia auto attack fix to {0} at position {1}.",
								Characters[j].NameGOOD, j);
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
		/// Performs the full detail scan (level, constellation, talents) for the
		/// character whose name and element have already been resolved by the
		/// fast probe in ScanCharacters.  Accepts pre-resolved name/element so the
		/// OCR name region is not re-read.
		/// </summary>
		private static Character ScanCharacterDetails(
			string probeName, string probeElement,
			string maleManekinAlias, string femaleManekinAlias)
		{
			var character = new Character();

			// Use probe results directly — do not re-scan the name region.
			string name    = probeName;
			string element = probeElement;

			Logger.Debug("[ScanCharacterDetails] name='{0}' element='{1}'",
				name ?? "<null>", element ?? "<null>");

			// Manekin: return a stub; GOOD export is skipped by the caller.
			if (name == "Manequin1" || name == "Manequin2")
			{
				character.NameGOOD = name.ToLower();
				character.Element  = element ?? "";
				return character;
			}

			if (string.IsNullOrWhiteSpace(name))
			{
				UserInterface.AddError("Could not determine character's name");
				if (string.IsNullOrWhiteSpace(element))
					UserInterface.AddError("Could not determine character's element");
				return character;
			}

			Navigation.SelectCharacterAttributes();

			character.NameGOOD = name;
			character.Element  = element;

			// Scan Level and ascension
			bool ascended = false;
			int level = ScanLevel(ref ascended);
			if (level == -1)
			{
				UserInterface.AddError($"Could not determine {character.NameGOOD}'s level. Setting to 1.");
				level    = 1;
				ascended = false;
			}
			character.Level    = level;
			character.Ascended = ascended;

			Logger.Info("{0} Level: {1}",   character.NameGOOD, character.Level);
			Logger.Info("{0} Ascended: {1}", character.NameGOOD, character.Ascended);

			// Scan Constellation
			Navigation.SelectCharacterConstellation();
			character.Constellation = ScanConstellations(character);
			Logger.Info("{0} Constellation: {1}", character.NameGOOD, character.Constellation);
			Navigation.SystemWait(Navigation.Speed.Normal);

			// Scan Talents
			Navigation.SelectCharacterTalents();
			character.Talents = ScanTalents(character);
			Logger.Info("{0} Talents: {1}", character.NameGOOD,
				"{" + string.Join(", ", character.Talents.Select(kv => kv.Key + "=" + kv.Value).ToArray()) + "}");
			Navigation.SystemWait(Navigation.Speed.Normal);

			// Scale down talents due to constellations
			// Traveler has per-element ConstellationOrder; ordinary chars have a flat array.
			if (character.Constellation >= 3)
			{
				string nameLower = name.ToLower();
				if (GenshinProcesor.Characters.ContainsKey(nameLower))
				{
					bool isTraveler = character.NameGOOD.StartsWith("Traveler",
						StringComparison.OrdinalIgnoreCase);

					string jsonPath = System.IO.Path.GetFullPath(@".\inventorylists\characters.json");
					var charData = GenshinProcesor.Characters[nameLower];
					var orderToken = charData != null ? charData["ConstellationOrder"] : null;
					bool orderExists = orderToken != null;

					Newtonsoft.Json.Linq.JObject orderObj = orderToken as Newtonsoft.Json.Linq.JObject;
					string availProps = "(none)";
					if (orderObj != null)
					{
						var props = orderObj.Properties().Select(p => p.Name).ToArray();
						availProps = props.Length > 0 ? string.Join(", ", props) : "(empty)";
					}

					Newtonsoft.Json.Linq.JToken elementToken = null;
					if (isTraveler && !string.IsNullOrEmpty(character.Element) && orderExists)
					{
						elementToken = orderToken[character.Element.ToLower()];
					}
					else if (!isTraveler)
					{
						elementToken = orderToken;
					}

					bool tokenExists = elementToken != null;
					string tokenType = tokenExists ? elementToken.Type.ToString() : "Null";
					Newtonsoft.Json.Linq.JArray arrToken = elementToken as Newtonsoft.Json.Linq.JArray;
					int itemCount = (tokenExists && arrToken != null) ? arrToken.Count : 0;

					Logger.Info("[ScanCharacterDetails] Talent scaling diagnostics: key='{0}', NameGOOD='{1}', Element='{2}', JSONPath='{3}', OrderExists={4}, AvailableProps='{5}', ElementTokenExists={6}, TokenType='{7}', ItemCount={8}",
						nameLower, character.NameGOOD, character.Element ?? "", jsonPath, orderExists, availProps, tokenExists, tokenType, itemCount);

					string talentAtC3 = null;
					string talentAtC5 = null;

					if (tokenExists && elementToken.Type == Newtonsoft.Json.Linq.JTokenType.Array && itemCount >= 2)
					{
						talentAtC3 = (string)elementToken[0];
						talentAtC5 = (string)elementToken[1];
					}
					else
					{
						Logger.Warn("[ScanCharacterDetails] Missing or invalid ConstellationOrder array for {0} (element: {1}). Scanned talent levels left unchanged.",
							character.NameGOOD, character.Element ?? "N/A");
					}

					if (!string.IsNullOrEmpty(talentAtC3) && character.Talents.ContainsKey(talentAtC3))
					{
						Logger.Info("{0} constellation 3+, adjusting scanned '{1}' level by -3",
							character.NameGOOD, talentAtC3);
						character.Talents[talentAtC3] -= 3;
					}

					if (character.Constellation >= 5 && !string.IsNullOrEmpty(talentAtC5) && character.Talents.ContainsKey(talentAtC5))
					{
						Logger.Info("{0} constellation 5+, adjusting scanned '{1}' level by -3",
							character.NameGOOD, talentAtC5);
						character.Talents[talentAtC5] -= 3;
					}
				}
			}

			return character;
		}

		public static string ScanMainCharacterName()
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
			GenshinProcesor.SetInvert(ref nameBitmap);
			Bitmap n = GenshinProcesor.ConvertToGrayscale(nameBitmap);

			UserInterface.SetNavigation_Image(nameBitmap);

			string text = GenshinProcesor.AnalyzeText(n).Trim();
			if (text != "")
			{
				// Only keep a-Z and 0-9
				text = Regex.Replace(text, @"[\W_]", string.Empty).ToLower();

				// Only keep text up until first space
				text = Regex.Replace(text, @"\s+\w*", string.Empty);

			}
			else
			{
				UserInterface.AddError(text);
			}
			n.Dispose();
			nameBitmap.Dispose();
			return text;
		}

		/// <summary>
		/// Scans the character name and element from the screen.
		/// Resolves Traveler/Wanderer/Manekin aliases before element validation.
		/// Used both as the fast probe in ScanCharacters (for roster identity)
		/// and directly when name/element are needed.
		/// </summary>
		private static void ScanNameAndElement(
			ref string name, ref string element,
			string travelerAlias, string wandererAlias,
			string maleManekinAlias, string femaleManekinAlias)
		{
			int attempts = 0;
			int maxAttempts = 75;
			Rectangle region = new RECT(
				Left:   (int)( 85  / 1280.0 * Navigation.GetWidth() ),
				Top:    (int)( 10  / 720.0 * Navigation.GetHeight() ),
				Right:  (int)( 305 / 1280.0 * Navigation.GetWidth() ),
				Bottom: (int)( 55  / 720.0 * Navigation.GetHeight() ));

			do
			{
				Navigation.SystemWait(Navigation.Speed.Fast);
				using (Bitmap bm = Navigation.CaptureRegion(region))
				{
					using (Bitmap n0 = GenshinProcesor.ConvertToGrayscale(bm))
					{
						Bitmap n = n0;
						GenshinProcesor.SetThreshold(110, ref n);
						GenshinProcesor.SetInvert(ref n);

						n = GenshinProcesor.ResizeImage(n, n.Width * 2, n.Height * 2);
						string block = GenshinProcesor.AnalyzeText(n, Tesseract.PageSegMode.Auto).ToLower().Trim();
						string line = GenshinProcesor.AnalyzeText(n, Tesseract.PageSegMode.SingleLine).ToLower().Trim();
						n.Dispose();

						// Characters with wrapped names will not have a slash
						string nameAndElement = line.Contains("/") ? line : block;

						if (nameAndElement.Contains("/"))
						{
							var split = nameAndElement.Split('/');

							// Search for element and character name in block

							// Long name characters might look like
							// <Element>   <First Name>
							// /           <Last Name>
							string scannedElement = !split[0].Contains(" ")
								? GenshinProcesor.FindElementByName(split[0].Trim())
								: GenshinProcesor.FindElementByName(split[0].Split(' ')[0].Trim());
							string rawOcrName = Regex.Replace(split[1], @"[\W]", string.Empty);
							string rawOcrNameLower = rawOcrName.ToLower();

							// --- Alias resolution: check special characters BEFORE fuzzy lookup ---

							// Traveler custom name → canonical "Traveler" key (element preserved for GOOD export)
							if (!string.IsNullOrWhiteSpace(travelerAlias) &&
								string.Equals(rawOcrNameLower, travelerAlias, StringComparison.OrdinalIgnoreCase))
							{
								name    = "Traveler";
								element = scannedElement; // may be null; that is fine for roster key
								Logger.Debug("[ScanNameAndElement] OCR name '{0}' matched Traveler alias '{1}'",
									rawOcrName, travelerAlias);
								UserInterface.SetCharacter_NameAndElement(bm, name, element ?? "");
								return;
							}

							// Wanderer custom name → canonical "Wanderer"
							if (!string.IsNullOrWhiteSpace(wandererAlias) &&
								string.Equals(rawOcrNameLower, wandererAlias, StringComparison.OrdinalIgnoreCase))
							{
								name    = "Wanderer";
								element = scannedElement;
								Logger.Debug("[ScanNameAndElement] OCR name '{0}' matched Wanderer alias '{1}'",
									rawOcrName, wandererAlias);
								UserInterface.SetCharacter_NameAndElement(bm, name, element ?? "");
								return;
							}

							// Male Manekin custom name
							if (!string.IsNullOrWhiteSpace(maleManekinAlias) &&
								string.Equals(rawOcrNameLower, maleManekinAlias, StringComparison.OrdinalIgnoreCase))
							{
								name    = "Manequin1";
								element = scannedElement; // element not required for Manekin roster key
								Logger.Debug("[ScanNameAndElement] OCR name '{0}' matched male Manekin alias '{1}'",
									rawOcrName, maleManekinAlias);
								UserInterface.SetCharacter_NameAndElement(bm, name, element ?? "");
								return;
							}

							// Female Manekin custom name
							if (!string.IsNullOrWhiteSpace(femaleManekinAlias) &&
								string.Equals(rawOcrNameLower, femaleManekinAlias, StringComparison.OrdinalIgnoreCase))
							{
								name    = "Manequin2";
								element = scannedElement;
								Logger.Debug("[ScanNameAndElement] OCR name '{0}' matched female Manekin alias '{1}'",
									rawOcrName, femaleManekinAlias);
								UserInterface.SetCharacter_NameAndElement(bm, name, element ?? "");
								return;
							}

							// Ordinary character: fuzzy lookup + element validation
							{
								string resolvedName = GenshinProcesor.FindClosestCharacterName(rawOcrName);
								if (!GenshinProcesor.CharacterMatchesElement(resolvedName, scannedElement))
								{
									Logger.Debug("[ScanNameAndElement] resolved='{0}' does not match element='{1}', clearing.",
										resolvedName, scannedElement);
									resolvedName   = "";
									scannedElement = "";
								}
								name    = resolvedName;
								element = scannedElement;
							}
						}

						if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(element))
						{
							Logger.Debug("Scanned character name as {0} with element {1}", name, element);
							UserInterface.SetCharacter_NameAndElement(bm, name, element);
							return;
						}
						else
						{
							Logger.Debug("Could not parse character name/element (Atempt {0}/{1}). Retrying...", attempts+1, maxAttempts);
							bm.Save($"./logging/characters/{bm.GetHashCode()}.png");
						}
					}
				}
				attempts++;
				Navigation.SystemWait(Navigation.Speed.Fast);
			} while ( attempts < maxAttempts );
			name = null;
			element = null;
		}

		private static int ScanLevel(ref bool ascended)
		{
            int attempt = 0;

            var xRef = 1280.0;
			var yRef = 720.0;
			if (Navigation.GetAspectRatio() == new Size(8, 5))
			{
				yRef = 800.0;
			}

			Rectangle region =  new RECT(
				Left:   (int)( 960  / xRef * Navigation.GetWidth() ),
				Top:    (int)( 135  / yRef * Navigation.GetHeight() ),
				Right:  (int)( 1125 / xRef * Navigation.GetWidth() ),
				Bottom: (int)( 163  / yRef * Navigation.GetHeight() ));

			do
			{
				Bitmap bm = Navigation.CaptureRegion(region);

				bm = GenshinProcesor.ResizeImage(bm, bm.Width * 2, bm.Height * 2);
				Bitmap n = GenshinProcesor.ConvertToGrayscale(bm);
				GenshinProcesor.SetInvert(ref n);
				GenshinProcesor.SetContrast(30.0, ref bm);

				string text = GenshinProcesor.AnalyzeText(n).Trim();
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
                        UserInterface.SetCharacter_Level(bm, level, maxLevel);
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
			} while (attempt < 50);

			return -1;
		}

		private static int ScanExperience()
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
			GenshinProcesor.SetContrast(30.0, ref bm);

			string text = GenshinProcesor.AnalyzeText(bm);
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
				UserInterface.AddError("Found " + experience + " instead of experience");
			}

			return experience;
		}

		private static int ScanConstellations(Character character)
		{
			double yReference = 720.0;
			int constellation;

			if (Navigation.GetAspectRatio() == new Size(8, 5))
			{
				yReference = 800.0;
			}

			Rectangle constActivate =  new RECT(
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

				if (Properties.Settings.Default.LogScreenshots)
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
					ImageStatistics statistics = new ImageStatistics(region);
					if (statistics.Red.Mean >= 190 && statistics.Green.Mean >= 190 && statistics.Blue.Mean >= 190)
						break;

				}
			}

			Navigation.sim.Keyboard.KeyPress(WindowsInput.Native.VirtualKeyCode.ESCAPE);
			UserInterface.SetCharacter_Constellation(constellation);
			return constellation;
		}

		private static Dictionary<string, int> ScanTalents(Character character)
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

			Rectangle region =  new Rectangle(
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

					Bitmap n = GenshinProcesor.ConvertToGrayscale(talentLevel);
					GenshinProcesor.SetContrast(60, ref n);
					GenshinProcesor.SetInvert(ref n);

					var text = GenshinProcesor.AnalyzeText(n, Tesseract.PageSegMode.SingleBlock).Trim().Split('\n').ToList();

					if (int.TryParse(Regex.Replace(text.Last(), @"\D", string.Empty), out int level))
					{
						if (level >= 1 && level <= 15)
						{
							talents[talent] = level;
							UserInterface.SetCharacter_Talent(talentLevel, level.ToString(), i);
						}
					}

					n.Dispose();
					talentLevel.Dispose();
				}
			}

			Navigation.sim.Keyboard.KeyPress(WindowsInput.Native.VirtualKeyCode.ESCAPE);
			return talents;
		}
	}
}