using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace InventoryKamera.ui
{
    public partial class SettingsForm : Form
    {
        public SettingsForm()
        {
            InitializeComponent();

            UiTheme.RoundCorners(FileSelectButton, 4);
            UiTheme.RoundCorners(CloseButton, 6);
            UiTheme.ApplyWindowChromeTint(this);
        }

        private void ValidateCustomName(object sender, EventArgs e)
        {
            var textbox = sender as TextBox;
            var name = textbox.Text;

            if (!string.IsNullOrWhiteSpace(name))
            {
                textbox.BackColor = GenshinProcesor.Characters.ContainsKey(name.ConvertToGood().ToLower())
                    ? Color.Yellow
                    : Color.White;
            }
        }

        private void ValidateCustomName1(object sender, EventArgs e)
        {
            var textbox = sender as TextBox;
            var name = textbox.Text;

            if (!string.IsNullOrWhiteSpace(name))
            {
                textbox.BackColor = GenshinProcesor.Characters.ContainsKey(name.ConvertToGood().ToLower())
                    ? Color.Yellow
                    : Color.White;
            }
        }

        private void ValidateCustomName2(object sender, EventArgs e)
        {
            var textbox = sender as TextBox;
            var name = textbox.Text;

            if (!string.IsNullOrWhiteSpace(name))
            {
                textbox.BackColor = GenshinProcesor.Characters.ContainsKey(name.ConvertToGood().ToLower())
                    ? Color.Yellow
                    : Color.White;
            }
        }

        private void DisplayCustomNameTooltip(object sender, EventArgs e)
        {
            var textbox = sender as TextBox;

            if (textbox.BackColor == Color.Yellow)
            {
                var tooltip = new ToolTip();
                tooltip.Show($"{textbox.Text} already exists as a character's name.\n" +
                    $"This may affect equipping items to characters and is not fully supported yet.", textbox);
            }
        }

        private void FileSelectButton_Click(object sender, EventArgs e)
        {
            // A nicer file browser
            CommonOpenFileDialog d = new CommonOpenFileDialog
            {
                InitialDirectory = !System.IO.Directory.Exists(OutputPath_TextBox.Text) ? System.IO.Directory.GetCurrentDirectory() : OutputPath_TextBox.Text,
                IsFolderPicker = true
            };

            if (d.ShowDialog() == CommonFileDialogResult.Ok)
            {
                OutputPath_TextBox.Text = d.FileName;
            }
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
