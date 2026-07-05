using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace InventoryKamera
{
    /// <summary>
    /// Fuzzy-matches noisy OCR text against the game's lookup data (gear slots/stats/elements/
    /// weapons/artifact sets/characters/materials). Extracted from <see cref="GenshinProcesor"/>'s
    /// "Element Searching" region (Phase 2 §2.1) as pure functions of <c>(data, input)</c>, same
    /// reasoning as <see cref="LookupService"/>: <c>GenshinProcesor.ReloadData()</c> reassigns its
    /// lookup dictionaries every scan, so a service capturing them at construction would go stale.
    /// <c>GenshinProcesor</c>'s existing <c>FindClosestX</c> methods now forward here, passing their
    /// current static fields each call.
    /// </summary>
    internal static class TextNormalizer
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        internal static string FindClosestGearSlot(string input, ICollection<string> gearSlots)
        {
            foreach (var slot in gearSlots)
            {
                if (input.Contains(slot))
                {
                    return slot;
                }
            }
            return input;
        }

        internal static string FindClosestStat(string stat, Dictionary<string, string> stats, int minConfidence = 90) =>
            FindClosestInDict(source: stat, targets: stats, minConfidence: minConfidence);

        internal static string FindElementByName(string name, Dictionary<string, string> elements, int minConfidence = 90) =>
            FindClosestInDict(source: name, targets: elements, minConfidence: minConfidence);

        internal static string FindClosestWeapon(string name, Dictionary<string, string> weapons, int maxEdits = 90) =>
            FindClosestInDict(source: name, targets: weapons, minConfidence: maxEdits);

        internal static string FindClosestSetName(string name, Dictionary<string, JObject> artifacts, int minConfidence = 90) =>
            FindClosestInDict(source: name, targets: artifacts, minConfidence: minConfidence);

        internal static string FindClosestArtifactSetFromArtifactName(string name, Dictionary<string, JObject> artifacts, int minConfidence = 90)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string closestMatch = null;
            double highestConfidence = 0;

            foreach (var artifactSet in artifacts)
            {
                string currentSet = artifactSet.Value["GOOD"].ToString();

                foreach (var slot in artifactSet.Value["artifacts"].Values())
                {
                    string artifactName = slot["normalizedName"].ToString();
                    if (artifactName == name) return currentSet;

                    double artifactSimilarity = StringSimilarity(name, artifactName);

                    if (artifactSimilarity > minConfidence && artifactSimilarity > highestConfidence)
                    {
                        highestConfidence = artifactSimilarity;
                        closestMatch = currentSet;
                    }
                }
            }

            return closestMatch;
        }

        internal static string FindClosestCharacterName(string name, Dictionary<string, JObject> characters, int minConfidence = 90)
        {
            var temp = new Dictionary<string, JObject>();
            foreach (var character in characters)
            {
                if (character.Value.TryGetValue("CustomName", out var customName)) temp.Add(((string)customName), character.Value);
                else temp.Add(character.Key, character.Value);
            }
            return FindClosestInDict(source: name, targets: temp, minConfidence: minConfidence);
        }

        internal static string FindClosestDevelopmentName(string name, Dictionary<string, string> devItems, Dictionary<string, string> materials, int minConfidence = 90)
        {
            string value = FindClosestInDict(source: name, targets: devItems, minConfidence: minConfidence);
            return !string.IsNullOrWhiteSpace(value) ? value : FindClosestInDict(source: name, targets: materials, minConfidence: minConfidence);
        }

        internal static string FindClosestMaterialName(string name, Dictionary<string, string> materials, int minConfidence = 90)
        {
            string value = FindClosestInDict(source: name, targets: materials, minConfidence: minConfidence);
            return !string.IsNullOrWhiteSpace(value) ? value : FindClosestInDict(source: name, targets: materials, minConfidence: minConfidence);
        }

        private static string FindClosestInDict(string source, Dictionary<string, string> targets, int minConfidence)
        {
            if (string.IsNullOrWhiteSpace(source)) return "";
            if (targets.TryGetValue(source, out string value)) return value;

            HashSet<string> keys = new HashSet<string>(targets.Keys);

            if (keys.Where(key => key.Contains(source)).Count() == 1) return targets[keys.First(key => key.Contains(source))];

            source = FindClosestInList(source, keys, minConfidence);

            return targets.TryGetValue(source, out value) ? value : source;
        }

        private static string FindClosestInDict(string source, Dictionary<string, JObject> targets, int minConfidence)
        {
            if (string.IsNullOrWhiteSpace(source)) return "";
            if (targets.TryGetValue(source, out JObject value)) return (string)value["GOOD"];

            HashSet<string> keys = new HashSet<string>(targets.Keys);

            if (keys.Where(key => key.Contains(source)).Count() == 1) return (string)targets[keys.First(key => key.Contains(source))]["GOOD"];

            source = FindClosestInList(source, keys, minConfidence);

            return targets.TryGetValue(source, out value) ? (string)value["GOOD"] : source;
        }

        /// <summary>
        /// Fuzzy-matches <paramref name="source"/> against a flat set of candidate strings (not a
        /// name-to-value lookup dictionary like the other <c>FindClosestX</c> methods) -- exposed
        /// (rather than the usual <c>private</c>) for ad-hoc matching against small hardcoded lists
        /// that don't warrant their own lookup dictionary, e.g. Phase 3 §6c's inventory tab names.
        /// </summary>
        internal static string FindClosestInList(string source, HashSet<string> targets, double minConfidence = 80)
        {
            if (targets.Contains(source)) return source;
            if (string.IsNullOrWhiteSpace(source)) return null;

            string mostSimilarString = "";
            double mostSimilarValue = 0;

            foreach (var target in targets)
            {
                double similarityValue = StringSimilarity(source, target);

                if (similarityValue > minConfidence && similarityValue > mostSimilarValue)
                {
                    mostSimilarValue = similarityValue;
                    mostSimilarString = target;
                }
            }

            if (!string.IsNullOrWhiteSpace(mostSimilarString) && !targets.Contains("critrate"))   // Only print this statement when not looking to match for a closest stat
                Logger.Debug("Most similar string found for {0} as {1} ({2}%)", source, mostSimilarString, mostSimilarValue);

            return mostSimilarString;
        }

        private static int LevenshteinDistance(string s1, string s2)
        {
            int m = s1.Length;
            int n = s2.Length;
            int[,] dp = new int[m + 1, n + 1];

            for (int i = 0; i <= m; i++)
            {
                for (int j = 0; j <= n; j++)
                {
                    if (i == 0)
                    {
                        dp[i, j] = j;
                    }
                    else if (j == 0)
                    {
                        dp[i, j] = i;
                    }
                    else if (s1[i - 1] == s2[j - 1])
                    {
                        dp[i, j] = dp[i - 1, j - 1];
                    }
                    else
                    {
                        dp[i, j] = 1 + Math.Min(Math.Min(dp[i - 1, j], dp[i, j - 1]), dp[i - 1, j - 1]);
                    }
                }
            }

            return dp[m, n];
        }

        private static double StringSimilarity(string s1, string s2)
        {
            int distance = LevenshteinDistance(s1, s2);
            int maxLength = Math.Max(s1.Length, s2.Length);
            double similarity = 1.0 - (distance / (double)maxLength);
            return similarity * 100.0;
        }
    }
}
