using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;


namespace InventoryKamera
{
    public class DatabaseManager
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private string _listdir = @".\inventorylists\";

        public string ListsDir
        {
            get
            {
                Directory.CreateDirectory(_listdir);
                return _listdir;
            }
            set { _listdir = value; }
        }

        private const string WeaponsJson = "weapons.json";
        private const string ArtifactsJson = "artifacts.json";
        private const string CharactersJson = "characters.json";
        private const string MaterialsJson = "materials.json";

        private readonly string NewVersion = "version.txt";

        private const string commitsAPIURL = "https://gitlab.com/api/v4/projects/Dimbreath%2FAnimeGameData2/repository/commits?per_page=100";
        private const string repoRawBaseURL = "https://gitlab.com/Dimbreath/AnimeGameData2/-/raw/";
        private const string TextMapEnPath = "TextMap/TextMapEN.json";
        private const string TextMapMediumEnPath = "TextMap/TextMap_MediumEN.json";
        private const string CharactersPath = "ExcelBinOutput/AvatarExcelConfigData.json";
        private const string ConstellationsPath = "ExcelBinOutput/FetterInfoExcelConfigData.json";
        private const string TalentsPath = "ExcelBinOutput/AvatarTalentExcelConfigData.json";
        private const string SkillsPath = "ExcelBinOutput/AvatarSkillExcelConfigData.json";

        private const string ArtifactsDisplayItemPath = "ExcelBinOutput/DisplayItemExcelConfigData.json";
        private const string ArtifactsCodexPath = "ExcelBinOutput/ReliquaryCodexExcelConfigData.json";
        private const string ArtifactSetConfigPath = "ExcelBinOutput/ReliquaryExcelConfigData.json";

        private const string WeaponsPath = "ExcelBinOutput/WeaponExcelConfigData.json";
        private const string MaterialsPath = "ExcelBinOutput/MaterialExcelConfigData.json";
        private const string UpdateManifestJson = "update_manifest.json";

        private ConcurrentDictionary<string, string> Mappings = new ConcurrentDictionary<string, string>();

        internal Version LocalVersion = new Version();
        internal Version RemoteVersion = new Version();
        internal string SourceHeadCommitSha = "";
        internal string VersionMarkerCommitSha = "";

        private static readonly List<string> elements = new List<string>
        {
            "Pyro",
            "Hydro",
            "Dendro",
            "Electro",
            "Anemo",
            "Cryo",
            "Geo",
        };

        private static readonly List<string> slots = new List<string>
        {
            "flower of life",
            "plume of death",
            "sands of eon",
            "goblet of eonothem",
            "circlet of logos",
        };

        public DatabaseManager()
        {
            Directory.CreateDirectory(ListsDir);


            if (!File.Exists(ListsDir + NewVersion)) File.WriteAllText(ListsDir + NewVersion, new Version().ToString());


            LocalVersion = new Version(File.ReadAllText(ListsDir + NewVersion));


            if (!(File.Exists(ListsDir + WeaponsJson) &&
                File.Exists(ListsDir + ArtifactsJson) &&
                File.Exists(ListsDir + CharactersJson) &&
                File.Exists(ListsDir + MaterialsJson)))
            {
                var bootstrapStatus = UpdateGameData(force: true);

                if (bootstrapStatus == UpdateStatus.Fail ||
                    !(File.Exists(ListsDir + WeaponsJson) &&
                      File.Exists(ListsDir + ArtifactsJson) &&
                      File.Exists(ListsDir + CharactersJson) &&
                      File.Exists(ListsDir + MaterialsJson)))
                {
                    throw new InvalidOperationException(
                        "Initial lookup-table generation failed. Previous data was preserved when available; see InventoryKamera.debug.log for the exact update failure.");
                }
            }
        }

        public bool UpdateAvailable()
        {
            Logger.Info("Checking for newer game data...");
            RemoteVersion = CheckRemoteVersion();
            return RemoteVersion == null
                ? throw new NullReferenceException("Could not get current Genshin version.")
                : RemoteVersion.CompareTo(LocalVersion) > 0;
        }

        private Version CheckRemoteVersion()
        {
            Version latest = null;
            string latestVersionCommit = "";
            string headCommit = "";

            // Scan multiple pages so version discovery does not silently fail if the
            // repository has more than 100 commits between game-version marker commits.
            for (int page = 1; page <= 5; page++)
            {
                var url = commitsAPIURL + "&page=" + page;

                string pattern = @"(\d+\.\d+\.\d+)";
                string body = LoadJsonFromURLAsync(url);
                var commits = JsonConvert.DeserializeObject<List<JObject>>(body) ?? new List<JObject>();

                if (page == 1 && commits.Count > 0)
                    headCommit = commits[0]["id"]?.ToString() ?? "";

                if (commits.Count == 0)
                    break;

                foreach (var commit in commits)
                {
                    var title = commit["title"]?.ToString() ?? string.Empty;
                    var match = Regex.Match(title, pattern);

                    if (!match.Success) continue;

                    if (Version.TryParse(match.Groups[1].Value, out var parsed) &&
                        (latest == null || parsed.CompareTo(latest) > 0))
                    {
                        latest = parsed;
                        latestVersionCommit = commit["id"]?.ToString() ?? "";
                    }
                }

                // Commits are returned newest first. Once the first page contains
                // a semantic game-version marker, older pages cannot contain a
                // newer game version.
                if (page == 1 && latest != null)
                    break;
            }

            if (latest != null)
            {
                SourceHeadCommitSha = headCommit;
                VersionMarkerCommitSha = latestVersionCommit;

                Logger.Info(
                    "Latest game data version {0}; source head={1}; version marker={2}",
                    latest,
                    string.IsNullOrWhiteSpace(SourceHeadCommitSha) ? "unknown" : SourceHeadCommitSha,
                    string.IsNullOrWhiteSpace(VersionMarkerCommitSha) ? "unknown" : VersionMarkerCommitSha);
            }

            return latest;
        }

        private string BuildDataURL(string relativePath)
        {
            var revision = string.IsNullOrWhiteSpace(SourceHeadCommitSha)
                ? "main"
                : SourceHeadCommitSha;

            return repoRawBaseURL + revision + "/" + relativePath.TrimStart('/');
        }

        public Dictionary<string, JObject> LoadCharacters()
        {
            return GetList(ListType.Characters).ToObject<Dictionary<string, JObject>>();
        }

        public Dictionary<string, string> LoadWeapons()
        {
            return GetList(ListType.Weapons).ToObject<Dictionary<string, string>>();
        }

        public Dictionary<string, JObject> LoadArtifacts()
        {
            return GetList(ListType.Artifacts).ToObject<Dictionary<string, JObject>>();
        }

        public Dictionary<string, string> LoadMaterials()
        {
            return GetList(ListType.Materials).ToObject<Dictionary<string, string>>();
        }

        public Dictionary<string, string> LoadDevItems()
        {
            return GetList(ListType.CharacterDevelopmentItems).ToObject<Dictionary<string, string>>();
        }

        private JToken GetList(ListType list)
        {
            string file = "";
            switch (list)
            {
                case ListType.Weapons:
                    file = WeaponsJson;
                    break;

                case ListType.Artifacts:
                    file = ArtifactsJson;
                    break;

                case ListType.Characters:
                    file = CharactersJson;
                    break;

                case ListType.CharacterDevelopmentItems:
                case ListType.Materials:
                    file = MaterialsJson;
                    break;

                default:
                    break;
            }

            if (!File.Exists(ListsDir + file)) throw new FileNotFoundException($"Data file does not exist for {list}.");
            string json = LoadJsonFromFile(file);
            return json == "{}"
                ? throw new FormatException($"Data file for {list} is invalid. Please try running the auto updater and try again.")
                : JToken.Parse(json);
        }

