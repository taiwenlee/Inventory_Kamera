using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace InventoryKamera
{
	public static class UserInterface
	{
		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		// Character
		private static PictureBox cName_PictureBox;

		private static PictureBox cLevel_PictureBox;
		private static PictureBox[] cTalent_PictureBoxes = new PictureBox[3];
		private static TextBox character_TextBox;

		public static void Init(PictureBox _c_name, PictureBox _c_level, PictureBox[] _c_talent, TextBox _c_textbox)
		{
			// Characters
			cName_PictureBox = _c_name;
			cLevel_PictureBox = _c_level;
			cTalent_PictureBoxes = _c_talent;
			character_TextBox = _c_textbox;
		}

		private static void UpdateElements(Bitmap bm, string text, PictureBox pictureBox, TextBox textBox)
		{
			UpdatePictureBox(bm, pictureBox);
			UpdateTextBox(text, textBox);
		}

		private static void UpdatePictureBox(Bitmap bm, PictureBox pictureBox)
		{
			try
			{
				Bitmap clone = new Bitmap(bm.Width, bm.Height);
				using (var copy = Graphics.FromImage(clone))
				{
					copy.DrawImage(bm, 0, 0);
				}
				MethodInvoker pictureBoxAction = delegate
			{
				pictureBox.Image = clone;
				pictureBox.Refresh();
			};
				pictureBox.Invoke(pictureBoxAction);
			}
			catch (Exception e)
			{
				Logger.Debug(e, "Problem updating picturebox {0}", pictureBox.Name);
			}
		}

		private static void UpdateTextBox(string text, TextBox textBox)
		{
			try
			{
				MethodInvoker textBoxAction = delegate
				{
					textBox.AppendText(text.Replace("\n", Environment.NewLine));
					textBox.AppendText(Environment.NewLine);
					textBox.Refresh();
				};
				textBox.Invoke(textBoxAction);
			}
			catch (Exception e)
			{
				Logger.Debug(e, "Problem updating textbox {0}", textBox.Name);
			}
		}

		private static void UpdateLabel(string text, Label label)
		{
			try
			{
				MethodInvoker labelAction =  delegate
			{
				label.Text = text;
				label.Refresh();
			};
				label.Invoke(labelAction);
			}
			catch (Exception e)
			{
				Logger.Debug(e, "Problem updating label {0}", label.Name);

			}
		}

		internal static void SetMainCharacterName(string text)
		{
			UpdateTextBox($"Traveler name: {text}", character_TextBox);
		}

		public static void SetCharacter_NameAndElement(Bitmap bm, string name, string element)
		{
			UpdateElements(bm, $"Name: {name}\nElement: {element}", cName_PictureBox, character_TextBox);
		}

		public static void SetCharacter_Level(Bitmap bm, int level, int maxLevel)
		{
			UpdateElements(bm, $"Level: {level} / {maxLevel}", cLevel_PictureBox, character_TextBox);
		}

		public static void SetCharacter_Constellation(int level)
		{
			UpdateTextBox($"Constellation: {level}", character_TextBox);
		}

		public static void SetCharacter_Constellation(Bitmap bm, int level)
		{
			if (bm == null)
			{
				SetCharacter_Constellation(level);
				return;
			}
			// Reuse the character-level PictureBox to show the activated-constellation capture:
			// constellations are scanned in a later phase than level, so the two never contend for it.
			UpdateElements(bm, $"Constellation: {level}", cLevel_PictureBox, character_TextBox);
		}

		public static void SetCharacter_Talent(Bitmap bm, string text, int i)
		{
			if (i < 0 || i > 2) return;

			// Only update a talent PictureBox if one exists for this slot: the UI can have fewer
			// talent PictureBoxes than the 3 talents (auto/skill/burst) that always get scanned.
			// The talent level still goes to the text box regardless of how many boxes there are.
			if (i < cTalent_PictureBoxes.Length)
				UpdatePictureBox(bm, cTalent_PictureBoxes[i]);

			UpdateTextBox($"Talent {i + 1}: {text}", character_TextBox);
		}

		public static void ResetCharacterDisplay()
		{
			MethodInvoker nameAction = delegate { cName_PictureBox.Image = null; };
			MethodInvoker levelAction = delegate { cLevel_PictureBox.Image = null; };
			MethodInvoker textAction = delegate { character_TextBox.Clear(); };

			cName_PictureBox.Invoke(nameAction);
			cLevel_PictureBox.Invoke(levelAction);
			// Clear however many talent PictureBoxes the UI actually has (may be fewer than 3).
			foreach (var talentPictureBox in cTalent_PictureBoxes)
			{
				var pb = talentPictureBox;
				pb.Invoke((MethodInvoker)delegate { pb.Image = null; });
			}
			character_TextBox.Invoke(textAction);
		}

	}
}