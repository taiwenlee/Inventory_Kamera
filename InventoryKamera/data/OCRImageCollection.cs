using System.Collections.Generic;
using System.Drawing;

namespace InventoryKamera
{
	public class OCRImageCollection
	{
		public List<Bitmap> Bitmaps { get; set; }
		public string Type { get; set; }
		public int Id { get; private set; }

		public OCRImageCollection(List<Bitmap> _bm, string _type, int _id)
		{
			Bitmaps = _bm;
			Type = _type;
			Id = _id;
		}
	}
}