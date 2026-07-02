using Xunit;

namespace InventoryKamera.Tests
{
    /// <summary>
    /// ScanSettings is a thin instance-method seam over the live Properties.Settings.Default
    /// (Phase 2 §2.3) -- deliberately a live pass-through, not a snapshot, since
    /// Properties.Settings.Default is two-way bound to MainForm/MainUI controls and can change
    /// mid-session. These confirm the forwarding reflects changes made after construction, exactly
    /// like reading Properties.Settings.Default directly would.
    /// </summary>
    public class ScanSettingsTests
    {
        [Fact]
        public void ScanWeapons_ReflectsLiveChangesToApplicationSettings()
        {
            bool original = Properties.Settings.Default.ScanWeapons;
            try
            {
                IScanSettings settings = new ScanSettings();

                Properties.Settings.Default.ScanWeapons = true;
                Assert.True(settings.ScanWeapons);

                Properties.Settings.Default.ScanWeapons = false;
                Assert.False(settings.ScanWeapons);
            }
            finally
            {
                Properties.Settings.Default.ScanWeapons = original;
            }
        }

        [Fact]
        public void MinimumWeaponLevel_ReflectsLiveChangesToApplicationSettings()
        {
            decimal original = Properties.Settings.Default.MinimumWeaponLevel;
            try
            {
                IScanSettings settings = new ScanSettings();

                Properties.Settings.Default.MinimumWeaponLevel = 42;
                Assert.Equal(42, settings.MinimumWeaponLevel);
            }
            finally
            {
                Properties.Settings.Default.MinimumWeaponLevel = original;
            }
        }

        [Fact]
        public void TravelerName_ReflectsLiveChangesToApplicationSettings()
        {
            string original = Properties.Settings.Default.TravelerName;
            try
            {
                IScanSettings settings = new ScanSettings();

                Properties.Settings.Default.TravelerName = "Aether";
                Assert.Equal("Aether", settings.TravelerName);
            }
            finally
            {
                Properties.Settings.Default.TravelerName = original;
            }
        }
    }
}