        public UpdateStatus UpdateGameData(bool force = false)
        {
            string backupDirectory = null;

            try
            {
                UpdateStatus overallStatus = UpdateStatus.Skipped;

                // Always refresh version/source metadata immediately before an update so
                // every downloaded table can be pinned to one immutable upstream commit.
                RemoteVersion = CheckRemoteVersion();
                if (RemoteVersion == null)
                    throw new InvalidOperationException("Could not determine the current Genshin data version.");

                if (!force && RemoteVersion.CompareTo(LocalVersion) <= 0)
                {
                    Logger.Info(
                        "Game data is already current. local={0}, remote={1}",
                        LocalVersion,
                        RemoteVersion);
                    return UpdateStatus.Skipped;
                }

                if (force)
                {
                    Logger.Info("Forcing update for game data");
                    Properties.Settings.Default.LastUpdateCheck = DateTime.MinValue;
                }

                // New game versions are rebuilt from source instead of merged into the
                // previous lookup tables. Merging can preserve stale metadata for existing
                // characters/items when upstream changes an already-known record.
                bool rebuildFromSource = true;

                Logger.Info(
                    "Game data update plan: local={0}, remote={1}, rebuildFromSource={2}, sourceHead={3}",
                    LocalVersion,
                    RemoteVersion,
                    rebuildFromSource,
                    string.IsNullOrWhiteSpace(SourceHeadCommitSha) ? "unknown" : SourceHeadCommitSha);

                backupDirectory = CreateUpdateBackup();
                LoadMappings();

                var lists = Enum.GetValues(typeof(ListType)).Cast<ListType>().ToList();
                lists.RemoveAll(e => e == ListType.CharacterDevelopmentItems);

                // Deterministic sequential updates avoid shared status races and make the
                // update log reproducible. The datasets are small enough that the previous
                // PLINQ fan-out provided little practical benefit.
                foreach (var list in lists)
                {
                    var listStatus = UpdateList(list, rebuildFromSource);

                    if (listStatus == UpdateStatus.Fail)
                    {
                        overallStatus = UpdateStatus.Fail;
                        Logger.Error("Failed to update {0} data", list);
                        break;
                    }

                    if (listStatus == UpdateStatus.Skipped)
                    {
                        overallStatus = UpdateStatus.Fail;
                        Logger.Error(
                            "Update for {0} unexpectedly produced no replacement data while rebuilding from source",
                            list);
                        break;
                    }

                    overallStatus = UpdateStatus.Success;
                }

                if (overallStatus == UpdateStatus.Fail)
                {
                    Logger.Error("Could not update all information for version {0}; restoring previous lookup set", RemoteVersion);
                    RestoreUpdateBackup(backupDirectory);
                    return UpdateStatus.Fail;
                }

                LocalVersion = RemoteVersion;

                if (!SaveTextToFileAtomically(LocalVersion.ToString(), NewVersion))
                {
                    Logger.Error("Failed to persist version metadata; restoring previous lookup set");
                    RestoreUpdateBackup(backupDirectory);
                    return UpdateStatus.Fail;
                }

                if (!WriteUpdateManifest())
                {
                    Logger.Error("Failed to persist update manifest; restoring previous lookup set");
                    RestoreUpdateBackup(backupDirectory);
                    return UpdateStatus.Fail;
                }

                DeleteUpdateBackup(backupDirectory);
                backupDirectory = null;

                Logger.Info(
                    "Game data update committed successfully. version={0}, sourceHead={1}",
                    LocalVersion,
                    string.IsNullOrWhiteSpace(SourceHeadCommitSha) ? "unknown" : SourceHeadCommitSha);

                return overallStatus;
            }
            catch (Exception e)
            {
                if (!string.IsNullOrWhiteSpace(backupDirectory))
                    RestoreUpdateBackup(backupDirectory);

                Logger.Error(e);
                return UpdateStatus.Fail;
            }
            finally
            {
                ReleaseMappings();
            }
        }

        private void LoadMappings()
        {
            lock (Mappings)
            {
                if (!Mappings.Any())
                {
                    var mapping = JObject.Parse(LoadJsonFromURLAsync(BuildDataURL(TextMapEnPath)))
                        .ToObject<Dictionary<string, string>>()
                        .Where(e => !string.IsNullOrWhiteSpace(e.Value)) // Remove any mapping with empty
                        .ToDictionary(i => i.Key, i => i.Value);

                    var mediumMapping = JObject.Parse(LoadJsonFromURLAsync(BuildDataURL(TextMapMediumEnPath)))
                        .ToObject<Dictionary<string, string>>()
                        .Where(e => !string.IsNullOrWhiteSpace(e.Value)) // Remove any mapping with empty
                        .ToDictionary(i => i.Key, i => i.Value);

                    foreach (var entry in mediumMapping)
                    {
                        mapping[entry.Key] = entry.Value; // Should be no overlap
                    }

                    Mappings = new ConcurrentDictionary<string, string>(mapping);
                }
            }
        }

        private void ReleaseMappings()
        {
            Mappings.Clear();
        }

        private string LoadJsonFromURLAsync(string url)
        {
            Exception lastException = null;

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using (var client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(60);

                        using (var response = client.GetAsync(url).Result)
                        {
                            response.EnsureSuccessStatusCode();
                            var json = response.Content.ReadAsStringAsync().Result;

                            if (string.IsNullOrWhiteSpace(json))
                                throw new InvalidDataException("Downloaded JSON was empty.");

                            var firstNonWhitespace = json.FirstOrDefault(c => !char.IsWhiteSpace(c));
                            if (firstNonWhitespace != '{' && firstNonWhitespace != '[')
                                throw new InvalidDataException("Downloaded content does not look like JSON.");

                            return json;
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Logger.Warn(
                        "Download attempt {0}/3 failed for {1}: {2}",
                        attempt,
                        url,
                        ex.Message);

                    if (attempt < 3)
                        Thread.Sleep(500 * attempt);
                }
            }

            throw new InvalidOperationException(
                "Failed to download required game data after 3 attempts: " + url,
                lastException);
        }

        private UpdateStatus UpdateList(ListType list, bool force = false)
        {
            UpdateStatus status = UpdateStatus.Success;

            Logger.Info("Updating {0}", list);

            try
            {
                switch (list)
                {
                    case ListType.Weapons:
                        status = UpdateWeapons(force);
                        break;

                    case ListType.Artifacts:
                        status = UpdateArtifacts(force);
                        break;

                    case ListType.Characters:
                        status = UpdateCharacters(force);
                        break;

                    case ListType.Materials:
                        status = UpdateMaterials(force);
                        break;

                    default:
                        break;
                }
                Logger.Info("Finished updating {0} ({1})", list, status);
            }
            catch (Exception e)
            {
                Logger.Error("Failed to update {0} data", list);
                Logger.Error(e);
                return UpdateStatus.Fail;
            }

            return status;
        }

