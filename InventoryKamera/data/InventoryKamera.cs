using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace InventoryKamera
{
    public class InventoryKamera
	{

		private static NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		[JsonProperty]
		public List<Character> Characters;

		[JsonProperty]
		public Inventory Inventory;

		private List<Artifact> equippedArtifacts;
		private List<Weapon> equippedWeapons;

		/// <summary>
		/// Hand-off from the weapon/artifact scan loops (producers) to the background OCR workers
		/// (consumers). Producers call <see cref="Channel{T}.Writer"/>.TryWrite; a normal end of work
		/// is signaled by completing the writer (see <see cref="AwaitProcessors"/>/GatherData), which
		/// lets ReadAllAsync drain every already-queued item before finishing. An abrupt stop (see
		/// <see cref="StopImageProcessorWorkers"/>) instead cancels <see cref="workerAbortCts"/>, which
		/// drops whatever is still queued.
		/// </summary>
		public static Channel<OCRImageCollection> workerChannel;
		private CancellationTokenSource workerAbortCts;
		private List<Task> imageProcessorTasks;

		private readonly IOcrService ocrService;
		private readonly IImagePreprocessor imagePreprocessor;
		private readonly IScanSettings scanSettings;
		private readonly IScanProgressReporter progressReporter;

		private WeaponScraper weaponScraper;
		private ArtifactScraper artifactScraper;
		private CharacterScraper characterScraper;
		private MaterialScraper materialScraper;

		private readonly int NumWorkers;

		/// <summary>
		/// Set when the user requests the scan be stopped (e.g. the Stop hotkey). Checked between
		/// scan phases and within each scraper's per-item loop, since .NET no longer supports
		/// Thread.Abort for interrupting the scan thread outright.
		/// </summary>
		internal static volatile bool CancelRequested;

		public bool HasData
        {
			get { return Characters.Count > 0 || Inventory.Size > 0; }
        }

		/// <param name="progressReporter">
		/// Owned by the caller (<see cref="MainForm"/>), not constructed here: unlike
		/// <c>ocrService</c>/<c>imagePreprocessor</c>/<c>scanSettings</c>, a <see cref="ScanViewModel"/>
		/// needs to outlive any single <see cref="InventoryKamera"/> instance (MainForm recreates one
		/// per scan) so its subscribers don't have to re-subscribe every time.
		/// </param>
		internal InventoryKamera(IScanProgressReporter progressReporter)
		{
			Characters = new List<Character>();
			Inventory = new Inventory();
			equippedArtifacts = new List<Artifact>();
			equippedWeapons = new List<Weapon>();
			imageProcessorTasks = new List<Task>();
			workerChannel = Channel.CreateUnbounded<OCRImageCollection>();
			workerAbortCts = new CancellationTokenSource();

			ocrService = new OcrService();
			imagePreprocessor = new ImageProcessor();
			scanSettings = new ScanSettings();
			this.progressReporter = progressReporter;

			weaponScraper = new WeaponScraper(ocrService, imagePreprocessor, scanSettings, progressReporter);
			artifactScraper = new ArtifactScraper(ocrService, imagePreprocessor, scanSettings, progressReporter);
			characterScraper = new CharacterScraper(ocrService, imagePreprocessor, scanSettings, progressReporter);
			materialScraper = new MaterialScraper(ocrService, imagePreprocessor, scanSettings, progressReporter);

			// Base worker count on available CPU (leaving headroom for the UI/navigation thread) so
			// small machines don't oversubscribe; the scanner-speed setting further caps it down for
			// the slower, lower-load profiles.
			int baseWorkers = Math.Max(1, Environment.ProcessorCount - 1);
			int userCap = scanSettings.ScannerDelay == 0 ? 3 : 2;
			NumWorkers = Math.Min(baseWorkers, userCap);

			Logger.Info("Kamera initialized");
		}

		public void ResetLogging()
		{
			try
			{
				Directory.Delete("./logging/weapons", true);
				Directory.Delete("./logging/artifacts", true);
				Directory.Delete("./logging/characters", true);
				Directory.Delete("./logging/materials", true);
			}
			catch { }

			Directory.CreateDirectory("./logging/weapons");
			Directory.CreateDirectory("./logging/artifacts");
			Directory.CreateDirectory("./logging/characters");
			Directory.CreateDirectory("./logging/materials");

			Logger.Info("Logging directory reset");
		}

		public void StopImageProcessorWorkers()
		{
			workerAbortCts.Cancel();
			AwaitProcessors();
			workerChannel = Channel.CreateUnbounded<OCRImageCollection>();
			workerAbortCts = new CancellationTokenSource();
		}

		public void GatherData()
		{
			CancelRequested = false;

			ResetLogging();

			GenshinProcesor.ReloadData();

			// Initialize Image Processors
			for (int i = 0; i < NumWorkers; i++)
			{
				imageProcessorTasks.Add(Task.Run(() => ImageProcessorWorkerAsync(workerAbortCts.Token)));
			}
			Logger.Debug("Added {NumWorkers} workers", NumWorkers);

			ocrService.Restart();


			// Assign Traveler's custom name
			GenshinProcesor.AssignTravelerName(scanSettings.TravelerName, ocrService, imagePreprocessor, progressReporter);

            // Assign Wanderer's custom name
            GenshinProcesor.UpdateCharacterName("wanderer", scanSettings.WandererName);

			// GenshinProcesor.ReloadData() (above) ensures manequin1/manequin2 exist in characters.json
			// via the JSON object model, so these are now guaranteed to succeed.
			GenshinProcesor.UpdateCharacterName("manequin1", scanSettings.Manequin1Name);
			GenshinProcesor.UpdateCharacterName("manequin2", scanSettings.Manequin2Name);

            if (scanSettings.ScanWeapons && !CancelRequested)
			{
				Logger.Info("Scanning weapons...");
				// Get Weapons
				Navigation.InventoryScreen();
				Navigation.SelectWeaponInventory();
				try
				{
                    weaponScraper.ScanWeapons();
				}
				catch (FormatException ex) { progressReporter.AddError(ex.Message); }
				catch (Exception ex)
				{
					progressReporter.AddError(ex.Message + "\n" + ex.StackTrace);
				}
				Navigation.MainMenuScreen();
				Logger.Info("Done scanning weapons");
			}

			if (scanSettings.ScanArtifacts && !CancelRequested)
			{
				Logger.Info("Scanning artifacts...");

				// Get Artifacts
				Navigation.InventoryScreen();
				Navigation.SelectArtifactInventory();
				try
				{
					artifactScraper.ScanArtifacts();
				}
				catch (FormatException ex) { progressReporter.AddError(ex.Message); }
				catch (Exception ex)
				{
					progressReporter.AddError(ex.Message + "\n" + ex.StackTrace);
				}
				Navigation.MainMenuScreen();
				Logger.Info("Done scanning artifacts");
			}

			// No more weapon/artifact items will be queued; workers drain whatever's left, then finish.
			workerChannel.Writer.Complete();

			if (scanSettings.ScanCharacters && !CancelRequested)
			{
				Logger.Info("Scanning characters...");
				// Get characters
				Navigation.CharacterScreen();
				try
				{
					characterScraper.ScanCharacters(ref Characters);
				}
				catch (Exception ex)
				{
					progressReporter.AddError(ex.Message + "\n" + ex.StackTrace);
				}
				Navigation.MainMenuScreen();
				Logger.Info("Done scanning characters");
			}

			// Wait for Image Processors to finish
			AwaitProcessors();

			if (scanSettings.ScanCharacters)
			{
				// Assign Artifacts to Characters
				if (scanSettings.ScanArtifacts)
					AssignArtifacts();
				if (scanSettings.ScanWeapons)
					AssignWeapons();
			}

			// Scan Character Development Items
			if (scanSettings.ScanCharDevItems && !CancelRequested)
			{
				Logger.Info("Scanning character development materials...");
				// Get Materials
				Navigation.InventoryScreen();
				Navigation.SelectCharacterDevelopmentInventory();
				HashSet<Material> devItems = new HashSet<Material>();
				try
				{
					materialScraper.SetInventoryPage(InventoryPage.CharacterDevelopmentItems);
					materialScraper.Scan_Materials(ref Inventory);
				}
				catch (FormatException ex) { progressReporter.AddError(ex.Message); }
				catch (Exception ex)
				{
					progressReporter.AddError(ex.Message + "\n" + ex.StackTrace);
				}
				Navigation.MainMenuScreen();
				Logger.Info("Done scanning character development materials");
			}

			// Scan Materials
			if (scanSettings.ScanMaterials && !CancelRequested)
			{
				Logger.Info("Scanning materials...");
				// Get Materials
				Navigation.InventoryScreen();
				Navigation.SelectMaterialInventory();
				HashSet<Material> materials = new HashSet<Material>();
				try
				{
					materialScraper.SetInventoryPage(InventoryPage.Materials);
					materialScraper.Scan_Materials(ref Inventory);
				}
				catch (FormatException ex) { progressReporter.AddError(ex.Message); }
				catch (Exception ex)
				{
					progressReporter.AddError(ex.Message + "\n" + ex.StackTrace);
				}
				Navigation.MainMenuScreen();
				Logger.Info("Done scanning materials");
			}
		}

		private void AwaitProcessors()
		{
			Task.WaitAll(imageProcessorTasks.ToArray());
			imageProcessorTasks.Clear();
		}

		public async Task ImageProcessorWorkerAsync(CancellationToken abortToken)
		{
			Logger.Debug("Worker task starting");
			try
			{
				await foreach (var imageCollection in workerChannel.Reader.ReadAllAsync(abortToken))
				{
					try
					{
						await ProcessImageCollectionAsync(imageCollection);
					}
					catch (Exception ex)
					{
						// A single bad item shouldn't take the whole worker down; log and keep going.
						Logger.Error(ex, "Image processor worker failed on a queued {Type} item", imageCollection.Type);
					}
				}
			}
			catch (OperationCanceledException)
			{
				// Abrupt stop requested (StopImageProcessorWorkers); drop whatever's still queued.
				Logger.Debug("Worker task cancelled");
			}
			Logger.Debug("Worker task exit");
		}

		private async Task ProcessImageCollectionAsync(OCRImageCollection imageCollection)
		{
					switch (imageCollection.Type)
					{
						case "weapon":
							if (weaponScraper.IsEnhancementMaterial(imageCollection.Bitmaps.First()))
							{
								Logger.Debug("Enhancement Material found for weapon #{weaponID}", imageCollection.Id);
								weaponScraper.StopScanning = true;
								break;
							}

							progressReporter.SetGearPictureBox(imageCollection.Bitmaps.Last());

							// Scan as weapon
							Weapon weapon = await weaponScraper.CatalogueFromBitmapsAsync(imageCollection.Bitmaps, imageCollection.Id);
							progressReporter.SetGear(imageCollection.Bitmaps.Last(), weapon);

							string weaponPath = $"./logging/weapons/weapon{weapon.Id}/";

							if (scanSettings.LogScreenshots) Directory.CreateDirectory(weaponPath);

							if (weapon.IsValid())
							{
								progressReporter.IncrementWeaponCount();
								Inventory.Add(weapon);
								if (!string.IsNullOrWhiteSpace(weapon.EquippedCharacter))
									equippedWeapons.Add(weapon);
							}
							else
							{
								progressReporter.AddError($"Unable to validate information for weapon ID#{weapon.Id}");
								string error = "";
								if (!weapon.HasValidWeaponName()) error += "Invalid weapon name\n"; 
								if (!weapon.HasValidRarity()) error += "Invalid weapon rarity\n";
								if (!weapon.HasValidLevel()) error += "Invalid weapon level\n";
								if (!weapon.HasValidRefinementLevel()) error += "Invalid refinement level\n";
								if (!weapon.HasValidEquippedCharacter()) error += "Inavlid equipped character\n";
								progressReporter.AddError(error + weapon.ToString());
								Directory.CreateDirectory(weaponPath);
								using (var writer = File.CreateText(weaponPath + "log.txt"))
								{
									writer.WriteLine($"Version: {Assembly.GetExecutingAssembly().GetName().Version.ToString(3)}");
									writer.WriteLine($"Resolution: {Navigation.GetWidth()}x{Navigation.GetHeight()}");
									writer.WriteLine($"Error log:\n\t{error.Replace("\n", "\n\t")}");
								}
							}

                            if (!weapon.IsValid() || scanSettings.LogScreenshots)
                            {
                                Directory.CreateDirectory(weaponPath + "name");
                                imageCollection.Bitmaps[0].Save(weaponPath + "name/name.png");
                                Directory.CreateDirectory(weaponPath + "rarity");
                                imageCollection.Bitmaps[0].Save(weaponPath + "rarity/rarity.png");
                                Directory.CreateDirectory(weaponPath + "level");
                                imageCollection.Bitmaps[1].Save(weaponPath + "level/level.png");
                                Directory.CreateDirectory(weaponPath + "refinement");
                                imageCollection.Bitmaps[2].Save(weaponPath + "refinement/refinement.png");
                                Directory.CreateDirectory(weaponPath + "equipped");
                                imageCollection.Bitmaps[4].Save(weaponPath + "equipped/equipped.png");

                                imageCollection.Bitmaps.Last().Save(weaponPath + "card.png");
								Task.Run(() => LogObject(weapon, weaponPath + "weapon.json"));
                            }

                            // Dispose of everything
                            imageCollection.Bitmaps.ForEach(b => b.Dispose());
							break;

						case "artifact":
							if (artifactScraper.IsEnhancementMaterial(imageCollection.Bitmaps.Last()))
							{
								Logger.Debug("Enhancement Material found for artifact #{artifactID}", imageCollection.Id);
								artifactScraper.StopScanning = true;
								break;
							}

							progressReporter.SetGearPictureBox(imageCollection.Bitmaps.Last());
							// Scan as artifact
							Artifact artifact = await artifactScraper.CatalogueFromBitmapsAsync(imageCollection.Bitmaps, imageCollection.Id);
							progressReporter.SetGear(imageCollection.Bitmaps.Last(), artifact);

							string artifactPath = $"./logging/artifacts/artifact{artifact.Id}/";

                            if (scanSettings.LogScreenshots) Directory.CreateDirectory(artifactPath);

							if (artifact.IsValid())
							{
								progressReporter.IncrementArtifactCount();
								Inventory.Add(artifact);
								if (!string.IsNullOrWhiteSpace(artifact.EquippedCharacter))
									equippedArtifacts.Add(artifact);
							}
							else
							{
								progressReporter.AddError($"Unable to validate information for artifact ID#{artifact.Id}");
								string error = "";
								if (!artifact.HasValidSlot()) error += "Invalid artifact gear slot\n";
								if (!artifact.HasValidSetName()) error += "Invalid artifact set name\n";
								if (!artifact.HasValidRarity()) error += "Invalid artifact rarity\n";
								if (!artifact.HasValidLevel()) error += "Invalid artifact level\n";
								if (!artifact.HasValidMainStat()) error += "Invalid artifact main stat\n";
								if (!artifact.HasValidSubStats()) error += "Invalid artifact sub stats\n";
								if (!artifact.HasValidEquippedCharacter()) error += "Invalid equipped character\n";
								progressReporter.AddError(error + artifact.ToString());
								Directory.CreateDirectory(artifactPath);
								using (var writer = File.CreateText(artifactPath + "log.txt"))
								{
									writer.WriteLine($"Version: {Assembly.GetExecutingAssembly().GetName().Version.ToString(3)}");
									writer.WriteLine($"Resolution: {Navigation.GetWidth()}x{Navigation.GetHeight()}");
									writer.WriteLine($"Error Log:\n\t{error.Replace("\n", "\n\t")}");
								}
							}

                            if (!artifact.IsValid() || scanSettings.LogScreenshots)
                            {
                                Directory.CreateDirectory(artifactPath + "name");
                                imageCollection.Bitmaps[0].Save(artifactPath + "name/name.png");
                                Directory.CreateDirectory(artifactPath + "slot");
								imageCollection.Bitmaps[1].Save(artifactPath + "slot/slot.png");
                                Directory.CreateDirectory(artifactPath + "mainstat");
                                imageCollection.Bitmaps[2].Save(artifactPath + "mainstat/mainstat.png");
								Directory.CreateDirectory(artifactPath + "level");
								imageCollection.Bitmaps[3].Save(artifactPath + "level/level.png");
								Directory.CreateDirectory(artifactPath + "substats");
								imageCollection.Bitmaps[4].Save(artifactPath + "substats/substats.png");
								Directory.CreateDirectory(artifactPath + "equipped");
								imageCollection.Bitmaps[5].Save(artifactPath + "equipped/equipped.png");
								Directory.CreateDirectory(artifactPath + "locked");
								imageCollection.Bitmaps[6].Save(artifactPath + "locked/locked.png");
                                Directory.CreateDirectory(artifactPath + "sanctify");
                                imageCollection.Bitmaps[7].Save(artifactPath + "sanctify/sanctify.png");


                                imageCollection.Bitmaps.Last().Save(artifactPath + "card.png");

								Task.Run(()=>LogObject(artifact, artifactPath + "artifact.json"));
							}

							// Dispose of everything
							imageCollection.Bitmaps.ForEach(b => b.Dispose());
							break;

						default:
							MainForm.UnexpectedError("Unknown Image type for Image Processor");
							break;
					}
		}

        private static void LogObject(object obj, string path)
        {
            using (var file = new StreamWriter(path))
            {
                var serializer = new JsonSerializer
                {
                    Formatting = Formatting.Indented
                };
                serializer.Serialize(file, obj);
            }
        }

        public void AssignArtifacts()
		{
			foreach (Artifact artifact in equippedArtifacts)
			{
				foreach (Character character in Characters)
				{
					if (artifact.EquippedCharacter == character.NameGOOD)
					{
						character.AssignArtifact(artifact); // Do we even need to do this?
						Logger.Debug("Assigned {fearSlot} to {character}", artifact.GearSlot, character.NameGOOD);
						break;
					}
				}
			}
		}

		public void AssignWeapons()
		{
			foreach (Character character in Characters)
			{
				foreach (Weapon weapon in equippedWeapons)
				{
					if (weapon.EquippedCharacter == character.NameGOOD)
					{
						character.AssignWeapon(weapon);
						Logger.Debug("Assigned {weapon} to {character}", weapon.Name, character.NameGOOD);
						break;
					}
				}
				if (character.Weapon is null)
				{
					Inventory.Add(new Weapon(character.WeaponType, character.NameGOOD));
					Logger.Info("Default weapon assigned to {character}", character.NameGOOD);
				}
			}
		}
	}
}