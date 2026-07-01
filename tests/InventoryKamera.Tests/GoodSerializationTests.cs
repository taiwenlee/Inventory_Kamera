using Newtonsoft.Json.Linq;
using InventoryKamera;
using Xunit;

namespace InventoryKamera.Tests
{
    /// <summary>
    /// Characterization tests for the GOOD export envelope. The parameterless constructor is
    /// pure (no settings/data dependencies), so it pins the serialized shape and default values
    /// that downstream tools rely on. The data-populated constructor is exercised in later phases
    /// once settings can be injected rather than read from a static provider.
    /// </summary>
    public class GoodSerializationTests
    {
        [Fact]
        public void EmptyGood_SerializesWithDefaultEnvelopeValues()
        {
            var json = JObject.Parse(new GOOD().ToString());

            Assert.Equal("EMPTY", (string)json["format"]);
            Assert.Equal(0, (int)json["version"]);
            Assert.Equal("NOT FILLED", (string)json["source"]);
        }

        [Fact]
        public void EmptyGood_OmitsNullItemCollections()
        {
            // weapons/artifacts/characters/materials use DefaultValueHandling.Ignore and are null
            // on an empty envelope, so they must not appear in the output.
            var json = JObject.Parse(new GOOD().ToString());

            Assert.False(json.ContainsKey("weapons"));
            Assert.False(json.ContainsKey("artifacts"));
            Assert.False(json.ContainsKey("characters"));
            Assert.False(json.ContainsKey("materials"));
        }
    }
}
