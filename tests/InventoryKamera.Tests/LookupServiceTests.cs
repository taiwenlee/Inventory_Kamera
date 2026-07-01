using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using Xunit;

namespace InventoryKamera.Tests
{
    /// <summary>
    /// LookupService takes its lookup data as parameters instead of capturing it (Phase 2 §2.1),
    /// so these tests use small fake dictionaries -- previously impossible when this logic lived
    /// as static methods on GenshinProcesor, whose static constructor loaded real game data from disk.
    /// </summary>
    public class LookupServiceTests
    {
        [Fact]
        public void IsValidMaterial_MatchesByValueOrLowercaseKey()
        {
            var materials = new Dictionary<string, string> { ["mora"] = "Mora" };

            Assert.True(LookupService.IsValidMaterial("Mora", materials));
            Assert.True(LookupService.IsValidMaterial("mora", materials));
            Assert.False(LookupService.IsValidMaterial("Gems", materials));
        }

        [Fact]
        public void IsValidStat_MatchesByValueOnly()
        {
            var stats = new Dictionary<string, string> { ["critrate"] = "critRate_" };

            Assert.True(LookupService.IsValidStat("critRate_", stats));
            Assert.False(LookupService.IsValidStat("critrate", stats));
        }

        [Fact]
        public void IsValidSlot_MatchesCollectionMembership()
        {
            var gearSlots = new List<string> { "flower", "plume" };

            Assert.True(LookupService.IsValidSlot("flower", gearSlots));
            Assert.False(LookupService.IsValidSlot("sands", gearSlots));
        }

        [Fact]
        public void IsValidCharacter_MatchesSpecialCasesAndLowercaseKey()
        {
            var characters = new Dictionary<string, JObject> { ["diluc"] = new JObject() };

            Assert.True(LookupService.IsValidCharacter("Diluc", characters));
            Assert.True(LookupService.IsValidCharacter("PlayerBoy Traveler", characters));
            Assert.True(LookupService.IsValidCharacter("Wanderer", characters));
            Assert.False(LookupService.IsValidCharacter("Unknown", characters));
        }

        [Fact]
        public void IsValidElement_MatchesByValueOrLowercaseKey()
        {
            var elements = new Dictionary<string, string> { ["pyro"] = "Pyro" };

            Assert.True(LookupService.IsValidElement("Pyro", elements));
            Assert.True(LookupService.IsValidElement("pyro", elements));
            Assert.False(LookupService.IsValidElement("Hydro", elements));
        }

        [Fact]
        public void IsEnhancementMaterial_MatchesEnhancementSetOrMaterials()
        {
            var enhancementMaterials = new List<string> { "sanctifying essence" };
            var materials = new Dictionary<string, string> { ["mora"] = "Mora" };

            Assert.True(LookupService.IsEnhancementMaterial("Sanctifying Essence", enhancementMaterials, materials));
            Assert.True(LookupService.IsEnhancementMaterial("Mora", enhancementMaterials, materials));
            Assert.False(LookupService.IsEnhancementMaterial("Gems", enhancementMaterials, materials));
        }

        [Fact]
        public void IsValidWeapon_MatchesByValueOrLowercaseKey()
        {
            var weapons = new Dictionary<string, string> { ["favoniussword"] = "Favonius Sword" };

            Assert.True(LookupService.IsValidWeapon("Favonius Sword", weapons));
            Assert.True(LookupService.IsValidWeapon("favoniussword", weapons));
            Assert.False(LookupService.IsValidWeapon("Unknown", weapons));
        }

        [Fact]
        public void IsValidSetName_MatchesByKeyOrByLowercaseKey()
        {
            var artifacts = new Dictionary<string, JObject> { ["gladiatorsfinale"] = new JObject() };

            Assert.True(LookupService.IsValidSetName("gladiatorsfinale", artifacts));
            Assert.False(LookupService.IsValidSetName("unknown", artifacts));
        }
    }
}
