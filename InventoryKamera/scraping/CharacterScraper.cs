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
		private const string SkipCharacterMarker = "__KAMERA_SKIP_UNRECOGNIZED__";

		protected int NumOfCharToScan;

        public CharacterScraper()
		{
			NumOfCharToScan = Properties.Settings.Default.NumOfCharToScan;
		}

		public void ScanCharacters(ref List<Character> Characters)
		{
			int viewed = 0;
			int counter = 0;
			string first = null;
			HashSet<string> scanned = new HashSet<string>();

			UserInterface.ResetCharacterDisplay();

			while (true)
			{
				var character = ScanCharacter(first);
				if (string.Equals(character.NameGOOD, SkipCharacterMarker, StringComparison.Ordinal))
				{
					Logger.Info("Skipped an unrecognized character and continuing to the next character.");
				}
				else if (GenshinProcesor.IsScannerOnlyCharacter(character.NameGOOD))
				{
					// Manekin/Manekina are recognized so navigation can continue and equipped
					// item OCR can identify their custom names. They are scanner-only aliases,
					// not standard GOOD character records.
					Logger.Info("Recognized scanner-only character {0}; skipping standard GOOD character export.", character.NameGOOD);
				}
				else
				{
                    if (Characters.Count > 0 && character.NameGOOD == Characters.ElementAt(0).NameGOOD) break;
                    if (character.IsValid())
                    {
                        if (!scanned.Contains(character.NameGOOD))
                        {
                            Characters.Add(character);
                            UserInterface.IncrementCharacterCount();
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
				UserInterface.ResetCharacterDisplay();

				if ((++viewed > 3 && Characters.Count < 1) || (NumOfCharToScan !=0 && counter >= NumOfCharToScan)) break;
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

		private static Character ScanCharacter(string firstCharacter)
		{
			var character = new Character();
			Navigation.SelectCharacterAttributes();

			string name = null;
			string element = null;

			// Scan the header with a bounded retry. Character identity is exact-only:
			// canonical database name or an exact configured custom alias.
			ScanNameAndElement(
				ref name,
				ref element,
				out string rawName,
				out List<string> exactCandidates);

			if (string.IsNullOrWhiteSpace(name))
			{
				bool travelerCandidate = exactCandidates.Any(candidate =>
					string.Equals(candidate, "Traveler", StringComparison.OrdinalIgnoreCase));
				var mannequinCandidates = exactCandidates
					.Where(GenshinProcesor.IsScannerOnlyCharacter)
					.ToList();

				// Exact Traveler/mannequin custom-name collisions cannot be resolved
				// by name alone. Carry Traveler forward provisionally so the visual
				// no-Constellations template can make the decision on the single
				// Constellation visit below.
				if (travelerCandidate && mannequinCandidates.Count > 0 &&
					!string.IsNullOrWhiteSpace(element))
				{
					name = "Traveler";
					Logger.Debug(
						"Configured Traveler/mannequin collision '{0}' carried forward as a provisional Traveler candidate.",
						rawName);
				}
				else
				{
					Logger.Info(
						"Character header name '{0}' is not a known database character or configured custom alias. Skipping.",
						string.IsNullOrWhiteSpace(rawName) ? "<unreadable>" : rawName);
					character.NameGOOD = SkipCharacterMarker;
					return character;
				}
			}

			character.NameGOOD = name;
			character.Element = element;

			// Scanner-only aliases never belong in standard GOOD output. If one is
			// explicitly configured and recognized, skip it without scanning stats.
			if (GenshinProcesor.IsScannerOnlyCharacter(character.NameGOOD))
				return character;

			bool ascended = false;

			// Scan level while we are still on Attributes. This deliberately occurs
			// before duplicate/wrap detection so the Constellation template can vet
			// an exact-name mannequin before a repeated name is allowed to end the loop.
			int level = ScanLevel(ref ascended);
			if (level == -1)
			{
				UserInterface.AddError($"Could not determine {character.NameGOOD}'s level. Setting to 1.");
				level = 1;
				ascended = false;
			}
			character.Level = level;
			character.Ascended = ascended;

			Logger.Info("{0} Level: {1}", character.NameGOOD, character.Level);
			Logger.Info("{0} Ascended: {1}", character.NameGOOD, character.Ascended);

			// ONE Constellation visit per recognized roster entry:
			// 1. Open Constellation.
			// 2. Compare the already-visible upper-right panel to the supplied
			//    no-Constellations template.
			// 3. If it matches, return to Attributes and skip.
			// 4. If it does not match, stay on this same Constellation screen and
			//    continue directly into the existing ScanConstellations routine.
			Navigation.SelectCharacterConstellation();

			if (LooksLikeNoConstellationPanel(out double noConstellationScore))
			{
				Logger.Info(
					"No-Constellations visual reference matched '{0}' " +
					"(score={1:0.000}); skipping this roster entry.",
					string.IsNullOrWhiteSpace(rawName) ? character.NameGOOD : rawName,
					noConstellationScore);

				Navigation.SelectCharacterAttributes();
				character.NameGOOD = SkipCharacterMarker;
				return character;
			}

			// Preserve the existing first-character duplicate/wrap termination rule.
			// This check intentionally occurs AFTER the visual mannequin gate so a
			// mannequin sharing the first character's name cannot end the scan.
			if (character.NameGOOD == firstCharacter)
			{
				Logger.Info("Repeat character {0} detected. Finishing character scan...", name);
				return character;
			}

			// We are already on Constellation. Do NOT select the tab a second time.
			character.Constellation = ScanConstellations(character);
			Logger.Info("{0} Constellation: {1}", character.NameGOOD, character.Constellation);
			Navigation.SystemWait(Navigation.Speed.Normal);

			// Scan Talents
			Navigation.SelectCharacterTalents();
			character.Talents = ScanTalents(character);
			Logger.Info(
				"{0} Talents: {1}",
				character.NameGOOD,
				"{" + string.Join(", ", character.Talents.Select(kv => kv.Key + "=" + kv.Value).ToArray()) + "}");
			Navigation.SystemWait(Navigation.Speed.Normal);

			// Scale down talents due to constellations
			if (character.Constellation >= 3)
			{
				if (GenshinProcesor.Characters.ContainsKey(name.ToLower()))
				{
					string talentLeveledAtConst3 = character.NameGOOD.Contains("Traveler")
						? (string)GenshinProcesor.Characters[name.ToLower()]["ConstellationOrder"][character.Element.ToLower()][0]
						: (string)GenshinProcesor.Characters[name.ToLower()]["ConstellationOrder"][0];
					string talentLeveledAtConst5 = character.NameGOOD.Contains("Traveler")
						? (string)GenshinProcesor.Characters[name.ToLower()]["ConstellationOrder"][character.Element.ToLower()][1]
						: (string)GenshinProcesor.Characters[name.ToLower()]["ConstellationOrder"][1];

					if (character.Constellation >= 3)
					{
						Logger.Info(
							"{0} constellation 3+, adjusting scanned {1} level",
							character.NameGOOD,
							talentLeveledAtConst3);
						character.Talents[talentLeveledAtConst3] -= 3;
					}

					if (character.Constellation >= 5)
					{
						Logger.Info(
							"{0} constellation 5+, adjusting scanned {1} level",
							character.NameGOOD,
							talentLeveledAtConst5);
						character.Talents[talentLeveledAtConst5] -= 3;
					}
				}
				else
				{
					return character;
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

		private static bool TryExtractRawCharacterHeader(
			string line,
			string block,
			out string rawName,
			out string element)
		{
			rawName = "";
			element = "";

			string lineText = (line ?? "").ToLower().Trim();
			string blockText = (block ?? "").ToLower().Trim();

			foreach (string raw in new[] { lineText, blockText })
			{
				if (string.IsNullOrWhiteSpace(raw)) continue;

				if (raw.Contains("/"))
				{
					var split = raw.Split(new[] { '/' }, 2);
					if (split.Length != 2) continue;

					string elementText = split[0].Trim();
					if (elementText.Contains(" "))
						elementText = elementText.Split(' ')[0].Trim();

					string parsedElement = GenshinProcesor.FindElementByName(elementText);
					string parsedRawName = Regex.Replace(split[1], @"[\W_]", string.Empty).ToLower();

					if (!string.IsNullOrWhiteSpace(parsedElement) &&
						!string.IsNullOrWhiteSpace(parsedRawName))
					{
						rawName = parsedRawName;
						element = parsedElement;
						return true;
					}
				}

				string elementKey = GenshinProcesor.Elements.Keys.FirstOrDefault(key =>
					Regex.IsMatch(
						raw,
						$@"(^|[^a-z]){Regex.Escape(key)}([^a-z]|$)",
						RegexOptions.IgnoreCase));

				if (string.IsNullOrWhiteSpace(elementKey)) continue;

				string parsedElementFallback = GenshinProcesor.FindElementByName(elementKey);
				string withoutElement = Regex.Replace(
					raw,
					$@"(^|[^a-z]){Regex.Escape(elementKey)}([^a-z]|$)",
					" ",
					RegexOptions.IgnoreCase);

				string parsedRawNameFallback = Regex.Split(withoutElement, @"[/\r\n]+")
					.Concat(new[] { withoutElement })
					.Select(fragment => Regex.Replace(fragment, @"[^a-z0-9]", string.Empty))
					.Where(fragment => fragment.Length >= 3)
					.OrderByDescending(fragment => fragment.Length)
					.FirstOrDefault();

				if (!string.IsNullOrWhiteSpace(parsedElementFallback) &&
					!string.IsNullOrWhiteSpace(parsedRawNameFallback))
				{
					rawName = parsedRawNameFallback;
					element = parsedElementFallback;
					return true;
				}
			}

			return false;
		}

		private static string ResolveExactCharacterName(string scannedName)
		{
			var exactMatches = GenshinProcesor.FindExactCharacterNameCandidates(scannedName);

			if (exactMatches.Count == 1)
				return exactMatches[0];

			if (exactMatches.Count > 1)
			{
				Logger.Debug(
					"Exact character header '{0}' maps to multiple configured identities: {1}",
					scannedName,
					string.Join(", ", exactMatches));
			}

			return "";
		}

		private static bool TryParseCharacterHeader(string line, string block, out string name, out string element)
		{
			name = "";
			element = "";

			string lineText = (line ?? "").ToLower().Trim();
			string blockText = (block ?? "").ToLower().Trim();
			string nameAndElement = lineText.Contains("/") ? lineText : blockText;

			// Primary path used by the original scanner.
			if (nameAndElement.Contains("/"))
			{
				var split = nameAndElement.Split(new[] { '/' }, 2);
				if (split.Length == 2)
				{
					string elementText = split[0].Trim();
					if (elementText.Contains(" ")) elementText = elementText.Split(' ')[0].Trim();

					element = GenshinProcesor.FindElementByName(elementText);
					name = ResolveExactCharacterName(
						Regex.Replace(split[1], @"[\W_]", string.Empty));

					if (!string.IsNullOrWhiteSpace(name) &&
						!string.IsNullOrWhiteSpace(element) &&
						GenshinProcesor.CharacterMatchesElement(name, element))
						return true;
				}
			}

			// Fallback for long/wrapped names or UI overlays that hide the slash.
			// Find a literal element token, remove it, then require an exact canonical
			// database name or exact configured custom alias. No fuzzy/partial identity.
			var rawCandidates = new[] { lineText, blockText, lineText + "\n" + blockText };
			foreach (string raw in rawCandidates)
			{
				if (string.IsNullOrWhiteSpace(raw)) continue;

				string elementKey = GenshinProcesor.Elements.Keys.FirstOrDefault(key =>
					Regex.IsMatch(raw, $@"(^|[^a-z]){Regex.Escape(key)}([^a-z]|$)",
						RegexOptions.IgnoreCase));

				if (string.IsNullOrWhiteSpace(elementKey)) continue;

				string parsedElement = GenshinProcesor.FindElementByName(elementKey);
				string withoutElement = Regex.Replace(
					raw,
					$@"(^|[^a-z]){Regex.Escape(elementKey)}([^a-z]|$)",
					" ",
					RegexOptions.IgnoreCase);

				var fragments = Regex.Split(withoutElement, @"[/\r\n]+")
					.Concat(new[] { withoutElement })
					.Select(fragment => Regex.Replace(fragment, @"[^a-z0-9]", string.Empty))
					.Where(fragment => fragment.Length >= 3)
					.Distinct()
					.OrderByDescending(fragment => fragment.Length);

				foreach (string fragment in fragments)
				{
					string parsedName = ResolveExactCharacterName(fragment);
					if (!string.IsNullOrWhiteSpace(parsedName) &&
						!string.IsNullOrWhiteSpace(parsedElement) &&
						GenshinProcesor.CharacterMatchesElement(parsedName, parsedElement))
					{
						name = parsedName;
						element = parsedElement;
						return true;
					}
				}
			}

			name = "";
			element = "";
			return false;
		}

		private static void ScanNameAndElement(
			ref string name,
			ref string element,
			out string rawName,
			out List<string> exactCandidates)
		{
			int attempts = 0;
			int maxAttempts = 10;
			rawName = "";
			exactCandidates = new List<string>();

			// Primary crop widened slightly for long names (for example Kaedehara Kazuha) while stopping before the Q/portrait UI; then retry with the existing wider/taller crop.
			RECT[] regions = new[]
			{
				new RECT(
					Left:   (int)( 85  / 1280.0 * Navigation.GetWidth() ),
					Top:    (int)( 10  / 720.0 * Navigation.GetHeight() ),
					Right:  (int)( 318 / 1280.0 * Navigation.GetWidth() ),
					Bottom: (int)( 55  / 720.0 * Navigation.GetHeight() )),
				new RECT(
					Left:   (int)( 70  / 1280.0 * Navigation.GetWidth() ),
					Top:    (int)( 5   / 720.0 * Navigation.GetHeight() ),
					Right:  (int)( 430 / 1280.0 * Navigation.GetWidth() ),
					Bottom: (int)( 78  / 720.0 * Navigation.GetHeight() ))
			};

			string lastRawName = "";
			string lastElement = "";
			List<string> lastExactCandidates = new List<string>();

			do
			{
				Navigation.SystemWait(Navigation.Speed.Fast);

				for (int regionIndex = 0; regionIndex < regions.Length; regionIndex++)
				{
					using (Bitmap bm = Navigation.CaptureRegion(regions[regionIndex]))
					{
						Bitmap n = GenshinProcesor.ConvertToGrayscale(bm);
						GenshinProcesor.SetThreshold(110, ref n);
						GenshinProcesor.SetInvert(ref n);

						n = GenshinProcesor.ResizeImage(n, n.Width * 2, n.Height * 2);
						string block = GenshinProcesor.AnalyzeText(
							n,
							Tesseract.PageSegMode.Auto).ToLower().Trim();
						string line = GenshinProcesor.AnalyzeText(
							n,
							Tesseract.PageSegMode.SingleLine).ToLower().Trim();
						n.Dispose();

						if (TryParseCharacterHeader(line, block, out name, out element))
						{
							if (TryExtractRawCharacterHeader(
								line,
								block,
								out string parsedRawName,
								out string parsedElement))
							{
								rawName = parsedRawName;
								exactCandidates =
									GenshinProcesor.FindExactCharacterNameCandidates(rawName);
							}

							Logger.Debug(
								"Scanned character name as {0} with element {1} using header region {2}",
								name,
								element,
								regionIndex);
							UserInterface.SetCharacter_NameAndElement(bm, name, element);
							return;
						}

						if (TryExtractRawCharacterHeader(
							line,
							block,
							out string unresolvedRawName,
							out string unresolvedElement))
						{
							lastRawName = unresolvedRawName;
							lastElement = unresolvedElement;
							lastExactCandidates =
								GenshinProcesor.FindExactCharacterNameCandidates(lastRawName);

							// An exact configured collision will not become less ambiguous
							// by retrying OCR. Return it to ScanCharacter for the narrow
							// exact configured Traveler/mannequin collision handling.
							if (lastExactCandidates.Count > 1)
							{
								name = null;
								element = lastElement;
								rawName = lastRawName;
								exactCandidates = lastExactCandidates;
								Logger.Debug(
									"Character header '{0}' exactly matches multiple configured identities: {1}",
									rawName,
									string.Join(", ", exactCandidates));
								return;
							}
						}

						Logger.Debug(
							"Could not parse character name/element (Attempt {0}/{1}, region {2}). Retrying...",
							attempts + 1,
							maxAttempts,
							regionIndex);

						if (Properties.Settings.Default.LogScreenshots)
						{
							Directory.CreateDirectory("./logging/characters");
							bm.Save(
								$"./logging/characters/header_attempt{attempts}_region{regionIndex}_{bm.GetHashCode()}.png");
						}
					}
				}

				attempts++;
				Navigation.SystemWait(Navigation.Speed.Fast);
			} while (attempts < maxAttempts);

			name = null;
			element = lastElement;
			rawName = lastRawName;
			exactCandidates = lastExactCandidates;
		}

		private const double NoConstellationReferenceThreshold = 0.78;
		private const int NoConstellationReferenceWidth = 1918;
		private const int NoConstellationDownsample = 4;

		private static byte[] BitmapToGrayArray(Bitmap bitmap)
		{
			byte[] pixels = new byte[bitmap.Width * bitmap.Height];
			int index = 0;

			for (int y = 0; y < bitmap.Height; y++)
			{
				for (int x = 0; x < bitmap.Width; x++)
				{
					Color c = bitmap.GetPixel(x, y);
					pixels[index++] = (byte)((c.R * 299 + c.G * 587 + c.B * 114) / 1000);
				}
			}

			return pixels;
		}

		private static double BestNormalizedTemplateScore(Bitmap source, Bitmap template)
		{
			if (source == null || template == null ||
				template.Width > source.Width || template.Height > source.Height)
				return 0;

			byte[] sourceGray = BitmapToGrayArray(source);
			byte[] templateGray = BitmapToGrayArray(template);

			int templatePixels = template.Width * template.Height;
			double templateSum = 0;
			double templateSumSquares = 0;

			for (int i = 0; i < templateGray.Length; i++)
			{
				double value = templateGray[i];
				templateSum += value;
				templateSumSquares += value * value;
			}

			double templateVariance =
				templateSumSquares - (templateSum * templateSum / templatePixels);
			if (templateVariance <= 0)
				return 0;

			double best = -1;

			for (int offsetY = 0; offsetY <= source.Height - template.Height; offsetY++)
			{
				for (int offsetX = 0; offsetX <= source.Width - template.Width; offsetX++)
				{
					double sourceSum = 0;
					double sourceSumSquares = 0;
					double crossSum = 0;

					for (int y = 0; y < template.Height; y++)
					{
						int sourceRow = (offsetY + y) * source.Width + offsetX;
						int templateRow = y * template.Width;

						for (int x = 0; x < template.Width; x++)
						{
							double sourceValue = sourceGray[sourceRow + x];
							double templateValue = templateGray[templateRow + x];
							sourceSum += sourceValue;
							sourceSumSquares += sourceValue * sourceValue;
							crossSum += sourceValue * templateValue;
						}
					}

					double sourceVariance =
						sourceSumSquares - (sourceSum * sourceSum / templatePixels);
					if (sourceVariance <= 0)
						continue;

					double covariance =
						crossSum - (sourceSum * templateSum / templatePixels);
					double score =
						covariance / Math.Sqrt(sourceVariance * templateVariance);

					if (score > best)
						best = score;
				}
			}

			return Math.Max(0, best);
		}

		private static bool LooksLikeNoConstellationPanel(out double bestScore)
		{
			bestScore = 0;

			// Pure visual check. The caller must already be on the Constellation tab.
			// This method performs no game navigation; it only captures and compares
			// the fixed upper-right panel against the supplied screenshot reference.
			// Exact visual reference supplied from the user's mannequin screenshot.
			// The stored image is the fixed right-corner panel crop. From that crop,
			// x=285..390 / y=5..105 is the prohibition icon used as the template.
			string referencePath = Path.Combine(
				AppDomain.CurrentDomain.BaseDirectory,
				"templates",
				"no_constellations_reference.png");

			if (!File.Exists(referencePath))
			{
				Logger.Warn(
					"No-Constellations visual reference is missing at {0}. Continuing without mannequin visual filtering.",
					referencePath);
				return false;
			}

			double yReference =
				Navigation.GetAspectRatio() == new Size(8, 5) ? 800.0 : 720.0;

			// Search only the upper-right area containing the prohibition icon in the
			// supplied screenshot. Reference-space bounds: x=1030..1200, y=70..200.
			Rectangle searchRegion = new RECT(
				Left:   (int)(1030 / 1280.0 * Navigation.GetWidth()),
				Top:    (int)(70   / yReference * Navigation.GetHeight()),
				Right:  (int)(1200 / 1280.0 * Navigation.GetWidth()),
				Bottom: (int)(200  / yReference * Navigation.GetHeight()));

			using (Bitmap currentRegion = Navigation.CaptureRegion(searchRegion))
			using (Bitmap referencePanel = new Bitmap(referencePath))
			using (Bitmap referenceIcon = referencePanel.Clone(
				new Rectangle(285, 5, 105, 100),
				referencePanel.PixelFormat))
			using (Bitmap smallCurrent = GenshinProcesor.ResizeImage(
				currentRegion,
				Math.Max(1, currentRegion.Width / NoConstellationDownsample),
				Math.Max(1, currentRegion.Height / NoConstellationDownsample)))
			{
				double baseScale =
					Navigation.GetWidth() / (double)NoConstellationReferenceWidth;

				foreach (double scaleMultiplier in new[] { 0.85, 1.00, 1.15 })
				{
					int templateWidth = Math.Max(
						4,
						(int)Math.Round(
							referenceIcon.Width *
							baseScale *
							scaleMultiplier /
							NoConstellationDownsample));
					int templateHeight = Math.Max(
						4,
						(int)Math.Round(
							referenceIcon.Height *
							baseScale *
							scaleMultiplier /
							NoConstellationDownsample));

					if (templateWidth > smallCurrent.Width ||
						templateHeight > smallCurrent.Height)
						continue;

					using (Bitmap scaledTemplate = GenshinProcesor.ResizeImage(
						referenceIcon,
						templateWidth,
						templateHeight))
					{
						double score =
							BestNormalizedTemplateScore(smallCurrent, scaledTemplate);
						if (score > bestScore)
							bestScore = score;
					}
				}
			}

			bool detected = bestScore >= NoConstellationReferenceThreshold;

			Logger.Debug(
				"No-Constellations visual reference probe: score={0:0.000}, threshold={1:0.000}, detected={2}",
				bestScore,
				NoConstellationReferenceThreshold,
				detected);

			return detected;
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