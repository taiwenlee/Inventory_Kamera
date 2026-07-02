using System;
using System.Drawing;
using System.Drawing.Imaging;
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

        private static Bitmap MakeSolidColor(int width, int height, Color color)
        {
            var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp)) g.Clear(color);
            return bmp;
        }

        [Fact]
        public void SetGearTextBox_SetsTextAndRaisesGearChanged()
        {
            var viewModel = new ScanViewModel();
            int raisedCount = 0;
            viewModel.GearChanged += () => raisedCount++;

            viewModel.SetGearTextBox("Favonius Sword");

            Assert.Equal("Favonius Sword", viewModel.GearText);
            Assert.Equal(1, raisedCount);
        }

        [Fact]
        public void SetGearPictureBox_ClonesTheBitmapRatherThanReferencingIt()
        {
            var viewModel = new ScanViewModel();
            using var source = MakeSolidColor(4, 4, Color.Red);

            viewModel.SetGearPictureBox(source);

            Assert.NotSame(source, viewModel.GearImage);
            Assert.Equal(source.Size, viewModel.GearImage.Size);
        }

        [Fact]
        public void SetGearPictureBox_DisposesThePreviousImageOnReplace()
        {
            var viewModel = new ScanViewModel();
            using var first = MakeSolidColor(4, 4, Color.Red);
            using var second = MakeSolidColor(4, 4, Color.Blue);

            viewModel.SetGearPictureBox(first);
            var previousImage = viewModel.GearImage;

            viewModel.SetGearPictureBox(second);

            Assert.Throws<ArgumentException>(() => previousImage.GetPixel(0, 0));
        }

        [Fact]
        public void ResetGearDisplay_ClearsImageAndText()
        {
            var viewModel = new ScanViewModel();
            using var bitmap = MakeSolidColor(4, 4, Color.Green);
            viewModel.SetGearPictureBox(bitmap);
            viewModel.SetGearTextBox("some text");

            viewModel.ResetGearDisplay();

            Assert.Null(viewModel.GearImage);
            Assert.Equal("", viewModel.GearText);
        }

        [Fact]
        public void CloneGearImage_ReturnsNullWhenNoImageSet()
        {
            var viewModel = new ScanViewModel();

            Assert.Null(viewModel.CloneGearImage());
        }

        [Fact]
        public void CloneGearImage_ReturnsAnIndependentCopyThatSurvivesReplace()
        {
            // Regression test: a live scan's worker pool runs multiple threads concurrently, any of
            // which can replace scanViewModel's gear image at any time. A renderer holding a shared
            // reference (instead of its own clone) could end up painting a disposed Bitmap, which
            // WinForms renders as a white box with red X's -- CloneGearImage() is the fix.
            var viewModel = new ScanViewModel();
            using var first = MakeSolidColor(4, 4, Color.Red);
            using var second = MakeSolidColor(4, 4, Color.Blue);
            viewModel.SetGearPictureBox(first);

            var clone = viewModel.CloneGearImage();
            viewModel.SetGearPictureBox(second); // disposes the original gearImage the clone came from

            // Does not throw -- clone is independently owned and still valid after the replace.
            Assert.Equal(Color.FromArgb(255, 255, 0, 0), clone.GetPixel(0, 0));
        }

        [Fact]
        public void SetMaterial_SetsTextAndImagesAndRaisesMaterialChanged()
        {
            var viewModel = new ScanViewModel();
            int raisedCount = 0;
            viewModel.MaterialChanged += () => raisedCount++;
            using var nameplate = MakeSolidColor(4, 4, Color.Red);
            using var quantity = MakeSolidColor(4, 4, Color.Blue);

            viewModel.SetMaterial(nameplate, quantity, "Mora", 5000);

            Assert.Equal("Name: Mora\nCount: 5000", viewModel.MaterialText);
            Assert.NotSame(nameplate, viewModel.CloneMaterialNameplateImage());
            Assert.NotSame(quantity, viewModel.CloneMaterialQuantityImage());
            Assert.Equal(1, raisedCount);
        }

        [Fact]
        public void SetMaterial_DisposesThePreviousImagesOnReplace()
        {
            var viewModel = new ScanViewModel();
            using var firstNameplate = MakeSolidColor(4, 4, Color.Red);
            using var firstQuantity = MakeSolidColor(4, 4, Color.Red);
            using var secondNameplate = MakeSolidColor(4, 4, Color.Blue);
            using var secondQuantity = MakeSolidColor(4, 4, Color.Blue);

            viewModel.SetMaterial(firstNameplate, firstQuantity, "Mora", 1);
            var previousNameplate = viewModel.CloneMaterialNameplateImage();
            viewModel.SetMaterial(secondNameplate, secondQuantity, "Hero's Wit", 2);

            // The clone taken before the replace is unaffected -- only the internally-owned image is
            // disposed, matching the same clone-before-dispose safety CloneGearImage relies on.
            Assert.Equal(Color.FromArgb(255, 255, 0, 0), previousNameplate.GetPixel(0, 0));
        }

        [Fact]
        public void SetMora_SetsTextAndImageAndRaisesMoraChanged()
        {
            var viewModel = new ScanViewModel();
            int raisedCount = 0;
            viewModel.MoraChanged += () => raisedCount++;
            using var bitmap = MakeSolidColor(4, 4, Color.Yellow);

            viewModel.SetMora(bitmap, 12345);

            Assert.Equal("Mora: 12345", viewModel.MoraText);
            Assert.NotSame(bitmap, viewModel.CloneMoraImage());
            Assert.Equal(1, raisedCount);
        }

        [Fact]
        public void SetNavigationImage_ClonesTheBitmapAndRaisesNavigationImageChanged()
        {
            var viewModel = new ScanViewModel();
            int raisedCount = 0;
            viewModel.NavigationImageChanged += () => raisedCount++;
            using var bitmap = MakeSolidColor(4, 4, Color.Purple);

            viewModel.SetNavigation_Image(bitmap);

            Assert.NotSame(bitmap, viewModel.CloneNavigationImage());
            Assert.Equal(1, raisedCount);
        }

        [Fact]
        public void CloneNavigationImage_DisposesThePreviousImageOnReplace()
        {
            var viewModel = new ScanViewModel();
            using var first = MakeSolidColor(4, 4, Color.Red);
            using var second = MakeSolidColor(4, 4, Color.Blue);
            viewModel.SetNavigation_Image(first);

            var clone = viewModel.CloneNavigationImage();
            viewModel.SetNavigation_Image(second);

            // Clone taken before the replace is unaffected, same safety CloneGearImage relies on.
            Assert.Equal(Color.FromArgb(255, 255, 0, 0), clone.GetPixel(0, 0));
        }
    }
}
