using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace InventoryKamera
{
    public static class GenshinProcesor
	{
		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		internal static Dictionary<string, string> Stats = new Dictionary<string, string>
		{
			["hp"] = "hp",
			["hp%"] = "hp_",
			["atk"] = "atk",
			["atk%"] = "atk_",
			["def"] = "def",
			["def%"] = "def_",
			["energyrecharge"] = "enerRech_",
			["elementalmastery"] = "eleMas",
			["healingbonus"] = "heal_",
			["critrate"] = "critRate_",
			["critdmg"] = "critDMG_",
			["physicaldmgbonus"] = "physical_dmg_",
		};

		internal static readonly List<string> gearSlots = new List<string>
		{
			"flower",
			"plume",
			"sands",
			"goblet",
			"circlet",
		};

		private static readonly List<string> elements = new List<string>
		{
			"pyro",
			"hydro",
			"dendro",
			"electro",
			"anemo",
			"cryo",
			"geo",
		};

		internal static readonly HashSet<string> enhancementMaterials = new HashSet<string>
		{
			"enhancementore",
			"fineenhancementore",
			"mysticenhancementore",
			"sanctifyingunction",
			"sanctifyingessence",
		};

		internal static readonly List<string> customNames = new List<string>
		{
			"Traveler",
			"Wanderer",
			"Manequin1",
			"Manequin2"
		};

		internal static Dictionary<string, string> Weapons, DevItems, Materials, Elements;

		internal static Dictionary<string, JObject> Characters, Artifacts;

		static GenshinProcesor()
        {
			ReloadData();

			Elements = new Dictionary<string, string>();
			foreach (var element in elements)
			{
				Stats.Add($"{element.ToLower()}dmgbonus", $"{element.ToLower()}_dmg_");  // ["anemodmgbonus"] = "anemo_dmg_"
				Elements.Add(element, char.ToUpper(element[0]) + element.Substring(1));
			}

			Logger.Info("Scraper initialized");
        }

		internal static void ReloadData()
        {
			var listManager = new DatabaseManager();

			Characters = listManager.LoadCharacters();
			Artifacts = listManager.LoadArtifacts();
			Weapons = listManager.LoadWeapons();
			DevItems = listManager.LoadDevItems();
			Materials = listManager.LoadMaterials();

			EnsureManequinEntriesExist(listManager.ListsDir);
		}

		private static readonly string[] manequinKeys = { "manequin1", "manequin2" };

		/// <summary>
		/// GOOD doesn't support manequins, so characters.json omits them from the app's hosted data.
		/// Add placeholder entries (excluded from scanning by name, same as before) if they're
		/// missing, via the JSON object model rather than string-surgery on the file, and persist
		/// them so future loads don't need to patch again.
		/// </summary>
		private static void EnsureManequinEntriesExist(string listsDir)
		{
			bool added = false;
			foreach (var key in manequinKeys)
			{
				if (!Characters.ContainsKey(key))
				{
					Characters[key] = BuildManequinEntry(key);
					added = true;
				}
			}

			if (!added) return;

			File.WriteAllText(Path.Combine(listsDir, "characters.json"),
				JsonConvert.SerializeObject(new SortedDictionary<string, JObject>(Characters), Newtonsoft.Json.Formatting.Indented));
			Logger.Info("Added missing manequin entries to characters.json");
		}

		internal static JObject BuildManequinEntry(string key)
		{
			string good = char.ToUpper(key[0]) + key.Substring(1); // "manequin1" -> "Manequin1"
			return new JObject
			{
				["GOOD"] = good,
				["ConstellationName"] = new JArray("Support entry to omit manequins during scanning; GOOD does not support manequins."),
				["ConstellationOrder"] = new JArray("burst", "skill"),
				["Element"] = new JArray("electro", "pyro", "dendro", "geo", "hydro", "anemo"),
				["WeaponType"] = 0
			};
		}

		internal static void UpdateCharacterName(string target, string name)
        {
			target = target.ConvertToGood().ToLower();
			name = name.ConvertToGood().ToLower();

			if (target == name) return;

			if (Characters.TryGetValue(name, out _))
			{
				Logger.Error("{0} already exists as a character in the game. " +
					"This may wind up confusing Kamera when connecting items for {1}.", name, target);
			}

            if (Characters.TryGetValue(target, out _))
			{
				Characters[target]["CustomName"] = name;
				Logger.Info("Internally set {0} custom name to {1}", target, Characters[target]["CustomName"]);
			}
			else throw new KeyNotFoundException($"Could not find '{target}' entry in characters.json");
		}

		internal static void AssignTravelerName(string name, IOcrService ocrService, IImagePreprocessor imagePreprocessor)
		{
			name = string.IsNullOrWhiteSpace(name) ? CharacterScraper.ScanMainCharacterName(ocrService, imagePreprocessor) : name.ToLower();
			if (!string.IsNullOrWhiteSpace(name))
			{
				UpdateCharacterName("traveler", name);
				UserInterface.SetMainCharacterName(name);
			}
			else
			{
				UserInterface.AddError("Could not parse Traveler's username");
			}
		}

		#region Check valid parameters

		// Thin forwarding wrappers to the extracted LookupService (Phase 2 §2.1), passing this
		// class's current static lookup dictionaries each call -- kept so existing call sites don't
		// need to change, and always fresh since ReloadData() reassigns these dictionaries per scan.

		internal static bool IsValidSetName(string setName) => LookupService.IsValidSetName(setName, Artifacts);

		internal static bool IsValidMaterial(string name) => LookupService.IsValidMaterial(name, Materials);

		internal static bool IsValidStat(string stat) => LookupService.IsValidStat(stat, Stats);

		internal static bool IsValidSlot(string gearSlot) => LookupService.IsValidSlot(gearSlot, gearSlots);

		internal static bool IsValidCharacter(string character) => LookupService.IsValidCharacter(character, Characters);

		internal static bool IsValidElement(string element) => LookupService.IsValidElement(element, Elements);

		internal static bool IsEnhancementMaterial(string material) => LookupService.IsEnhancementMaterial(material, enhancementMaterials, Materials);

		internal static bool IsValidWeapon(string weapon) => LookupService.IsValidWeapon(weapon, Weapons);

		#endregion Check valid parameters

		#region Element Searching

		// Thin forwarding wrappers to the extracted TextNormalizer (Phase 2 §2.1), passing this
		// class's current static lookup dictionaries each call -- same reasoning as the
		// "Check valid parameters" region above: always fresh, no staleness risk from ReloadData().

		internal static string FindClosestGearSlot(string input) => TextNormalizer.FindClosestGearSlot(input, gearSlots);

		internal static string FindClosestStat(string stat, int minConfidence = 90) => TextNormalizer.FindClosestStat(stat, Stats, minConfidence);

		internal static string FindElementByName(string name, int minConfidence = 90) => TextNormalizer.FindElementByName(name, Elements, minConfidence);

		internal static string FindClosestWeapon(string name, int maxEdits = 90) => TextNormalizer.FindClosestWeapon(name, Weapons, maxEdits);

		internal static string FindClosestSetName(string name, int minConfidence = 90) => TextNormalizer.FindClosestSetName(name, Artifacts, minConfidence);

		internal static string FindClosestArtifactSetFromArtifactName(string name, int minConfidence = 90) =>
			TextNormalizer.FindClosestArtifactSetFromArtifactName(name, Artifacts, minConfidence);

		internal static string FindClosestCharacterName(string name, int minConfidence = 90) =>
			TextNormalizer.FindClosestCharacterName(name, Characters, minConfidence);

		internal static string FindClosestDevelopmentName(string name, int minConfidence = 90) =>
			TextNormalizer.FindClosestDevelopmentName(name, DevItems, Materials, minConfidence);

		internal static string FindClosestMaterialName(string name, int minConfidence = 90) =>
			TextNormalizer.FindClosestMaterialName(name, Materials, minConfidence);

        #endregion Element Searching


        internal static void FindDelay(List<Rectangle> rectangles)
		{
			Navigation.SetDelay(180);
			int delayOffset = 20;
			bool bStoppedOnce = false;
			Bitmap card1; Bitmap card2; Bitmap card3;
			Rectangle item1 = rectangles[0];

			RECT reference; int width = Navigation.GetWidth(); int height = Navigation.GetHeight();

			#region Get first card

			if (Navigation.GetAspectRatio() == new Size(16, 9))
			{
				reference = new RECT(new Rectangle(862, 80, 327, 560));

				int left = (int)Math.Round(reference.Left / 1280.0 * width, MidpointRounding.AwayFromZero);
				int top = (int)Math.Round(reference.Top / 720.0 * height, MidpointRounding.AwayFromZero);
				int right = (int)Math.Round(reference.Right / 1280.0 * width, MidpointRounding.AwayFromZero);
				int bottom = (int)Math.Round(reference.Bottom / 720.0 * height, MidpointRounding.AwayFromZero);

				card1 = Navigation.CaptureRegion(new RECT(left, top, right, bottom));
			}
			else // if (Navigation.GetAspectRatio() == new Size(8, 5))
			{
				reference = new RECT(new Rectangle(862, 80, 327, 640));

				int left = (int)Math.Round(reference.Left / 1280.0 * width, MidpointRounding.AwayFromZero);
				int top = (int)Math.Round(reference.Top / 800.0 * height, MidpointRounding.AwayFromZero);
				int right = (int)Math.Round(reference.Right / 1280.0 * width, MidpointRounding.AwayFromZero);
				int bottom = (int)Math.Round(reference.Bottom / 800.0 * height, MidpointRounding.AwayFromZero);

				card1 = Navigation.CaptureRegion(new RECT(left, top, right, bottom));
			}

			#endregion Get first card

			do
			{
				if (bStoppedOnce)
					delayOffset = 10;

				// Do mouse movement to first and second UI element in Inventory
				Navigation.SetCursor(item1.Center().X, item1.Center().Y);
				Navigation.Click();
				Navigation.Wait(((int)Navigation.GetDelay()) - delayOffset);

				Rectangle item2 = rectangles[1];
				Navigation.SetCursor(item2.Center().X, item2.Center().Y);
				Navigation.Click();
				Navigation.Wait(((int)Navigation.GetDelay()) - delayOffset);

				// Take image after second click
				if (Navigation.GetAspectRatio() == new Size(16, 9))
				{
					int left = (int)Math.Round(reference.Left / 1280.0 * width, MidpointRounding.AwayFromZero);
					int top = (int)Math.Round(reference.Top / 720.0 * height, MidpointRounding.AwayFromZero);
					int right = (int)Math.Round(reference.Right / 1280.0 * width, MidpointRounding.AwayFromZero);
					int bottom = (int)Math.Round(reference.Bottom / 720.0 * height, MidpointRounding.AwayFromZero);

					card2 = Navigation.CaptureRegion(new RECT(left, top, right, bottom));
				}
				else
				{
					int left = (int)Math.Round(reference.Left / 1280.0 * width, MidpointRounding.AwayFromZero);
					int top = (int)Math.Round(reference.Top / 800.0 * height, MidpointRounding.AwayFromZero);
					int right = (int)Math.Round(reference.Right / 1280.0 * width, MidpointRounding.AwayFromZero);
					int bottom = (int)Math.Round(reference.Bottom / 800.0 * height, MidpointRounding.AwayFromZero);

					card2 = Navigation.CaptureRegion(new RECT(left, top, right, bottom));
				}

				Rectangle item3 = rectangles[2];
				Navigation.SetCursor(item3.Center().X, item3.Center().Y);
				Navigation.Click();
				Navigation.Wait(((int)Navigation.GetDelay()) - delayOffset);

				// Take image after third click
				if (Navigation.GetAspectRatio() == new Size(16, 9))
				{
					int left = (int)Math.Round(reference.Left / 1280.0 * width, MidpointRounding.AwayFromZero);
					int top = (int)Math.Round(reference.Top / 720.0 * height, MidpointRounding.AwayFromZero);
					int right = (int)Math.Round(reference.Right / 1280.0 * width, MidpointRounding.AwayFromZero);
					int bottom = (int)Math.Round(reference.Bottom / 720.0 * height, MidpointRounding.AwayFromZero);

					card3 = Navigation.CaptureRegion(new RECT(left, top, right, bottom));
				}
				else
				{
					int left = (int)Math.Round(reference.Left / 1280.0 * width, MidpointRounding.AwayFromZero);
					int top = (int)Math.Round(reference.Top / 800.0 * height, MidpointRounding.AwayFromZero);
					int right = (int)Math.Round(reference.Right / 1280.0 * width, MidpointRounding.AwayFromZero);
					int bottom = (int)Math.Round(reference.Bottom / 800.0 * height, MidpointRounding.AwayFromZero);

					card3 = Navigation.CaptureRegion(new RECT(left, top, right, bottom));
				}

				// logic for continuing to run
				if (!CompareBitmapsFast(card1, card2) && !CompareBitmapsFast(card2, card3))
				{
					Navigation.SetDelay(Navigation.GetDelay() - delayOffset);
					//Navigation.SystemRandomWait();

					Navigation.SetCursor(item1.Center().X, item1.Center().Y);
					Navigation.Click();
					//Navigation.SystemRandomWait();
				}
				else
				{
					if (bStoppedOnce)
					{
                    }
                    bStoppedOnce = true;
				}
			} while (!bStoppedOnce && ( Navigation.GetDelay() - delayOffset > 0 ));

			// delay of compare function
			Navigation.SetDelay(Navigation.GetDelay() + 7);
			Debug.WriteLine($"Delay found:  {Navigation.GetDelay()}");
			card1.Dispose(); card2.Dispose();

			// set back to first element
			Navigation.SystemWait(Navigation.Speed.Slowest);
			Navigation.SetCursor(item1.Center().X, item1.Center().Y);
			Navigation.Click();
			Navigation.SystemWait(Navigation.Speed.Slower);
		}

        #region Image Operations

        internal static Bitmap ResizeImage(System.Drawing.Image image, int width, int height)
		{
			var destRect = new Rectangle(0, 0, width, height);
			var destImage = new Bitmap(width, height);

			destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

			using (var graphics = Graphics.FromImage(destImage))
			{
				graphics.CompositingMode = CompositingMode.SourceCopy;
				graphics.CompositingQuality = CompositingQuality.HighQuality;
				graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
				graphics.SmoothingMode = SmoothingMode.HighQuality;
				graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

				using (var wrapMode = new ImageAttributes())
				{
					wrapMode.SetWrapMode(WrapMode.TileFlipXY);
					graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
				}
			}

			return destImage;
		}

		internal static Bitmap ResizeImage(Bitmap image, Size tSize)
		{
			int targetWidth = (int)Math.Round((double)image.Width * (tSize.Height / tSize.Width));
			int targetHeight = (int)Math.Round((double)image.Height * (tSize.Height / tSize.Width));
			using (var reSized = new Bitmap(targetWidth, targetHeight))
			using (var g = Graphics.FromImage(reSized))
			{
				g.DrawImage(image, 0,0, targetWidth, targetHeight);
				return (Bitmap)reSized.Clone();
			}
		}

		internal static Bitmap ScaleImage(System.Drawing.Image image, double factor)
		{
			return ResizeImage(image, (int)( image.Width * factor ), (int)( image.Height * factor ));
		}

		internal static bool CompareColors(Color a, Color b)
		{
			int[] diff = new int[3];
			diff[0] = Math.Abs(a.R - b.R);
			diff[1] = Math.Abs(a.G - b.G);
			diff[2] = Math.Abs(a.B - b.B);

			return diff[0] < 10 && diff[1] < 10 && diff[2] < 10;
		}

		internal static Color ClosestColor(List<Color> colors, Color c2)
		{
			var diff = colors.Select(x => new { Value = x, Diff = GetColorDifference(x, c2) }).ToList();

			foreach (var c in colors)
            {
                if (CompareColors(c, c2)) return c;
            }

            return diff.Find(x=> x.Diff == diff.Min(y=>y.Diff)).Value;
		}

        private static int GetColorDifference(Color c, Color c2)
        {
			int r = c.R - c2.R, g = c.G - c2.G, b = c.B - c2.B;
			return r*r + g*g + b*b;
		}

        internal static void SetGamma(double red, double green, double blue, ref Bitmap bitmap)
		{
			Bitmap temp = bitmap;
			Bitmap bmap = (Bitmap)temp.Clone();
			Color c;
			byte[] redGamma = CreateGammaArray(red);
			byte[] greenGamma = CreateGammaArray(green);
			byte[] blueGamma = CreateGammaArray(blue);
			for (int i = 0; i < bmap.Width; i++)
			{
				for (int j = 0; j < bmap.Height; j++)
				{
					c = bmap.GetPixel(i, j);
					bmap.SetPixel(i, j, Color.FromArgb(redGamma[c.R],
					   greenGamma[c.G], blueGamma[c.B]));
				}
			}
			bitmap = (Bitmap)bmap.Clone();
		}

		private static byte[] CreateGammaArray(double color)
		{
			byte[] gammaArray = new byte[256];
			for (int i = 0; i < 256; ++i)
			{
				gammaArray[i] = (byte)Math.Min(255,
		(int)( ( 255.0 * Math.Pow(i / 255.0, 1.0 / color) ) + 0.5 ));
			}
			return gammaArray;
		}

		internal static void SetColor(string colorFilterType, ref Bitmap bitmap)
		{
			Bitmap temp = bitmap;
			Bitmap bmap = (Bitmap)temp.Clone();
			Color c;
			for (int i = 0; i < bmap.Width; i++)
			{
				for (int j = 0; j < bmap.Height; j++)
				{
					c = bmap.GetPixel(i, j);
					int nPixelR = 0;
					int nPixelG = 0;
					int nPixelB = 0;
					if (colorFilterType == "red")
					{
						nPixelR = c.R;
						nPixelG = c.G - 255;
						nPixelB = c.B - 255;
					}
					else if (colorFilterType == "green")
					{
						nPixelR = c.R - 255;
						nPixelG = c.G;
						nPixelB = c.B - 255;
					}
					else if (colorFilterType == "blue")
					{
						nPixelR = c.R - 255;
						nPixelG = c.G - 255;
						nPixelB = c.B;
					}
					nPixelR = Math.Max(nPixelR, 0);
					nPixelR = Math.Min(255, nPixelR);

					nPixelG = Math.Max(nPixelG, 0);
					nPixelG = Math.Min(255, nPixelG);

					nPixelB = Math.Max(nPixelB, 0);
					nPixelB = Math.Min(255, nPixelB);

					bmap.SetPixel(i, j, Color.FromArgb((byte)nPixelR,
					  (byte)nPixelG, (byte)nPixelB));
				}
			}
			bitmap = (Bitmap)bmap.Clone();
		}

		internal static void SetBrightness(int brightness, ref Bitmap bitmap)
		{
			if (brightness < -255) brightness = -255;
			if (brightness > 255) brightness = 255;

			Bitmap temp = bitmap;
			Bitmap bmap = (Bitmap)temp.Clone();
			Color c;
			for (int i = 0; i < bmap.Width; i++)
			{
				for (int j = 0; j < bmap.Height; j++)
				{
					c = bmap.GetPixel(i, j);
					int cR = c.R + brightness;
					int cG = c.G + brightness;
					int cB = c.B + brightness;

					if (cR < 0) cR = 1;
					if (cR > 255) cR = 255;

					if (cG < 0) cG = 1;
					if (cG > 255) cG = 255;

					if (cB < 0) cB = 1;
					if (cB > 255) cB = 255;

					bmap.SetPixel(i, j, Color.FromArgb((byte)cR, (byte)cG, (byte)cB));
				}
			}
			bitmap = (Bitmap)bmap.Clone();
		}

		internal static bool CompareBitmapsFast(Bitmap bmp1, Bitmap bmp2)
		{
			if (bmp1 == null || bmp2 == null)
				return false;
			if (object.Equals(bmp1, bmp2))
				return true;
			if (!bmp1.Size.Equals(bmp2.Size) || !bmp1.PixelFormat.Equals(bmp2.PixelFormat))
				return false;

			int bytes = bmp1.Width * bmp1.Height * (System.Drawing.Image.GetPixelFormatSize(bmp1.PixelFormat) / 8);

			bool result = true;
			byte[] b1bytes = new byte[bytes];
			byte[] b2bytes = new byte[bytes];

			BitmapData bitmapData1 = bmp1.LockBits(new Rectangle(0, 0, bmp1.Width, bmp1.Height), ImageLockMode.ReadOnly, bmp1.PixelFormat);
			BitmapData bitmapData2 = bmp2.LockBits(new Rectangle(0, 0, bmp2.Width, bmp2.Height), ImageLockMode.ReadOnly, bmp2.PixelFormat);

			Marshal.Copy(bitmapData1.Scan0, b1bytes, 0, bytes);
			Marshal.Copy(bitmapData2.Scan0, b2bytes, 0, bytes);

			for (int n = 0; n <= bytes - 1; n++)
			{
				if (b1bytes[n] != b2bytes[n])
				{
					result = false;
					break;
				}
			}

			bmp1.UnlockBits(bitmapData1);
			bmp2.UnlockBits(bitmapData2);

			return result;
		}

		internal static Bitmap CopyBitmap(Bitmap source, Rectangle region)
		{
			ClipToSource(source, ref region);
            return source.Clone(region, source.PixelFormat);

			void ClipToSource(Bitmap s, ref Rectangle r)
			{
				if (r.X + r.Width > source.Width) { r.Width = s.Width - r.X; }
				if (r.Y + r.Height > source.Height) { r.Height = s.Height - r.Y; }
			}
        }

		#endregion Image Operations

		internal static string ConvertToGood(this string text)
		{
            text = text.ToLower();
            var pascal = CultureInfo.GetCultureInfo("en-US").TextInfo.ToTitleCase(text);
            return Regex.Replace(pascal, @"[\W]", string.Empty);
        }

        internal static bool CharacterMatchesElement(string name, string element)
        {
            return !string.IsNullOrWhiteSpace(name.ToLower()) && GetCharactersElements(name.ToLower()).Contains(element.ToLower());
        }

        internal static List<string> GetCharactersElements(string name)
		{
            if (string.IsNullOrWhiteSpace(name.ToLower()))
            {
                return new List<string>();
            }
            else
            {
                if (Characters.TryGetValue(name.ToLower(), out var data))
                {
                    return data["Element"].ToObject<List<string>>();
                }
                else
                {
                    return null;
                }
            }
        }
    }
}