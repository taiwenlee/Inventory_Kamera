using System.Drawing;
using InventoryKamera;
using Xunit;

namespace InventoryKamera.Tests
{
    /// <summary>
    /// Characterization tests for the RECT struct. These pin the current behaviour so the
    /// geometry helpers can be refactored safely in later phases.
    /// </summary>
    public class RectTests
    {
        [Fact]
        public void WidthAndHeight_AreDerivedFromEdges()
        {
            var rect = new RECT(Left: 10, Top: 20, Right: 60, Bottom: 100);

            Assert.Equal(50, rect.Width);
            Assert.Equal(80, rect.Height);
        }

        [Fact]
        public void SettingWidth_MovesRightEdgeRelativeToLeft()
        {
            var rect = new RECT(10, 20, 60, 100) { Width = 30 };

            Assert.Equal(40, rect.Right);
            Assert.Equal(30, rect.Width);
        }

        [Fact]
        public void SettingHeight_MovesBottomEdgeRelativeToTop()
        {
            var rect = new RECT(10, 20, 60, 100) { Height = 15 };

            Assert.Equal(35, rect.Bottom);
            Assert.Equal(15, rect.Height);
        }

        [Fact]
        public void ImplicitConversion_ToRectangle_UsesLocationAndSize()
        {
            RECT rect = new RECT(10, 20, 60, 100);
            Rectangle rectangle = rect;

            Assert.Equal(new Rectangle(10, 20, 50, 80), rectangle);
        }

        [Fact]
        public void ImplicitConversion_RoundTripsThroughRectangle()
        {
            var original = new RECT(10, 20, 60, 100);
            Rectangle rectangle = original;
            RECT roundTripped = rectangle;

            Assert.Equal(original, roundTripped);
        }

        [Fact]
        public void Location_ReflectsTopLeft()
        {
            var rect = new RECT(10, 20, 60, 100);

            Assert.Equal(new Point(10, 20), rect.Location);
            Assert.Equal(new Size(50, 80), rect.Size);
        }

        [Fact]
        public void Equality_ComparesAllEdges()
        {
            var a = new RECT(1, 2, 3, 4);
            var b = new RECT(1, 2, 3, 4);
            var c = new RECT(1, 2, 3, 5);

            Assert.True(a == b);
            Assert.True(a != c);
            Assert.Equal(a, b);
        }
    }
}
