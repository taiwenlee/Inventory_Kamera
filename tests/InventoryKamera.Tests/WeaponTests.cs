using System;
using Newtonsoft.Json.Linq;
using InventoryKamera;
using Xunit;

namespace InventoryKamera.Tests
{
    /// <summary>
    /// Characterization tests for the Weapon model. These pin the ascension-tier mapping,
    /// validation ranges, default-weapon naming, and GOOD serialization shape so the model can be
    /// refactored (and moved to typed export logic) safely in later phases. Validators that reach
    /// into GenshinProcesor's loaded lookup tables (name/character validity) are intentionally not
    /// exercised here to keep the tests deterministic and disk-free.
    /// </summary>
    public class WeaponTests
    {
        [Theory]
        [InlineData(1, false, 0)]
        [InlineData(19, false, 0)]
        [InlineData(20, false, 0)]   // level 20, not yet ascended
        [InlineData(20, true, 1)]    // level 20, ascended
        [InlineData(40, false, 1)]
        [InlineData(40, true, 2)]
        [InlineData(50, true, 3)]
        [InlineData(60, true, 4)]
        [InlineData(70, true, 5)]
        [InlineData(80, true, 6)]
        [InlineData(90, false, 6)]
        [InlineData(90, true, 6)]
        public void AscensionCount_MapsLevelAndAscensionFlagToTier(int level, bool ascended, int expected)
        {
            var weapon = new Weapon("SomeWeapon", level, ascended, _refinementLevel: 1, _rarity: 5);

            Assert.Equal(expected, weapon.AscensionCount());
            Assert.Equal(expected, weapon.AscensionLevel);
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(90, true)]
        [InlineData(0, false)]
        [InlineData(91, false)]
        public void HasValidLevel_AcceptsOneThroughNinety(int level, bool expected)
        {
            var weapon = new Weapon("SomeWeapon", level, false, 1, _rarity: 5);
            Assert.Equal(expected, weapon.HasValidLevel());
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, true)]
        [InlineData(5, true)]
        [InlineData(6, false)]
        public void HasValidRarity_AcceptsOneThroughFive(int rarity, bool expected)
        {
            var weapon = new Weapon("SomeWeapon", 1, false, 1, _rarity: rarity);
            Assert.Equal(expected, weapon.HasValidRarity());
        }

        [Fact]
        public void Constructor_ForcesRefinementToOne_ForRarityTwoAndBelow()
        {
            var lowRarity = new Weapon("SomeWeapon", 1, false, _refinementLevel: 5, _rarity: 2);
            var highRarity = new Weapon("SomeWeapon", 1, false, _refinementLevel: 5, _rarity: 3);

            Assert.Equal(1, lowRarity.RefinementLevel);   // 1-2 star weapons have no refinement
            Assert.Equal(5, highRarity.RefinementLevel);
        }

        [Theory]
        [InlineData(WeaponType.Sword, "DullBlade")]
        [InlineData(WeaponType.Claymore, "WasterGreatsword")]
        [InlineData(WeaponType.Polearm, "BeginnersProtector")]
        [InlineData(WeaponType.Bow, "HuntersBow")]
        [InlineData(WeaponType.Catalyst, "ApprenticesNotes")]
        public void TypeConstructor_AssignsDefaultWeaponName(WeaponType type, string expectedName)
        {
            var weapon = new Weapon(type, _equippedCharacter: "");
            Assert.Equal(expectedName, weapon.Name);
        }

        [Fact]
        public void TypeConstructor_ThrowsForInvalidWeaponType()
        {
            Assert.Throws<ArgumentException>(() => new Weapon((WeaponType)999, ""));
        }

        [Fact]
        public void Equality_IgnoresIdAndRarity_ComparesCoreFields()
        {
            var a = new Weapon("Blade", 90, true, 5, _id: 1, _rarity: 5);
            var b = new Weapon("Blade", 90, true, 5, _id: 999, _rarity: 5);

            Assert.True(a == b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Serialization_UsesGoodKeys_AndOmitsRarity()
        {
            var weapon = new Weapon("Blade", 80, true, 5, locked: true, _id: 42, _rarity: 5);

            var json = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(weapon));

            Assert.Equal("Blade", (string)json["key"]);
            Assert.Equal(80, (int)json["level"]);
            Assert.Equal(6, (int)json["ascension"]);   // level 80 ascended
            Assert.Equal(5, (int)json["refinement"]);
            Assert.True((bool)json["lock"]);
            Assert.Equal(42, (int)json["id"]);
            Assert.False(json.ContainsKey("rarity"));   // [JsonIgnore]
        }
    }
}
