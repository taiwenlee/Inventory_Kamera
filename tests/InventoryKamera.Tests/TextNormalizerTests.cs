using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using Xunit;

namespace InventoryKamera.Tests
{
    /// <summary>
    /// TextNormalizer takes its lookup data as parameters instead of capturing it (Phase 2 §2.1,
    /// same pattern as LookupService), so these tests use small fake dictionaries -- previously
    /// impossible when this logic lived as static methods on GenshinProcesor, whose static
    /// constructor loaded real game data from disk.
    /// </summary>
    public class TextNormalizerTests
    {
        [Fact]
        public void FindClosestGearSlot_ReturnsFirstContainedSlot()
        {
            var gearSlots = new List<string> { "flower", "plume", "sands" };

            Assert.Equal("flower", TextNormalizer.FindClosestGearSlot("theflower", gearSlots));
            Assert.Equal("noise", TextNormalizer.FindClosestGearSlot("noise", gearSlots));
        }

        [Fact]
        public void FindClosestStat_ReturnsExactMatch()
        {
            var stats = new Dictionary<string, string> { ["critrate"] = "critRate_", ["critdmg"] = "critDMG_" };

            Assert.Equal("critRate_", TextNormalizer.FindClosestStat("critrate", stats));
        }

        [Fact]
        public void FindClosestStat_FuzzyMatchesTypos()
        {
            var stats = new Dictionary<string, string> { ["elementalmastery"] = "eleMas" };

            // One-character typo in a longer string stays above the default 90% similarity threshold.
            Assert.Equal("eleMas", TextNormalizer.FindClosestStat("elementalmaster1", stats));
        }

        [Fact]
        public void FindClosestStat_ReturnsEmptyWhenNoConfidentMatch()
        {
            // FindClosestInDict reassigns `source` to the fuzzy-match result (empty when nothing
            // clears the confidence threshold), so a total miss returns "", not the original input.
            var stats = new Dictionary<string, string> { ["critrate"] = "critRate_" };

            Assert.Equal("", TextNormalizer.FindClosestStat("completelydifferent", stats));
        }

        [Fact]
        public void FindClosestMaterialName_ReturnsEmptyWhenNoMatch()
        {
            var materials = new Dictionary<string, string> { ["mora"] = "Mora" };

            Assert.Equal("Mora", TextNormalizer.FindClosestMaterialName("mora", materials));
            Assert.Equal("", TextNormalizer.FindClosestMaterialName("unknownitem", materials));
        }

        [Fact]
        public void FindClosestDevelopmentName_FallsBackFromDevItemsToMaterials()
        {
            var devItems = new Dictionary<string, string> { ["heroswit"] = "Hero's Wit" };
            var materials = new Dictionary<string, string> { ["mora"] = "Mora" };

            Assert.Equal("Hero's Wit", TextNormalizer.FindClosestDevelopmentName("heroswit", devItems, materials));
            Assert.Equal("Mora", TextNormalizer.FindClosestDevelopmentName("mora", devItems, materials));
        }

        [Fact]
        public void FindClosestCharacterName_PrefersCustomName()
        {
            var characterJson = new JObject { ["CustomName"] = "MyDiluc", ["GOOD"] = "Diluc" };
            var characters = new Dictionary<string, JObject> { ["diluc"] = characterJson };

            Assert.Equal("Diluc", TextNormalizer.FindClosestCharacterName("MyDiluc", characters));
        }

        [Fact]
        public void FindClosestSetName_MatchesExactKey()
        {
            var setJson = new JObject { ["GOOD"] = "GladiatorsFinale" };
            var artifacts = new Dictionary<string, JObject> { ["gladiatorsfinale"] = setJson };

            Assert.Equal("GladiatorsFinale", TextNormalizer.FindClosestSetName("gladiatorsfinale", artifacts));
        }

        [Fact]
        public void FindClosestArtifactSetFromArtifactName_MatchesByNormalizedArtifactName()
        {
            // "artifacts" is a JObject keyed by slot (flower/plume/...), .Values() enumerates the
            // per-slot artifact JObjects -- not a JArray.
            var artifactEntry = new JObject { ["normalizedName"] = "gladiatorsnostalgia" };
            var artifactsBySlot = new JObject { ["flower"] = artifactEntry };
            var setJson = new JObject { ["GOOD"] = "GladiatorsFinale", ["artifacts"] = artifactsBySlot };
            var artifacts = new Dictionary<string, JObject> { ["gladiatorsfinale"] = setJson };

            Assert.Equal("GladiatorsFinale", TextNormalizer.FindClosestArtifactSetFromArtifactName("gladiatorsnostalgia", artifacts));
            Assert.Null(TextNormalizer.FindClosestArtifactSetFromArtifactName("somethingelse", artifacts));
        }

        [Fact]
        public void FindClosestWeapon_ReturnsExactMatch()
        {
            var weapons = new Dictionary<string, string> { ["favoniussword"] = "Favonius Sword" };

            Assert.Equal("Favonius Sword", TextNormalizer.FindClosestWeapon("favoniussword", weapons));
        }
    }
}
