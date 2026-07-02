using System;
using System.Drawing;
using System.Threading;

namespace InventoryKamera
{
    /// <summary>
    /// First real slice of the MVVM redesign sketched in the plan for §2.5: owns genuine observable
    /// state for exactly one control group (the weapon/artifact/character counters) instead of
    /// delegating straight to <see cref="UserInterface"/>. <see cref="MainForm"/> owns one long-lived
    /// instance, subscribes to <see cref="CountersChanged"/> once at startup, and renders the counter
    /// labels itself from <see cref="WeaponCount"/>/<see cref="WeaponMax"/>/etc. -- instead of a
    /// shared static facade owning those controls. Everything else still bridges to
    /// <see cref="UserInterface"/> unchanged; carving out more control groups this way is deliberately
    /// left as separate, individually live-tested slices (see the plan doc's §2.5 sequencing note)
    /// rather than one large rewrite.
    /// </summary>
    internal sealed class ScanViewModel : IScanProgressReporter
    {
        private int weaponCount;
        private int? weaponMax;
        private int artifactCount;
        private int? artifactMax;
        private int characterCount;

        /// <summary>
        /// Raised after any counter-related state changes. Scan logic calls this from background
        /// worker threads; subscribers are responsible for marshaling onto the UI thread themselves.
        /// </summary>
        public event Action CountersChanged;

        public int WeaponCount => weaponCount;

        /// <summary>Null until <see cref="SetWeapon_Max"/> runs -- matches the original "?" placeholder.</summary>
        public int? WeaponMax => weaponMax;
        public int ArtifactCount => artifactCount;

        /// <summary>Null until <see cref="SetArtifact_Max"/> runs -- matches the original "?" placeholder.</summary>
        public int? ArtifactMax => artifactMax;
        public int CharacterCount => characterCount;

        public void SetWeapon_Max(int value)
        {
            weaponMax = value;
            CountersChanged?.Invoke();
        }

        public void SetArtifact_Max(int value)
        {
            artifactMax = value;
            CountersChanged?.Invoke();
        }

        public void IncrementWeaponCount()
        {
            Interlocked.Increment(ref weaponCount);
            CountersChanged?.Invoke();
        }

        public void IncrementArtifactCount()
        {
            Interlocked.Increment(ref artifactCount);
            CountersChanged?.Invoke();
        }

        public void IncrementCharacterCount()
        {
            Interlocked.Increment(ref characterCount);
            CountersChanged?.Invoke();
        }

        public void ResetCounters()
        {
            Volatile.Write(ref weaponCount, 0);
            weaponMax = null;
            Volatile.Write(ref artifactCount, 0);
            artifactMax = null;
            Volatile.Write(ref characterCount, 0);
            CountersChanged?.Invoke();
        }

        public void ResetAll()
        {
            UserInterface.ResetGearDisplay();
            UserInterface.ResetCharacterDisplay();
            ResetCounters();
            UserInterface.ResetErrors();
        }

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
        public void SetProgramStatus(string status, bool ok = true) => UserInterface.SetProgramStatus(status, ok);
        public void AddError(string error) => UserInterface.AddError(error);
        public void SetNavigation_Image(Bitmap bm) => UserInterface.SetNavigation_Image(bm);
        public void ResetCharacterDisplay() => UserInterface.ResetCharacterDisplay();
        public void ResetGearDisplay() => UserInterface.ResetGearDisplay();
        public void ResetErrors() => UserInterface.ResetErrors();
    }
}
