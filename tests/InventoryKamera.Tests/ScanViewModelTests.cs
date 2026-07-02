using Xunit;

namespace InventoryKamera.Tests
{
    /// <summary>
    /// ScanViewModel owns real observable state for the counter group (Phase 2 §2.5, first MVVM
    /// slice) -- unlike the rest of IScanProgressReporter, which still bridges to UserInterface's
    /// Control.Invoke calls, this part has no WinForms dependency and is directly testable.
    /// </summary>
    public class ScanViewModelTests
    {
        [Fact]
        public void IncrementWeaponCount_IncrementsAndRaisesCountersChanged()
        {
            var viewModel = new ScanViewModel();
            int raisedCount = 0;
            viewModel.CountersChanged += () => raisedCount++;

            viewModel.IncrementWeaponCount();
            viewModel.IncrementWeaponCount();

            Assert.Equal(2, viewModel.WeaponCount);
            Assert.Equal(2, raisedCount);
        }

        [Fact]
        public void SetWeaponMax_IsNullUntilSet()
        {
            var viewModel = new ScanViewModel();

            Assert.Null(viewModel.WeaponMax);

            viewModel.SetWeapon_Max(42);

            Assert.Equal(42, viewModel.WeaponMax);
        }

        [Fact]
        public void ResetCounters_ZerosCountsAndNullsMax()
        {
            var viewModel = new ScanViewModel();
            viewModel.IncrementWeaponCount();
            viewModel.SetWeapon_Max(10);
            viewModel.IncrementArtifactCount();
            viewModel.SetArtifact_Max(20);
            viewModel.IncrementCharacterCount();

            viewModel.ResetCounters();

            Assert.Equal(0, viewModel.WeaponCount);
            Assert.Null(viewModel.WeaponMax);
            Assert.Equal(0, viewModel.ArtifactCount);
            Assert.Null(viewModel.ArtifactMax);
            Assert.Equal(0, viewModel.CharacterCount);
        }

        [Fact]
        public void IndependentCounters_DoNotInterfereWithEachOther()
        {
            var viewModel = new ScanViewModel();

            viewModel.IncrementWeaponCount();
            viewModel.IncrementArtifactCount();
            viewModel.IncrementArtifactCount();
            viewModel.IncrementCharacterCount();
            viewModel.IncrementCharacterCount();
            viewModel.IncrementCharacterCount();

            Assert.Equal(1, viewModel.WeaponCount);
            Assert.Equal(2, viewModel.ArtifactCount);
            Assert.Equal(3, viewModel.CharacterCount);
        }
    }
}