        private UpdateStatus UpdateCharacters(bool force)
        {
            var status = UpdateStatus.Skipped;

            // Load existing data (or empty dict if file doesn't exist or force mode)
            var data = force
                ? new ConcurrentDictionary<string, JObject>()
                : JToken.Parse(LoadJsonFromFile(CharactersJson)).ToObject<ConcurrentDictionary<string, JObject>>();

            var newData = new ConcurrentDictionary<string, JObject>(data);

            try
            {
                var characters = JArray.Parse(LoadJsonFromURLAsync(BuildDataURL(CharactersPath))).ToObject<List<JObject>>();
                var constellations = JArray.Parse(LoadJsonFromURLAsync(BuildDataURL(ConstellationsPath))).ToObject<List<JObject>>();
                var talents = JArray.Parse(LoadJsonFromURLAsync(BuildDataURL(TalentsPath))).ToObject<List<JObject>>();
                var skills = JArray.Parse(LoadJsonFromURLAsync(BuildDataURL(SkillsPath))).ToObject<List<JObject>>();

                characters.RemoveAll(c => !c.ContainsKey("useType") || c["useType"].ToString() != "AVATAR_FORMAL");

                Logger.Debug("Found {0} playable characters available", characters.Count);

                int sourceFormalRows = characters.Count;
                int generatedRows = 0;
                int skippedFemaleTraveler = 0;
                int skippedInternalId = 0;
                int skippedKnownMannequin = 0;
                int skippedUnmappedName = 0;
                int skippedUnmappedSkill = 0;
                int skippedUnmappedC3 = 0;
                int skippedUnmappedC5 = 0;
                int skippedDuplicateKey = 0;
                int failedRows = 0;

                foreach (var character in characters)
                {
                    string name = "<unknown>";

                    try
                    {
                        if (!character.TryGetValue("nameTextMapHash", out var nameHashToken) ||
                            string.IsNullOrWhiteSpace(nameHashToken?.ToString()) ||
                            !Mappings.TryGetValue(nameHashToken.ToString(), out name))
                        {
                            skippedUnmappedName++;
                            Logger.Warn(
                                "Skipping character with unmapped name. id={0}, hash={1}, iconName={2}",
                                character["id"]?.ToString() ?? "missing",
                                nameHashToken?.ToString() ?? "missing",
                                character["iconName"]?.ToString() ?? "missing");
                            continue;
                        }

                        if (string.Equals(name, "PlayerGirl", StringComparison.OrdinalIgnoreCase))
                        {
                            skippedFemaleTraveler++;
                            Logger.Debug(
                                "Excluding duplicate female Traveler source row. id={0}, iconName={1}",
                                character["id"]?.ToString() ?? "missing",
                                character["iconName"]?.ToString() ?? "missing");
                            continue;
                        }

                        string PascalCase = CultureInfo.GetCultureInfo("en").TextInfo.ToTitleCase(name);
                        string nameGOOD = Regex.Replace(PascalCase, @"[\W]", string.Empty);
                        string nameKey = nameGOOD.ToLower();
                        int characterID = (int)character["id"];

                        if (characterID > 10000900)
                        {
                            skippedInternalId++;
                            Logger.Debug(
                                "Excluding internal/non-playable character row by ID range. name={0}, id={1}, iconName={2}",
                                nameGOOD,
                                characterID,
                                character["iconName"]?.ToString() ?? "missing");
                            continue;
                        }

                        if (string.Equals(nameGOOD, "Manekin", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(nameGOOD, "Manekina", StringComparison.OrdinalIgnoreCase))
                        {
                            skippedKnownMannequin++;
                            Logger.Debug(
                                "Excluding known mannequin/internal avatar row. name={0}, id={1}",
                                nameGOOD,
                                characterID);
                            continue;
                        }

                        string const3Description = "";
                        string const5Description = "";
                        string skill = "";
                        string auto = "Normal Attack";

                        var value = new JObject();

                        if (newData.ContainsKey(nameKey))
                        {
                            skippedDuplicateKey++;
                            Logger.Warn(
                                "Skipping duplicate character GOOD key. key={0}, name={1}, id={2}",
                                nameKey,
                                nameGOOD,
                                characterID);
                            continue;
                        }

                        {
                            value.Add("GOOD", nameGOOD);

                            // Some characters have different internal names.
                            // Ex: Jean -> Qin, Yanfei -> Feiyan, etc.
                            name = character["iconName"].ToString().Split('_').Last(); // UI_AvatarIcon_[Qin] -> Qin

                            if (name.ToLower() == "PlayerBoy".ToLower()) // Handle traveler elements separately
                            {
                                var constellationNames = new JArray { "Viator", "Viatrix" };
                                var constellationOrder = new JObject();

                                var playerElements = new Dictionary<string, string>
                                {
                                    { "Electric", "electro" },
                                    { "Fire", "pyro" },
                                    { "Grass", "dendro" },
                                    { "Rock", "geo"},
                                    { "Water", "hydro"},
                                    { "Wind", "anemo"},
                                    { "Ice", "cryo"}
                                };

                                value.Add("Element", new JArray(playerElements.Values));

                                foreach (var element in playerElements)
                                {
                                    var elementSkill = skills.FirstOrDefault(entry =>
                                        entry["skillIcon"] != null &&
                                        entry["skillIcon"].ToString().Contains($"Player{element.Key}") &&
                                        !entry.ContainsKey("costElemType"));
                                    

                                    if (elementSkill == null) continue;

                                    var travelerSkillHash = elementSkill["nameTextMapHash"]?.ToString();
                                    if (string.IsNullOrWhiteSpace(travelerSkillHash) ||
                                        !Mappings.TryGetValue(travelerSkillHash, out skill))
                                    {
                                        Logger.Warn("Skipping unmapped Traveler skill. element={0}, hash={1}",
                                            element.Key, travelerSkillHash ?? "missing");
                                        continue;
                                    }

                                    var travelerTalent = talents
                                        .Where(entry => entry["openConfig"] != null && entry["openConfig"].ToString().Contains($"Player_{element.Key}"))
                                        .Skip(2)
                                        .FirstOrDefault();
                                    var travelerC3Hash = travelerTalent?["descTextMapHash"]?.ToString();
                                    if (string.IsNullOrWhiteSpace(travelerC3Hash) ||
                                        !Mappings.TryGetValue(travelerC3Hash, out const3Description))
                                    {
                                        Logger.Warn("Skipping unmapped Traveler C3 description. element={0}, hash={1}",
                                            element.Key, travelerC3Hash ?? "missing");
                                        continue;
                                    }

                                    if (const3Description.Contains(skill))
                                    {
                                        constellationOrder.Add(element.Value, new JArray { "skill", "burst" });
                                    }
                                    else
                                    {
                                        constellationOrder.Add(element.Value, new JArray { "burst", "skill" });
                                    }
                                }
                                value.Add("ConstellationOrder", constellationOrder);
                            }
                            else // Any other character that isn't traveler
                            {
                                var characterSkillCandidates = skills
                                    .Where(entry => entry["skillIcon"] != null &&
                                        entry["skillIcon"].ToString().Contains($"Skill_S_{name}"))
                                    .ToList();

                                // Prefer the canonical unsuffixed skill icon.  Some characters have
                                // multiple Skill_S_* rows and FirstOrDefault() can select an alternate
                                // state/variant, which then breaks C3/C5 talent detection.
                                var characterSkill = characterSkillCandidates.FirstOrDefault(entry =>
                                    string.Equals(entry["skillIcon"].ToString(), $"Skill_S_{name}", StringComparison.OrdinalIgnoreCase))
                                    ?? characterSkillCandidates.FirstOrDefault(entry => !entry.ContainsKey("costElemType"))
                                    ?? characterSkillCandidates.FirstOrDefault();

                                var characterSkillHash = characterSkill?["nameTextMapHash"]?.ToString();

                                if (string.IsNullOrWhiteSpace(characterSkillHash) ||
                                    !Mappings.TryGetValue(characterSkillHash, out skill))
                                {
                                    skippedUnmappedSkill++;
                                    Logger.Warn("Skipping character with unmapped skill. character={0}, hash={1}",
                                        nameGOOD, characterSkillHash ?? "missing");
                                    continue;
                                }

                                value.Add("ConstellationName", new JArray
                                {
                                    GetConstellationNameFromId(characterID, nameGOOD)
                                });

                                var constellationOrder = new JArray();

                                // The skill/burst name is always mentioned in the constellation's description so we'll check for it
                                var thirdTalent = talents
                                    .Where(entry => entry["icon"] != null && entry["icon"].ToString().Contains(name))
                                    .Skip(2)
                                    .FirstOrDefault();
                                var c3Hash = thirdTalent?["descTextMapHash"]?.ToString();
                                if (string.IsNullOrWhiteSpace(c3Hash) ||
                                    !Mappings.TryGetValue(c3Hash, out const3Description))
                                {
                                    skippedUnmappedC3++;
                                    Logger.Warn("Skipping character with unmapped C3 description. character={0}, hash={1}",
                                        nameGOOD, c3Hash ?? "missing");
                                    continue;
                                }

                                if (const3Description.Contains(auto) || const3Description.Contains("Charged Attack"))
                                {
                                    constellationOrder.Add("auto");
                                } else if (const3Description.Contains(skill))
                                {
                                    constellationOrder.Add("skill");
                                }
                                else
                                {
                                    constellationOrder.Add("burst");
                                }

                                // The skill/burst name is always mentioned in the constellation's description so we'll check for it
                                var fifthTalent = talents
                                    .Where(entry => entry["icon"] != null && entry["icon"].ToString().Contains(name))
                                    .Skip(4)
                                    .FirstOrDefault();
                                var c5Hash = fifthTalent?["descTextMapHash"]?.ToString();
                                if (string.IsNullOrWhiteSpace(c5Hash) ||
                                    !Mappings.TryGetValue(c5Hash, out const5Description))
                                {
                                    skippedUnmappedC5++;
                                    Logger.Warn("Skipping character with unmapped C5 description. character={0}, hash={1}",
                                        nameGOOD, c5Hash ?? "missing");
                                    continue;
                                }

                                if (const5Description.Contains(auto) || const5Description.Contains("Charged Attack"))
                                {
                                    constellationOrder.Add("auto");
                                }
                                else if (const5Description.Contains(skill))
                                {
                                    constellationOrder.Add("skill");
                                }
                                else
                                {
                                    constellationOrder.Add("burst");
                                }

                                // A small number of characters cannot be inferred reliably from the
                                // current text/icon heuristic.  Keep explicit, verified fallbacks rather
                                // than silently emitting duplicate/incorrect C3/C5 talent adjustments.
                                if (nameGOOD == "KamisatoAyaka")
                                {
                                    constellationOrder = new JArray { "burst", "skill" };
                                }
                                else if (nameGOOD == "Aloy")
                                {
                                    // Aloy has no obtainable constellations, but keep structurally sane
                                    // metadata for consumers that expect a two-entry order.
                                    constellationOrder = new JArray { "burst", "skill" };
                                }
                                else if (nameGOOD == "Sandrone")
                                {
                                    // Sandrone C3 increases her Normal Attack talent and C5 increases
                                    // her Elemental Burst. The localized constellation descriptions
                                    // reference talent names rather than the generic "Normal Attack"
                                    // label, so the text heuristic cannot infer this reliably.
                                    constellationOrder = new JArray { "auto", "burst" };
                                }

                                value.Add("ConstellationOrder", constellationOrder);

                                value.Add("Element", new JArray(GetElementFromID(characterID)));
                            }

                            var weaponArchetype = character["weaponType"].ToString();
                            WeaponType weaponType;
                            if (weaponArchetype.Contains("SWORD_ONE_HAND")) weaponType = WeaponType.Sword;
                            else if (weaponArchetype.Contains("CLAYMORE")) weaponType = WeaponType.Claymore;
                            else if (weaponArchetype.Contains("POLE")) weaponType = WeaponType.Polearm;
                            else if (weaponArchetype.Contains("BOW")) weaponType = WeaponType.Bow;
                            else if (weaponArchetype.Contains("CATALYST")) weaponType = WeaponType.Catalyst;
                            else throw new IndexOutOfRangeException($"{name} uses unknown weapon type {weaponArchetype}");

                            value.Add("WeaponType", (int)weaponType);

                            if (newData.TryAdd(nameKey, value))
                            {
                                generatedRows++;
                                status = UpdateStatus.Success;
                            }
                            else
                            {
                                skippedDuplicateKey++;
                                Logger.Warn(
                                    "Character key became duplicate before insertion. key={0}, name={1}, id={2}",
                                    nameKey,
                                    nameGOOD,
                                    characterID);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        failedRows++;
                        Logger.Warn("Problem when generating character information for {0}", name);
                        Logger.Warn(ex);
                    }
                }

                int excludedRows =
                    skippedFemaleTraveler +
                    skippedInternalId +
                    skippedKnownMannequin +
                    skippedUnmappedName +
                    skippedUnmappedSkill +
                    skippedUnmappedC3 +
                    skippedUnmappedC5 +
                    skippedDuplicateKey;

                int accountedRows = generatedRows + excludedRows + failedRows;

                Logger.Info(
                    "Character source accounting: sourceFormalRows={0}, generatedRows={1}, excludedRows={2}, failedRows={3}, accountedRows={4}",
                    sourceFormalRows,
                    generatedRows,
                    excludedRows,
                    failedRows,
                    accountedRows);

                Logger.Info(
                    "Character exclusion breakdown: femaleTravelerDuplicate={0}, internalIdRange={1}, knownMannequin={2}, unmappedName={3}, unmappedSkill={4}, unmappedC3={5}, unmappedC5={6}, duplicateKey={7}",
                    skippedFemaleTraveler,
                    skippedInternalId,
                    skippedKnownMannequin,
                    skippedUnmappedName,
                    skippedUnmappedSkill,
                    skippedUnmappedC3,
                    skippedUnmappedC5,
                    skippedDuplicateKey);

                if (accountedRows != sourceFormalRows)
                {
                    Logger.Error(
                        "Character source accounting mismatch: sourceFormalRows={0}, accountedRows={1}",
                        sourceFormalRows,
                        accountedRows);
                    status = UpdateStatus.Fail;
                }

                if (failedRows > 0)
                {
                    Logger.Error(
                        "Character generation encountered {0} unhandled row exception(s); refusing to save a partial character table",
                        failedRows);
                    status = UpdateStatus.Fail;
                }

                string GetConstellationNameFromId(int targetID, string goodName)
                {
                    // Some characters intentionally or historically expose "???" in the
                    // upstream text-map field even though their in-game constellation name
                    // is known.  Preserve authoritative overrides instead of exporting the
                    // placeholder and make future unresolved placeholders visible in logs.
                    var knownConstellationNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "Columbina", "Columbina Hyposelenia" },
                        { "Neuvillette", "Leviathan Judicator" },
                        { "Zhongli", "Lapis Dei" }
                    };

                    foreach (var constellation in from constellation in constellations
                                                  where (((int)constellation["avatarId"]) == targetID)
                                                  select constellation)
                    {
                        var hash = constellation["avatarConstellationBeforTextMapHash"]?.ToString();
                        string constellationName = null;
                        if (!string.IsNullOrWhiteSpace(hash) &&
                            Mappings.TryGetValue(hash, out constellationName) &&
                            !string.IsNullOrWhiteSpace(constellationName) &&
                            constellationName != "???")
                        {
                            return constellationName;
                        }

                        if (knownConstellationNames.TryGetValue(goodName ?? string.Empty, out var knownName))
                        {
                            Logger.Info(
                                "Using verified constellation-name override. character={0}, characterId={1}, upstream={2}, hash={3}, override={4}",
                                goodName ?? "unknown",
                                targetID,
                                constellationName ?? "missing",
                                hash ?? "missing",
                                knownName);
                            return knownName;
                        }

                        Logger.Warn(
                            "Unresolved constellation name. character={0}, characterId={1}, upstream={2}, hash={3}",
                            goodName ?? "unknown",
                            targetID,
                            constellationName ?? "missing",
                            hash ?? "missing");
                        return "";
                    }

                    if (knownConstellationNames.TryGetValue(goodName ?? string.Empty, out var fallbackName))
                    {
                        Logger.Info(
                            "Using verified constellation-name override without FetterInfo row. character={0}, characterId={1}, override={2}",
                            goodName ?? "unknown",
                            targetID,
                            fallbackName);
                        return fallbackName;
                    }

                    Logger.Warn(
                        "No constellation metadata row found. character={0}, characterId={1}",
                        goodName ?? "unknown",
                        targetID);
                    return "";
                }

                string GetElementFromID(int targetID)
                {
                    foreach (var element in from constellation in constellations
                                            where (((int)constellation["avatarId"]) == targetID)
                                            select constellation)
                    {
                        var hash = element["avatarVisionBeforTextMapHash"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(hash) &&
                            Mappings.TryGetValue(hash, out var elementName))
                            return elementName.ToLower();

                        Logger.Warn("Missing element mapping. characterId={0}, hash={1}",
                            targetID, hash ?? "missing");
                        return "";
                    }
                    return "";
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex);
                status = UpdateStatus.Fail;
            }

            // Validate the new data before saving
            if (status == UpdateStatus.Success)
            {
                if (ValidateCharacterData(newData))
                {
                    Logger.Info("Character data validation passed. Saving {0} characters.", newData.Count);
                    if (!SaveJsonToFile(JsonConvert.SerializeObject(new SortedDictionary<string, JObject>(newData)), CharactersJson))
                    {
                        Logger.Error("Failed to atomically save character data.");
                        status = UpdateStatus.Fail;
                    }
                }
                else
                {
                    Logger.Error("Character data validation failed. Keeping existing data.");
                    status = UpdateStatus.Fail;
                }
            }

            return status;
        }

        private bool ValidateCharacterData(ConcurrentDictionary<string, JObject> data)
        {
            if (data == null || data.Count == 0)
            {
                Logger.Error("Validation failed: Character data is null or empty");
                return false;
            }

            // Broad sanity floor only; completeness is checked structurally below.
            if (data.Count < 50)
            {
                Logger.Error("Validation failed: Only {0} characters found (expected at least 50)", data.Count);
                return false;
            }

            string[] criticalCharacters = { "traveler", "amber", "kaeya", "lisa" };
            foreach (var charKey in criticalCharacters)
            {
                if (!data.ContainsKey(charKey))
                {
                    Logger.Error("Validation failed: Missing critical character '{0}'", charKey);
                    return false;
                }
            }

            var validTalentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "auto", "skill", "burst"
            };

            // Validate EVERY generated character.  Sampling the first five allowed bad
            // ConstellationOrder entries to be written silently.
            foreach (var kvp in data)
            {
                var key = kvp.Key;
                var charData = kvp.Value;

                if (charData == null ||
                    !charData.ContainsKey("GOOD") ||
                    !charData.ContainsKey("Element") ||
                    !charData.ContainsKey("WeaponType") ||
                    !charData.ContainsKey("ConstellationOrder"))
                {
                    Logger.Error("Validation failed: Character '{0}' missing required fields", key);
                    return false;
                }

                var goodName = charData["GOOD"]?.ToString();
                if (string.IsNullOrWhiteSpace(goodName) ||
                    !string.Equals(key, goodName.ToLowerInvariant(), StringComparison.Ordinal))
                {
                    Logger.Error(
                        "Validation failed: Character key/GOOD mismatch. key='{0}', GOOD='{1}'",
                        key,
                        goodName ?? "missing");
                    return false;
                }

                if (!key.Equals("traveler", StringComparison.OrdinalIgnoreCase))
                {
                    var constellationNames = charData["ConstellationName"] as JArray;
                    var constellationName = constellationNames?.FirstOrDefault()?.ToString();

                    if (constellationNames == null ||
                        constellationNames.Count == 0 ||
                        string.IsNullOrWhiteSpace(constellationName) ||
                        constellationName == "???")
                    {
                        Logger.Error(
                            "Validation failed: Character '{0}' has unresolved ConstellationName '{1}'",
                            key,
                            constellationName ?? "missing");
                        return false;
                    }
                }

                var elements = charData["Element"] as JArray;
                if (elements == null || elements.Count == 0 ||
                    elements.Any(e => string.IsNullOrWhiteSpace(e?.ToString())))
                {
                    Logger.Error("Validation failed: Character '{0}' has invalid Element data", key);
                    return false;
                }

                if (!int.TryParse(charData["WeaponType"]?.ToString(), out var weaponType) ||
                    weaponType < (int)WeaponType.Sword || weaponType > (int)WeaponType.Catalyst)
                {
                    Logger.Error("Validation failed: Character '{0}' has invalid WeaponType '{1}'",
                        key, charData["WeaponType"]?.ToString() ?? "missing");
                    return false;
                }

                if (key.Equals("traveler", StringComparison.OrdinalIgnoreCase))
                {
                    var travelerOrder = charData["ConstellationOrder"] as JObject;
                    if (travelerOrder == null)
                    {
                        Logger.Error("Validation failed: Traveler ConstellationOrder is not element-specific");
                        return false;
                    }

                    foreach (var elementToken in elements)
                    {
                        var element = elementToken.ToString().ToLowerInvariant();
                        var order = travelerOrder[element] as JArray;
                        if (!ValidateTalentOrder(order, $"traveler/{element}")) return false;
                    }
                }
                else
                {
                    var order = charData["ConstellationOrder"] as JArray;
                    if (!ValidateTalentOrder(order, key)) return false;
                }
            }

            Logger.Debug("Character data validation passed: {0} characters, all checks OK", data.Count);
            return true;

            bool ValidateTalentOrder(JArray order, string label)
            {
                if (order == null || order.Count != 2)
                {
                    Logger.Error("Validation failed: Character '{0}' has malformed ConstellationOrder", label);
                    return false;
                }

                var first = order[0]?.ToString();
                var second = order[1]?.ToString();
                if (!validTalentTypes.Contains(first ?? string.Empty) ||
                    !validTalentTypes.Contains(second ?? string.Empty))
                {
                    Logger.Error("Validation failed: Character '{0}' has unknown talent type(s) in ConstellationOrder: [{1}, {2}]",
                        label, first ?? "missing", second ?? "missing");
                    return false;
                }

                if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Error("Validation failed: Character '{0}' has duplicate C3/C5 talent target '{1}'",
                        label, first);
                    return false;
                }

                return true;
            }
        }

        private UpdateStatus UpdateArtifacts(bool force)
        {
            var status = UpdateStatus.Skipped;

            // Load existing data (or empty dict if file doesn't exist or force mode)
            var data = force
                ? new ConcurrentDictionary<string, JObject>()
                : JToken.Parse(LoadJsonFromFile(ArtifactsJson)).ToObject<ConcurrentDictionary<string, JObject>>();

            var newData = new ConcurrentDictionary<string, JObject>(data);

            var artifactDisplays = JArray.Parse(LoadJsonFromURLAsync(BuildDataURL(ArtifactsDisplayItemPath))).ToObject<List<JObject>>();
            artifactDisplays.RemoveAll(a => a.TryGetValue("icon", out var icon) && !icon.ToString().Contains("RelicIcon"));

            var codex = JArray.Parse(LoadJsonFromURLAsync(BuildDataURL(ArtifactsCodexPath))).ToObject<List<JObject>>();
            var sets = JArray.Parse(LoadJsonFromURLAsync(BuildDataURL(ArtifactSetConfigPath))).ToObject<List<JObject>>();

            artifactDisplays.AsParallel().ForAll(artifactDisplay =>
            {
                if (Mappings.ContainsKey(artifactDisplay["nameTextMapHash"].ToString()))
                {
                    string setName = Mappings[artifactDisplay["nameTextMapHash"].ToString()];                  // Archaic Petra
                    try
                    {
                        var setNamePascalCase = CultureInfo.GetCultureInfo("en").TextInfo.ToTitleCase(setName); // Archaic Petra
                        var setNameGOOD = Regex.Replace(setNamePascalCase, @"[\W]", string.Empty);              // ArchaicPetra
                        var setNameKey = setNameGOOD.ToLower();                                                 // archaicpetra
                        var setNameNormalized = setName;
                        var setID = (int)artifactDisplay["param"];

                        if (!newData.ContainsKey(setNameKey))
                        {
                            foreach (var set in codex)
                            {
                                var artifacts = new JObject();

                                if ((int)set["suitId"] == setID)
                                {
                                    var artifactIDs = new Dictionary<string, JToken>()
                                    {
                                        { "flower", set["flowerId"]},
                                        { "plume", set["leatherId"] },
                                        { "sands" , set["sandId"] },
                                        { "goblet", set["cupId"] },
                                        { "circlet", set["capId"] },
                                    };
                                    
                                    foreach (var artifact in sets.Where(s => artifactIDs.Values.Contains((int)s["id"])))
                                    {
                                        var slot = artifactIDs.First(x => x.Value != null && (int)x.Value == (int)artifact["id"]).Key;
                                        var artifactHash = artifact["nameTextMapHash"]?.ToString();
                                        if (string.IsNullOrWhiteSpace(artifactHash) ||
                                            !Mappings.TryGetValue(artifactHash, out var artifactName))
                                        {
                                            Logger.Warn("Skipping artifact piece with unmapped name. id={0}, hash={1}",
                                                artifact["id"]?.ToString() ?? "missing",
                                                artifactHash ?? "missing");
                                            continue;
                                        }
                                        string artifactNamePascalCase = CultureInfo.GetCultureInfo("en").TextInfo.ToTitleCase(artifactName);  // Goblet Of The Sojourner
                                        string artifactNameGOOD = Regex.Replace(artifactNamePascalCase, @"[\W]", string.Empty);               // GobletOfTheSojourner
                                        string artifactNormalized = artifactNameGOOD.ToLower();                                               // gobletofthesojourner

                                        artifacts.Add(slot, new JObject
                                        {
                                            { "artifactName", artifactName },
                                            { "GOOD", artifactNameGOOD },
                                            { "normalizedName", artifactNormalized },
                                        });
                                    }
                                }

                                if (artifacts.Count < 1) continue;
                                var value = new JObject
                                {
                                    { "setName", setName },
                                    { "GOOD", setNameGOOD },
                                    { "normalizedName", setNameGOOD.ToLower() },
                                    { "artifacts", artifacts }
                                };
                                if (newData.TryAdd(setNameKey, value) && status != UpdateStatus.Fail) status = UpdateStatus.Success;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("An error was encountered when gathering information for the artifact set {0}", setName);
                        Logger.Error(ex);
                        status = UpdateStatus.Fail;
                    }
                }
            });

            // Validate the new data before saving
            if (status == UpdateStatus.Success)
            {
                if (ValidateArtifactData(newData))
                {
                    Logger.Info("Artifact data validation passed. Saving {0} artifact sets.", newData.Count);
                    if (!SaveJsonToFile(JsonConvert.SerializeObject(new SortedDictionary<string, JObject>(newData)), ArtifactsJson))
                    {
                        Logger.Error("Failed to atomically save artifact data.");
                        status = UpdateStatus.Fail;
                    }
                }
                else
                {
                    Logger.Error("Artifact data validation failed. Keeping existing data.");
                    status = UpdateStatus.Fail;
                }
            }

            return status;
        }

        private bool ValidateArtifactData(ConcurrentDictionary<string, JObject> data)
        {
            if (data == null || data.Count == 0)
            {
                Logger.Error("Validation failed: Artifact data is null or empty");
                return false;
            }

            // Broad sanity floor only. Every generated set/piece is validated below.
            if (data.Count < 30)
            {
                Logger.Error("Validation failed: Only {0} artifact sets found (expected at least 30)", data.Count);
                return false;
            }

            string[] criticalSets = { "gladiatorsfinale", "wandererstroupe", "noblesseoblige" };
            foreach (var setKey in criticalSets)
            {
                if (!data.ContainsKey(setKey))
                {
                    Logger.Error("Validation failed: Missing critical artifact set '{0}'", setKey);
                    return false;
                }
            }

            var validSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "flower", "plume", "sands", "goblet", "circlet"
            };

            foreach (var kvp in data)
            {
                var setKey = kvp.Key;
                var setData = kvp.Value;

                if (setData == null ||
                    !setData.ContainsKey("setName") ||
                    !setData.ContainsKey("GOOD") ||
                    !setData.ContainsKey("normalizedName") ||
                    !setData.ContainsKey("artifacts"))
                {
                    Logger.Error("Validation failed: Artifact set '{0}' missing required fields", setKey);
                    return false;
                }

                var setGood = setData["GOOD"]?.ToString();
                if (string.IsNullOrWhiteSpace(setGood) ||
                    !string.Equals(setKey, setGood.ToLowerInvariant(), StringComparison.Ordinal))
                {
                    Logger.Error(
                        "Validation failed: Artifact set key/GOOD mismatch. key='{0}', GOOD='{1}'",
                        setKey,
                        setGood ?? "missing");
                    return false;
                }

                var artifacts = setData["artifacts"] as JObject;
                if (artifacts == null || artifacts.Count < 1 || artifacts.Count > 5)
                {
                    Logger.Error(
                        "Validation failed: Artifact set '{0}' has invalid piece count {1}",
                        setKey,
                        artifacts?.Count ?? 0);
                    return false;
                }

                foreach (var piece in artifacts.Properties())
                {
                    if (!validSlots.Contains(piece.Name))
                    {
                        Logger.Error(
                            "Validation failed: Artifact set '{0}' contains unknown slot '{1}'",
                            setKey,
                            piece.Name);
                        return false;
                    }

                    var pieceData = piece.Value as JObject;
                    if (pieceData == null ||
                        string.IsNullOrWhiteSpace(pieceData["artifactName"]?.ToString()) ||
                        string.IsNullOrWhiteSpace(pieceData["GOOD"]?.ToString()) ||
                        string.IsNullOrWhiteSpace(pieceData["normalizedName"]?.ToString()))
                    {
                        Logger.Error(
                            "Validation failed: Artifact set '{0}' slot '{1}' has malformed piece metadata",
                            setKey,
                            piece.Name);
                        return false;
                    }

                    var pieceGood = pieceData["GOOD"].ToString();
                    if (!string.Equals(
                        pieceData["normalizedName"].ToString(),
                        pieceGood.ToLowerInvariant(),
                        StringComparison.Ordinal))
                    {
                        Logger.Error(
                            "Validation failed: Artifact set '{0}' slot '{1}' normalizedName/GOOD mismatch",
                            setKey,
                            piece.Name);
                        return false;
                    }
                }
            }

            Logger.Debug("Artifact data validation passed: {0} sets, all checks OK", data.Count);
            return true;
        }

        private UpdateStatus UpdateWeapons(bool force)
        {
            var status = UpdateStatus.Skipped;

            // Load existing data (or empty dict if file doesn't exist or force mode)
            var data = force
                ? new ConcurrentDictionary<string, string>()
                : JToken.Parse(LoadJsonFromFile(WeaponsJson)).ToObject<ConcurrentDictionary<string, string>>();

            var newData = new ConcurrentDictionary<string, string>(data);

            try
            {
                List<JObject> weapons = JArray.Parse(LoadJsonFromURLAsync(BuildDataURL(WeaponsPath))).ToObject<List<JObject>>();

                weapons.AsParallel().ForAll(weapon =>
                {
                    try
                    {
                        if (Mappings.ContainsKey(weapon["nameTextMapHash"].ToString()))
                        {
                            var name = Mappings[weapon["nameTextMapHash"].ToString()];
                            string PascalCase = CultureInfo.GetCultureInfo("en").TextInfo.ToTitleCase(name); // Dull Blade
                            string nameGOOD = Regex.Replace(PascalCase, @"[\W]", string.Empty);              // DullBlade
                            string nameKey = nameGOOD.ToLower();                                             // dullblade

                            if (newData.TryAdd(nameKey, nameGOOD)) status = UpdateStatus.Success;
                        }
                        else Logger.Warn("Weapon hash {0} not found in Mappings. It's likely unreleased.", weapon["nameTextMapHash"].ToString());
                    }
                    catch (Exception ex) { Logger.Warn(ex, weapon["nameTextMapHash"].ToString()); }
                });
            }
            catch (Exception ex)
            {
                Logger.Warn(ex);
                status = UpdateStatus.Fail;
            }

            // Validate the new data before saving
            if (status == UpdateStatus.Success)
            {
                if (ValidateWeaponData(newData))
                {
                    Logger.Info("Weapon data validation passed. Saving {0} weapons.", newData.Count);
                    if (!SaveJsonToFile(JsonConvert.SerializeObject(new SortedDictionary<string, string>(newData)), WeaponsJson))
                    {
                        Logger.Error("Failed to atomically save weapon data.");
                        status = UpdateStatus.Fail;
                    }
                }
                else
                {
                    Logger.Error("Weapon data validation failed. Keeping existing data.");
                    status = UpdateStatus.Fail;
                }
            }

            return status;
        }

        private bool ValidateWeaponData(ConcurrentDictionary<string, string> data)
        {
            if (data == null || data.Count == 0)
            {
                Logger.Error("Validation failed: Weapon data is null or empty");
                return false;
            }

            if (data.Count < 100)
            {
                Logger.Error("Validation failed: Only {0} weapons found (expected at least 100)", data.Count);
                return false;
            }

            string[] criticalWeapons = { "dullblade", "silversword", "beginnersprotector" };
            foreach (var weaponKey in criticalWeapons)
            {
                if (!data.ContainsKey(weaponKey))
                {
                    Logger.Error("Validation failed: Missing critical weapon '{0}'", weaponKey);
                    return false;
                }
            }

            foreach (var kvp in data)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key) ||
                    string.IsNullOrWhiteSpace(kvp.Value) ||
                    !string.Equals(kvp.Key, kvp.Value.ToLowerInvariant(), StringComparison.Ordinal))
                {
                    Logger.Error(
                        "Validation failed: Weapon key/GOOD mismatch or blank value. key='{0}', GOOD='{1}'",
                        kvp.Key ?? "missing",
                        kvp.Value ?? "missing");
                    return false;
                }
            }

            Logger.Debug("Weapon data validation passed: {0} weapons, all checks OK", data.Count);
            return true;
        }

        private UpdateStatus UpdateMaterials(bool force)
        {
            var status = UpdateStatus.Skipped;

            var materialCategories = new List<string>
            {
                "MATERIAL_EXP_FRUIT",
                "MATERIAL_AVATAR_MATERIAL",
                "MATERIAL_EXCHANGE",
                "MATERIAL_WOOD",
                "MATERIAL_FISH_BAIT",
                "MATERIAL_WEAPON_EXP_STONE",
            };

            // Load existing data (or empty dict if file doesn't exist or force mode)
            var data = force
                ? new ConcurrentDictionary<string, string>()
                : JToken.Parse(LoadJsonFromFile(MaterialsJson)).ToObject<ConcurrentDictionary<string, string>>();

            var newData = new ConcurrentDictionary<string, string>(data);

            var materials = JArray.Parse(LoadJsonFromURLAsync(BuildDataURL(MaterialsPath))).ToObject<List<JObject>>();
            materials.RemoveAll(material => !(material.TryGetValue("materialType", out var materialType) && materialCategories.Contains(materialType.ToString())));

            materials.AsParallel().ForAll(material =>
            {
                try
                {
                    JToken jToken = material["nameTextMapHash"];
                    if (Mappings.TryGetValue(jToken.ToString(), out var name))
                    {
                        var PascalCase = CultureInfo.GetCultureInfo("en").TextInfo.ToTitleCase(name);
                        var nameGood = Regex.Replace(PascalCase, @"[\W]", string.Empty);
                        var nameKey = nameGood.ToLower();

                        if (newData.TryAdd(nameKey, nameGood) && status != UpdateStatus.Fail) status = UpdateStatus.Success;
                    }
                    else
                    {
                        Logger.Warn("Material hash {0} not found in Mappings.", material["nameTextMapHash"].ToString());
                    }
                }
                catch (Exception ex) { Logger.Warn(ex); }
            });

            // Validate the new data before saving
            if (status == UpdateStatus.Success)
            {
                if (ValidateMaterialData(newData))
                {
                    Logger.Info("Material data validation passed. Saving {0} materials.", newData.Count);
                    if (!SaveJsonToFile(JsonConvert.SerializeObject(new SortedDictionary<string, string>(newData)), MaterialsJson))
                    {
                        Logger.Error("Failed to atomically save material data.");
                        status = UpdateStatus.Fail;
                    }
                }
                else
                {
                    Logger.Error("Material data validation failed. Keeping existing data.");
                    status = UpdateStatus.Fail;
                }
            }

            return status;
        }

        private bool ValidateMaterialData(ConcurrentDictionary<string, string> data)
        {
            if (data == null || data.Count == 0)
            {
                Logger.Error("Validation failed: Material data is null or empty");
                return false;
            }

            if (data.Count < 50)
            {
                Logger.Error("Validation failed: Only {0} materials found (expected at least 50)", data.Count);
                return false;
            }

            foreach (var kvp in data)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key) ||
                    string.IsNullOrWhiteSpace(kvp.Value) ||
                    !string.Equals(kvp.Key, kvp.Value.ToLowerInvariant(), StringComparison.Ordinal))
                {
                    Logger.Error(
                        "Validation failed: Material key/GOOD mismatch or blank value. key='{0}', GOOD='{1}'",
                        kvp.Key ?? "missing",
                        kvp.Value ?? "missing");
                    return false;
                }
            }

            Logger.Debug("Material data validation passed: {0} materials, all checks OK", data.Count);
            return true;
        }

        private string FetchHTML(string url)
        {
            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Encoding = System.Text.Encoding.UTF8;
                    return client.DownloadString(url);
                }
            }
            catch { throw; }

        }

        private string LoadJsonFromFile(string fileName)
        {
            lock (this)
            {
                try
                {
                    using (StreamReader file = File.OpenText(ListsDir + fileName))
                    using (JsonTextReader reader = new JsonTextReader(file))
                    {
                        return JToken.ReadFrom(reader).ToString();
                    }
                }
                catch (Exception)
                {
                    File.Create(ListsDir + fileName).Close();
                    return "{}";
                }
            }
        }

        private string[] ManagedLookupFileNames()
        {
            return new[]
            {
                WeaponsJson,
                ArtifactsJson,
                CharactersJson,
                MaterialsJson,
                NewVersion,
                UpdateManifestJson
            };
        }

        private string CreateUpdateBackup()
        {
            var backupDirectory = Path.Combine(
                ListsDir,
                ".update-backup-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(backupDirectory);

            foreach (var fileName in ManagedLookupFileNames())
            {
                var sourcePath = Path.Combine(ListsDir, fileName);
                var backupPath = Path.Combine(backupDirectory, fileName);

                if (File.Exists(sourcePath))
                    File.Copy(sourcePath, backupPath, true);
            }

            Logger.Debug("Created lookup update backup at {0}", backupDirectory);
            return backupDirectory;
        }

        private void RestoreUpdateBackup(string backupDirectory)
        {
            if (string.IsNullOrWhiteSpace(backupDirectory) ||
                !Directory.Exists(backupDirectory))
                return;

            try
            {
                foreach (var fileName in ManagedLookupFileNames())
                {
                    var targetPath = Path.Combine(ListsDir, fileName);
                    var backupPath = Path.Combine(backupDirectory, fileName);

                    if (File.Exists(targetPath))
                        File.Delete(targetPath);

                    if (File.Exists(backupPath))
                        File.Copy(backupPath, targetPath, true);
                }

                Logger.Warn("Restored previous lookup set after failed update.");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to restore lookup update backup from {0}", backupDirectory);
            }
            finally
            {
                DeleteUpdateBackup(backupDirectory);
            }
        }

        private void DeleteUpdateBackup(string backupDirectory)
        {
            if (string.IsNullOrWhiteSpace(backupDirectory))
                return;

            try
            {
                if (Directory.Exists(backupDirectory))
                    Directory.Delete(backupDirectory, true);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not remove lookup update backup directory {0}", backupDirectory);
            }
        }

        private bool SaveTextToFileAtomically(string content, string fileName)
        {
            lock (this)
            {
                string targetPath = Path.Combine(ListsDir, fileName);
                string tempPath = targetPath + ".tmp";
                string replaceBackupPath = targetPath + ".replacebak";

                try
                {
                    Directory.CreateDirectory(ListsDir);

                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                    if (File.Exists(replaceBackupPath))
                        File.Delete(replaceBackupPath);

                    File.WriteAllText(tempPath, content ?? string.Empty);

                    if (File.Exists(targetPath))
                    {
                        File.Replace(tempPath, targetPath, replaceBackupPath);
                        if (File.Exists(replaceBackupPath))
                            File.Delete(replaceBackupPath);
                    }
                    else
                    {
                        File.Move(tempPath, targetPath);
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Atomic text save failed for {0}", fileName);

                    try
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                    catch { }

                    return false;
                }
            }
        }

        private bool SaveJsonToFile(string json, string fileName)
        {
            lock (this)
            {
                string targetPath = Path.Combine(ListsDir, fileName);
                string tempPath = targetPath + ".tmp";
                string replaceBackupPath = targetPath + ".replacebak";

                try
                {
                    Directory.CreateDirectory(ListsDir);

                    // Parse before touching disk so malformed generated JSON can never
                    // overwrite a previously working lookup file.
                    var parsed = JToken.Parse(json);

                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                    if (File.Exists(replaceBackupPath))
                        File.Delete(replaceBackupPath);

                    using (StreamWriter file = new StreamWriter(tempPath))
                    using (JsonTextWriter writer = new JsonTextWriter(file))
                    {
                        writer.Formatting = Formatting.Indented;
                        parsed.WriteTo(writer);
                    }

                    // Verify the fully written temporary file before promotion.
                    using (StreamReader file = File.OpenText(tempPath))
                    using (JsonTextReader reader = new JsonTextReader(file))
                    {
                        JToken.ReadFrom(reader);
                    }

                    if (File.Exists(targetPath))
                    {
                        File.Replace(tempPath, targetPath, replaceBackupPath);
                        if (File.Exists(replaceBackupPath))
                            File.Delete(replaceBackupPath);
                    }
                    else
                    {
                        File.Move(tempPath, targetPath);
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Atomic JSON save failed for {0}", fileName);

                    try
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                    catch { }

                    return false;
                }
            }
        }

        private bool WriteUpdateManifest()
        {
            try
            {
                var counts = new JObject
                {
                    { "characters", JObject.Parse(File.ReadAllText(Path.Combine(ListsDir, CharactersJson))).Count },
                    { "weapons", JObject.Parse(File.ReadAllText(Path.Combine(ListsDir, WeaponsJson))).Count },
                    { "artifacts", JObject.Parse(File.ReadAllText(Path.Combine(ListsDir, ArtifactsJson))).Count },
                    { "materials", JObject.Parse(File.ReadAllText(Path.Combine(ListsDir, MaterialsJson))).Count }
                };

                var files = new JObject
                {
                    { CharactersJson, BuildFileMetadata(CharactersJson) },
                    { WeaponsJson, BuildFileMetadata(WeaponsJson) },
                    { ArtifactsJson, BuildFileMetadata(ArtifactsJson) },
                    { MaterialsJson, BuildFileMetadata(MaterialsJson) }
                };

                var manifest = new JObject
                {
                    { "schemaVersion", 1 },
                    { "generatedAtUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                    { "gameVersion", LocalVersion.ToString() },
                    { "sourceRepository", "Dimbreath/AnimeGameData2" },
                    { "sourceRevision", string.IsNullOrWhiteSpace(SourceHeadCommitSha) ? "main" : SourceHeadCommitSha },
                    { "versionMarkerCommit", VersionMarkerCommitSha ?? string.Empty },
                    { "counts", counts },
                    { "files", files }
                };

                return SaveJsonToFile(manifest.ToString(Formatting.None), UpdateManifestJson);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Unable to build lookup update manifest");
                return false;
            }
        }

        private JObject BuildFileMetadata(string fileName)
        {
            var path = Path.Combine(ListsDir, fileName);
            var info = new FileInfo(path);

            return new JObject
            {
                { "bytes", info.Length },
                { "sha256", GetFileSha256(path) }
            };
        }

        private string GetFileSha256(string path)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha256.ComputeHash(stream))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private string ConvertToGOOD(string text)
        {
            foreach (Match match in Regex.Matches(text, @"&#\d*;"))
            {
                if (int.TryParse(Regex.Replace(match.Value, @"\D", string.Empty), out int dec))
                    text = text.Replace(match.Value, Char.ToString(Convert.ToChar(dec)));

                else
                    Logger.Warn("Failed to parse decimal value {0} from string {1}", match.Value, text);
            }
            var pascal = CultureInfo.GetCultureInfo("en-US").TextInfo.ToTitleCase(text);
            return Regex.Replace(pascal, @"[\W]", string.Empty);
        }
    }

    public enum ListType
    {
        Characters,
        Weapons,
        Artifacts,
        CharacterDevelopmentItems,
        Materials,
    }

    public enum UpdateStatus
    {
        Fail,
        Skipped,
        Success,
    }
}
