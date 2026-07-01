using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using InventoryKamera;
using Xunit;

namespace InventoryKamera.Tests
{
    /// <summary>
    /// Characterization tests for the Artifact model. These pin the substat-filtering rule,
    /// validation ranges, substat formatting, and GOOD serialization shape. Validators that reach
    /// into GenshinProcesor's loaded lookup tables (set/slot/stat/character validity) are
    /// intentionally not exercised here to keep the tests deterministic and disk-free.
    /// </summary>
    public class ArtifactTests
    {
        private static Artifact.SubStat Sub(string stat, decimal value) =>
            new Artifact.SubStat { stat = stat, value = value };

        [Fact]
        public void Constructor_DropsSubstatsWithNonPositiveValue()
        {
            var subs = new List<Artifact.SubStat> { Sub("atk_", 5.8m), Sub("critRate_", 0m) };
            var unactivated = new List<Artifact.SubStat> { Sub("def", 0m) };

            var artifact = new Artifact("GladiatorsFinale", 5, 20, "flower", "hp", subs, unactivated);

            Assert.Single(artifact.SubStats);
            Assert.Equal("atk_", artifact.SubStats[0].stat);
            Assert.Empty(artifact.unactivatedSubstats);
        }

        [Theory]
        [InlineData(-1, false)]
        [InlineData(0, true)]
        [InlineData(20, true)]
        [InlineData(21, false)]
        public void HasValidLevel_AcceptsZeroThroughTwenty(int level, bool expected)
        {
            var artifact = MakeArtifact(level: level, rarity: 5);
            Assert.Equal(expected, artifact.HasValidLevel());
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, true)]
        [InlineData(5, true)]
        [InlineData(6, false)]
        public void HasValidRarity_AcceptsOneThroughFive(int rarity, bool expected)
        {
            var artifact = MakeArtifact(level: 20, rarity: rarity);
            Assert.Equal(expected, artifact.HasValidRarity());
        }

        [Fact]
        public void SubStat_ToString_AppendsPercentForUnderscoreStats()
        {
            Assert.Equal("critRate_ + 5.8%", Sub("critRate_", 5.8m).ToString());
            Assert.Equal("atk + 16", Sub("atk", 16m).ToString());
            Assert.Equal("NULL", default(Artifact.SubStat).ToString());
        }

        [Fact]
        public void EmptyConstructor_HasSentinelDefaults()
        {
            var artifact = new Artifact();

            Assert.Equal(-1, artifact.Rarity);
            Assert.Equal(-1, artifact.Level);
            Assert.Null(artifact.SetName);
            Assert.Empty(artifact.SubStats);
        }

        [Fact]
        public void Serialization_UsesGoodKeys()
        {
            var artifact = MakeArtifact(level: 20, rarity: 5);

            var json = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(artifact));

            Assert.Equal("GladiatorsFinale", (string)json["setKey"]);
            Assert.Equal("flower", (string)json["slotKey"]);
            Assert.Equal("hp", (string)json["mainStatKey"]);
            Assert.Equal(5, (int)json["rarity"]);
            Assert.Equal(20, (int)json["level"]);
            Assert.True(json.ContainsKey("substats"));
        }

        private static Artifact MakeArtifact(int level, int rarity) =>
            new Artifact(
                _setName: "GladiatorsFinale",
                _rarity: rarity,
                _level: level,
                _gearSlot: "flower",
                _mainStat: "hp",
                _subStats: new List<Artifact.SubStat> { Sub("atk_", 5.8m) },
                _unactivatedSubStats: new List<Artifact.SubStat>());
    }
}
