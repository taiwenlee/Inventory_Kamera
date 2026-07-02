using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace InventoryKamera
{
	public static class UserInterface
	{
		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		// Artifacts and Weapons
		private static PictureBox gear_PictureBox;

		private static TextBox gear_TextBox;

		// Character
		private static PictureBox cName_PictureBox;

		private static PictureBox cLevel_PictureBox;
		private static PictureBox[] cTalent_PictureBoxes = new PictureBox[3];
		private static TextBox character_TextBox;

		// Current Images
		private static PictureBox navigation_PictureBox;

		public static void Init(PictureBox _gear_PictureBox, TextBox _a_textbox, PictureBox _c_name, PictureBox _c_level, PictureBox[] _c_talent, TextBox _c_textbox, PictureBox _navigation_Image)
		{
			// Artifacts and Weapons
			gear_PictureBox = _gear_PictureBox;
			gear_TextBox = _a_textbox;

			// Characters
			cName_PictureBox = _c_name;
			cLevel_PictureBox = _c_level;
			cTalent_PictureBoxes = _c_talent;
			character_TextBox = _c_textbox;

			// Navigation Image
			navigation_PictureBox = _navigation_Image;
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
				Logger.Debug($"Problem updating picturebox {0}\n{1}", pictureBox.Name, e);
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
				Logger.Debug($"Problem updating picturebox {0}\n{1}", textBox.Name, e);
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
				Logger.Debug($"Problem updating picturebox {0}\n{1}", label.Name, e);

			}
		}

		public static void SetGear(Bitmap bm, Weapon weapon)
		{
			ResetGearDisplay();
			SetGearPictureBox(bm);
			SetGearTextBox(weapon.ToString());
		}

		public static void SetGear(Bitmap bm, Artifact artifact)
		{
			ResetGearDisplay();
			SetGearPictureBox(bm);
			SetGearTextBox(artifact.ToString());
		}

		public static void SetGearPictureBox(Bitmap bm)
		{
			UpdatePictureBox(bm, gear_PictureBox);
		}

		public static void SetGearTextBox(string text)
		{
			UpdateTextBox(text, gear_TextBox);
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

		internal static void SetMaterial(Bitmap nameplate, Bitmap quantity, string name, int count)
		{
			UpdateElements(nameplate, $"Name: {name}", cName_PictureBox, character_TextBox);
			UpdateElements(quantity, $"Count: {count}", cLevel_PictureBox, character_TextBox);
		}

		public static void SetMora(Bitmap mora, int count)
		{
			UpdateElements(mora, $"Mora: {count}", navigation_PictureBox, character_TextBox);
		}


		public static void SetCharacter_Talent(Bitmap bm, string text, int i)
		{
			if (i > -1 && i < 3)
			{
				UpdatePictureBox(bm, cTalent_PictureBoxes[i]);
				UpdateTextBox($"Talent {i + 1}: {text}", character_TextBox);
			}
		}

		public static void SetNavigation_Image(Bitmap bm)
		{
			UpdatePictureBox(bm, navigation_PictureBox);
		}

		public static void ResetCharacterDisplay()
		{
			MethodInvoker nameAction = delegate { cName_PictureBox.Image = null; };
			MethodInvoker levelAction = delegate { cLevel_PictureBox.Image = null; };
			MethodInvoker talentAction_1 = delegate { cTalent_PictureBoxes[0].Image = null; };
			MethodInvoker talentAction_2 = delegate { cTalent_PictureBoxes[1].Image = null; };
			MethodInvoker talentAction_3 = delegate { cTalent_PictureBoxes[2].Image = null; };
			MethodInvoker textAction = delegate { character_TextBox.Clear(); };

			cName_PictureBox.Invoke(nameAction);
			cLevel_PictureBox.Invoke(levelAction);
			cTalent_PictureBoxes[0].Invoke(talentAction_1);
			cTalent_PictureBoxes[1].Invoke(talentAction_2);
			cTalent_PictureBoxes[2].Invoke(talentAction_3);
			character_TextBox.Invoke(textAction);
		}

		public static void ResetGearDisplay()
		{
			MethodInvoker gearAction = delegate { gear_PictureBox.Image = null; };

			MethodInvoker textAction = delegate { gear_TextBox.Clear(); };

			gear_PictureBox.Invoke(gearAction);
			gear_TextBox.Invoke(textAction);
		}

	}
}