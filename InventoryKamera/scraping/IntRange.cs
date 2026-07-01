namespace InventoryKamera
{
    /// <summary>
    /// Minimal inclusive integer range, replacing <c>Accord.IntRange</c> (the last Accord type used
    /// after the image filters were reimplemented in <see cref="ImageProcessing"/>).
    /// </summary>
    internal readonly struct IntRange
    {
        public int Min { get; }
        public int Max { get; }

        public IntRange(int min, int max)
        {
            Min = min;
            Max = max;
        }
    }
}
