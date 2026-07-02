using System.Drawing;

namespace InventoryKamera
{
    /// <summary>
    /// Default <see cref="IScanProgressReporter"/> implementation; delegates straight to the static
    /// <see cref="UserInterface"/>, which stays put -- it's wired directly to MainForm's WinForms
    /// controls via <see cref="UserInterface.Init"/> and does its own <c>Control.Invoke</c> thread
    /// marshaling. This class only exists so scan logic depends on a small interface instead of the
    /// concrete static type.
    /// </summary>
    internal sealed class UserInterfaceReporter : IScanProgressReporter
    {
        public void SetGear(Bitmap bm, Weapon weapon) => UserInterface.SetGear(bm, weapon);
        public void SetGear(Bitmap bm, Artifact artifact) => UserInterface.SetGear(bm, artifact);
        public void SetGearPictureBox(Bitmap bm) => UserInterface.SetGearPictureBox(bm);
        public void SetGearTextBox(string text) => UserInterface.SetGearTextBox(text);
        public void SetMainCharacterName(string text) => UserInterface.SetMainCharacterName(text);
        public void SetCharacter_NameAndElement(Bitmap bm, string name, string element) => UserInterface.SetCharacter_NameAndElement(bm, name, element);
        public void SetCharacter_Level(Bitmap bm, int level, int maxLevel) => UserInterface.SetCharacter_Level(bm, level, maxLevel);
        public void SetCharacter_Constellation(int level) => UserInterface.SetCharacter_Constellation(level);
        public void SetMaterial(Bitmap nameplate, Bitmap quantity, string name, int count) => UserInterface.SetMaterial(nameplate, quantity, name, count);
        public void SetMora(Bitmap mora, int count) => UserInterface.SetMora(mora, count);
        public void SetCharacter_Talent(Bitmap bm, string text, int i) => UserInterface.SetCharacter_Talent(bm, text, i);
        public void SetWeapon_Max(int value) => UserInterface.SetWeapon_Max(value);
        public void SetArtifact_Max(int value) => UserInterface.SetArtifact_Max(value);
        public void IncrementArtifactCount() => UserInterface.IncrementArtifactCount();
        public void IncrementWeaponCount() => UserInterface.IncrementWeaponCount();
        public void IncrementCharacterCount() => UserInterface.IncrementCharacterCount();
        public void SetProgramStatus(string status, bool ok = true) => UserInterface.SetProgramStatus(status, ok);
        public void AddError(string error) => UserInterface.AddError(error);
        public void SetNavigation_Image(Bitmap bm) => UserInterface.SetNavigation_Image(bm);
        public void ResetCharacterDisplay() => UserInterface.ResetCharacterDisplay();
        public void ResetGearDisplay() => UserInterface.ResetGearDisplay();
        public void ResetCounters() => UserInterface.ResetCounters();
        public void ResetErrors() => UserInterface.ResetErrors();
        public void ResetAll() => UserInterface.ResetAll();
    }
}
