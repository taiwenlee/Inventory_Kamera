using System;
using System.Drawing;
using System.Windows.Forms;

namespace InventoryKamera.ui
{
    /// <summary>
    /// Phase 3 §3.3 inline OCR correction dialog. Shown modally from <see cref="MainForm"/>'s
    /// <c>ScanViewModel.CorrectionRequested</c> handler when a scan-thread OCR result falls below the
    /// configured confidence threshold. Built entirely in code rather than via the WinForms Designer
    /// surface -- <c>MainForm.Designer.cs</c> has repeatedly been corrupted by opening the Designer in
    /// this repo (see MODERNIZATION_PLAN.md §3.0), and this dialog's layout is simple enough not to
    /// need it.
    /// </summary>
    internal sealed class OcrCorrectionForm : Form
    {
        private readonly TextBox correctedTextBox;

        /// <summary>Null if the user cancelled/skipped; otherwise the (possibly unchanged) corrected text.</summary>
        public string CorrectedText { get; private set; }

        public OcrCorrectionForm(Bitmap image, string recognizedText, float confidencePercent, string fieldLabel)
        {
            Text = "Low-confidence OCR result";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            BackColor = UiTheme.Background;
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(360, 260);
            Padding = new Padding(16);

            var fieldLabelControl = new Label
            {
                Text = $"{fieldLabel} — recognized with {confidencePercent:0}% confidence:",
                AutoSize = false,
                Location = new Point(16, 12),
                Size = new Size(328, 32),
            };

            var pictureBox = new PictureBox
            {
                Image = image,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(16, 48),
                Size = new Size(328, 100),
            };

            var promptLabel = new Label
            {
                Text = "Correct value (leave as-is to accept):",
                AutoSize = false,
                Location = new Point(16, 156),
                Size = new Size(328, 20),
            };

            correctedTextBox = new TextBox
            {
                Text = recognizedText,
                Location = new Point(16, 178),
                Size = new Size(328, 24),
                Font = new Font("Segoe UI", 10F),
            };

            var okButton = new Button
            {
                Text = "Use this value",
                DialogResult = DialogResult.OK,
                Location = new Point(139, 216),
                Size = new Size(100, 30),
                BackColor = UiTheme.Accent,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            okButton.FlatAppearance.BorderSize = 0;
            okButton.Click += (s, e) => { CorrectedText = correctedTextBox.Text; Close(); };

            var skipButton = new Button
            {
                Text = "Skip",
                DialogResult = DialogResult.Cancel,
                Location = new Point(245, 216),
                Size = new Size(99, 30),
            };

            Controls.Add(fieldLabelControl);
            Controls.Add(pictureBox);
            Controls.Add(promptLabel);
            Controls.Add(correctedTextBox);
            Controls.Add(okButton);
            Controls.Add(skipButton);

            AcceptButton = okButton;
            CancelButton = skipButton;
        }
    }
}
