using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace InventoryKamera
{
    /// <summary>
    /// Validity checks against the game's lookup data (characters/weapons/artifacts/materials/stats).
    /// Extracted from <see cref="GenshinProcesor"/> (Phase 2 §2.1) as pure functions of
    /// <c>(data, input)</c> rather than an injected/stateful service: <c>GenshinProcesor.ReloadData()</c>
    /// *reassigns* its lookup dictionaries each scan (a fresh <see cref="Dictionary{TKey,TValue}"/>
    /// object, not an in-place mutation), so a service that captured them in a constructor would go
    /// stale after the first reload. Taking them as parameters sidesteps that entirely — same
    /// stateless-static-class shape as <see cref="ImageProcessing"/> from Phase 1, appropriate here
    /// because none of these checks need any state of their own once the data is explicit.
    /// <c>GenshinProcesor</c>'s existing <c>IsValidX</c> methods now forward here, passing their
    /// current static fields each call (always fresh, no staleness risk).
    /// </summary>
    internal static class LookupService
    {
        internal static bool IsValidSetName(string setName, Dictionary<string, JObject> artifacts)
        {
            if (artifacts.TryGetValue(setName, out _) || artifacts.TryGetValue(setName.ToLower(), out _)) return true;
            foreach (var artifactSet in artifacts.Values)
                foreach (var field in artifactSet)
                    if (field.ToString() == setName) return true;

            return false;
        }

        internal static bool IsValidMaterial(string name, Dictionary<string, string> materials)
        {
            return materials.ContainsValue(name) || materials.ContainsKey(name.ToLower());
        }

        internal static bool IsValidStat(string stat, Dictionary<string, string> stats)
        {
            return stats.ContainsValue(stat);
        }

        internal static bool IsValidSlot(string gearSlot, ICollection<string> gearSlots)
        {
            return gearSlots.Contains(gearSlot);
        }

        internal static bool IsValidCharacter(string character, Dictionary<string, JObject> characters)
        {
            return character.Contains("Traveler") || character == "Wanderer" || character == "Manequin1" || character == "Manequin2" || characters.ContainsKey(character.ToLower());
        }

        internal static bool IsValidElement(string element, Dictionary<string, string> elements)
        {
            return elements.ContainsValue(element) || elements.ContainsKey(element.ToLower());
        }

        internal static bool IsEnhancementMaterial(string material, ICollection<string> enhancementMaterials, Dictionary<string, string> materials)
        {
            return enhancementMaterials.Contains(material.ToLower()) || materials.ContainsValue(material) || materials.ContainsKey(material.ToLower());
        }

        internal static bool IsValidWeapon(string weapon, Dictionary<string, string> weapons)
        {
            return weapons.ContainsValue(weapon) || weapons.ContainsKey(weapon.ToLower());
        }
    }
}
