namespace InventoryKamera.ui
{
    partial class SettingsForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            CharacterNamesGroupBox = new FlatGroupBox();
            label1 = new System.Windows.Forms.Label();
            travelerNameTextBox = new System.Windows.Forms.TextBox();
            label4 = new System.Windows.Forms.Label();
            wandererNameTextBox = new System.Windows.Forms.TextBox();
            label6 = new System.Windows.Forms.Label();
            textBox1 = new System.Windows.Forms.TextBox();
            label7 = new System.Windows.Forms.Label();
            textBox2 = new System.Windows.Forms.TextBox();
            OutputGroupBox = new FlatGroupBox();
            FastScannerDelay_Label = new System.Windows.Forms.Label();
            MidScannerDelay_Label = new System.Windows.Forms.Label();
            SlowScannerDelay_Label = new System.Windows.Forms.Label();
            FileLocation_Label = new System.Windows.Forms.Label();
            FileSelectButton = new System.Windows.Forms.Button();
            OutputPath_TextBox = new System.Windows.Forms.TextBox();
            LogScreenshotsCheckBox = new System.Windows.Forms.CheckBox();
            ScannerDelay_Label = new System.Windows.Forms.Label();
            ScannerDelay_TrackBar = new System.Windows.Forms.TrackBar();
            OcrConfidenceThreshold_Label = new System.Windows.Forms.Label();
            OcrConfidenceThreshold_NumericUpDown = new System.Windows.Forms.NumericUpDown();
            CloseButton = new System.Windows.Forms.Button();
            screenshotsToolTip = new System.Windows.Forms.ToolTip(components);
            CharacterNamesGroupBox.SuspendLayout();
            OutputGroupBox.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)ScannerDelay_TrackBar).BeginInit();
            ((System.ComponentModel.ISupportInitialize)OcrConfidenceThreshold_NumericUpDown).BeginInit();
            SuspendLayout();
            //
            // CharacterNamesGroupBox
            //
            CharacterNamesGroupBox.Controls.Add(label1);
            CharacterNamesGroupBox.Controls.Add(travelerNameTextBox);
            CharacterNamesGroupBox.Controls.Add(label4);
            CharacterNamesGroupBox.Controls.Add(wandererNameTextBox);
            CharacterNamesGroupBox.Controls.Add(label6);
            CharacterNamesGroupBox.Controls.Add(textBox1);
            CharacterNamesGroupBox.Controls.Add(label7);
            CharacterNamesGroupBox.Controls.Add(textBox2);
            CharacterNamesGroupBox.Location = new System.Drawing.Point(12, 12);
            CharacterNamesGroupBox.Name = "CharacterNamesGroupBox";
            CharacterNamesGroupBox.Size = new System.Drawing.Size(268, 215);
            CharacterNamesGroupBox.TabIndex = 0;
            CharacterNamesGroupBox.TabStop = false;
            CharacterNamesGroupBox.Text = "Character Names";
            //
            // label1
            //
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(6, 20);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(94, 15);
            label1.TabIndex = 0;
            label1.Text = "Traveler's Name:";
            //
            // travelerNameTextBox
            //
            travelerNameTextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", global::InventoryKamera.Properties.Settings.Default, "TravelerName", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged));
            travelerNameTextBox.Location = new System.Drawing.Point(6, 38);
            travelerNameTextBox.Name = "travelerNameTextBox";
            travelerNameTextBox.Size = new System.Drawing.Size(250, 23);
            travelerNameTextBox.TabIndex = 1;
            travelerNameTextBox.TextChanged += ValidateCustomName;
            travelerNameTextBox.MouseHover += DisplayCustomNameTooltip;
            //
            // label4
            //
            label4.AutoSize = true;
            label4.Location = new System.Drawing.Point(6, 68);
            label4.Name = "label4";
            label4.Size = new System.Drawing.Size(104, 15);
            label4.TabIndex = 2;
            label4.Text = "Wanderer's Name:";
            //
            // wandererNameTextBox
            //
            wandererNameTextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", global::InventoryKamera.Properties.Settings.Default, "WandererName", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged));
            wandererNameTextBox.Location = new System.Drawing.Point(6, 86);
            wandererNameTextBox.Name = "wandererNameTextBox";
            wandererNameTextBox.Size = new System.Drawing.Size(250, 23);
            wandererNameTextBox.TabIndex = 3;
            wandererNameTextBox.TextChanged += ValidateCustomName;
            wandererNameTextBox.MouseHover += DisplayCustomNameTooltip;
            //
            // label6
            //
            label6.AutoSize = true;
            label6.Location = new System.Drawing.Point(6, 116);
            label6.Name = "label6";
            label6.Size = new System.Drawing.Size(122, 15);
            label6.TabIndex = 4;
            label6.Text = "Manequin's (f) Name:";
            //
            // textBox1
            //
            textBox1.DataBindings.Add(new System.Windows.Forms.Binding("Text", global::InventoryKamera.Properties.Settings.Default, "Manequin1Name", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged));
            textBox1.Location = new System.Drawing.Point(6, 134);
            textBox1.Name = "textBox1";
            textBox1.Size = new System.Drawing.Size(250, 23);
            textBox1.TabIndex = 5;
            textBox1.TextChanged += ValidateCustomName1;
            //
            // label7
            //
            label7.AutoSize = true;
            label7.Location = new System.Drawing.Point(6, 164);
            label7.Name = "label7";
            label7.Size = new System.Drawing.Size(129, 15);
            label7.TabIndex = 6;
            label7.Text = "Manequin's (m) Name:";
            //
            // textBox2
            //
            textBox2.DataBindings.Add(new System.Windows.Forms.Binding("Text", global::InventoryKamera.Properties.Settings.Default, "Manequin2Name", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged));
            textBox2.Location = new System.Drawing.Point(6, 182);
            textBox2.Name = "textBox2";
            textBox2.Size = new System.Drawing.Size(250, 23);
            textBox2.TabIndex = 7;
            textBox2.TextChanged += ValidateCustomName2;
            //
            // OutputGroupBox
            //
            OutputGroupBox.Controls.Add(FastScannerDelay_Label);
            OutputGroupBox.Controls.Add(MidScannerDelay_Label);
            OutputGroupBox.Controls.Add(SlowScannerDelay_Label);
            OutputGroupBox.Controls.Add(FileLocation_Label);
            OutputGroupBox.Controls.Add(FileSelectButton);
            OutputGroupBox.Controls.Add(OutputPath_TextBox);
            OutputGroupBox.Controls.Add(LogScreenshotsCheckBox);
            OutputGroupBox.Controls.Add(ScannerDelay_Label);
            OutputGroupBox.Controls.Add(ScannerDelay_TrackBar);
            OutputGroupBox.Controls.Add(OcrConfidenceThreshold_Label);
            OutputGroupBox.Controls.Add(OcrConfidenceThreshold_NumericUpDown);
            OutputGroupBox.Location = new System.Drawing.Point(292, 12);
            OutputGroupBox.Name = "OutputGroupBox";
            OutputGroupBox.Size = new System.Drawing.Size(277, 215);
            OutputGroupBox.TabIndex = 1;
            OutputGroupBox.TabStop = false;
            OutputGroupBox.Text = "Output";
            //
            // FastScannerDelay_Label
            //
            FastScannerDelay_Label.AutoSize = true;
            FastScannerDelay_Label.Font = new System.Drawing.Font("Segoe UI", 6F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
            FastScannerDelay_Label.Location = new System.Drawing.Point(6, 160);
            FastScannerDelay_Label.Name = "FastScannerDelay_Label";
            FastScannerDelay_Label.Size = new System.Drawing.Size(20, 9);
            FastScannerDelay_Label.TabIndex = 0;
            FastScannerDelay_Label.Text = "Fast";
            //
            // MidScannerDelay_Label
            //
            MidScannerDelay_Label.AutoSize = true;
            MidScannerDelay_Label.Font = new System.Drawing.Font("Segoe UI", 6F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
            MidScannerDelay_Label.Location = new System.Drawing.Point(100, 160);
            MidScannerDelay_Label.Name = "MidScannerDelay_Label";
            MidScannerDelay_Label.Size = new System.Drawing.Size(29, 9);
            MidScannerDelay_Label.TabIndex = 1;
            MidScannerDelay_Label.Text = "Slower";
            //
            // SlowScannerDelay_Label
            //
            SlowScannerDelay_Label.AutoSize = true;
            SlowScannerDelay_Label.Font = new System.Drawing.Font("Segoe UI", 6F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
            SlowScannerDelay_Label.Location = new System.Drawing.Point(190, 160);
            SlowScannerDelay_Label.Name = "SlowScannerDelay_Label";
            SlowScannerDelay_Label.Size = new System.Drawing.Size(22, 9);
            SlowScannerDelay_Label.TabIndex = 2;
            SlowScannerDelay_Label.Text = "Slow";
            //
            // FileLocation_Label
            //
            FileLocation_Label.AutoSize = true;
            FileLocation_Label.Font = new System.Drawing.Font("Segoe UI", 7.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
            FileLocation_Label.Location = new System.Drawing.Point(6, 20);
            FileLocation_Label.Name = "FileLocation_Label";
            FileLocation_Label.Size = new System.Drawing.Size(69, 13);
            FileLocation_Label.TabIndex = 3;
            FileLocation_Label.Text = "File Location:";
            //
            // FileSelectButton
            //
            FileSelectButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(222, 216, 205);
            FileSelectButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            FileSelectButton.Font = new System.Drawing.Font("Segoe UI", 6.2F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
            FileSelectButton.Location = new System.Drawing.Point(6, 38);
            FileSelectButton.Name = "FileSelectButton";
            FileSelectButton.Size = new System.Drawing.Size(45, 20);
            FileSelectButton.TabIndex = 4;
            FileSelectButton.Text = "Select";
            FileSelectButton.UseVisualStyleBackColor = true;
            FileSelectButton.Click += FileSelectButton_Click;
            //
            // OutputPath_TextBox
            //
            OutputPath_TextBox.BackColor = System.Drawing.Color.White;
            OutputPath_TextBox.DataBindings.Add(new System.Windows.Forms.Binding("Text", global::InventoryKamera.Properties.Settings.Default, "OutputPath", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged));
            OutputPath_TextBox.Font = new System.Drawing.Font("Segoe UI", 7F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
            OutputPath_TextBox.Location = new System.Drawing.Point(55, 38);
            OutputPath_TextBox.Name = "OutputPath_TextBox";
            OutputPath_TextBox.Size = new System.Drawing.Size(215, 20);
            OutputPath_TextBox.TabIndex = 5;
            //
            // LogScreenshotsCheckBox
            //
            LogScreenshotsCheckBox.AutoSize = true;
            LogScreenshotsCheckBox.DataBindings.Add(new System.Windows.Forms.Binding("Checked", global::InventoryKamera.Properties.Settings.Default, "LogScreenshots", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged));
            LogScreenshotsCheckBox.Location = new System.Drawing.Point(6, 68);
            LogScreenshotsCheckBox.Name = "LogScreenshotsCheckBox";
            LogScreenshotsCheckBox.Size = new System.Drawing.Size(129, 19);
            LogScreenshotsCheckBox.TabIndex = 6;
            LogScreenshotsCheckBox.Text = "Log All Screenshots";
            screenshotsToolTip.SetToolTip(LogScreenshotsCheckBox, "Debug tool. If enabled, all screenshots will be logged to local files. \r\nAll screenshots will be cleared when a new scan is started.\r\n");
            LogScreenshotsCheckBox.UseVisualStyleBackColor = true;
            //
            // ScannerDelay_Label
            //
            ScannerDelay_Label.AutoSize = true;
            ScannerDelay_Label.Location = new System.Drawing.Point(6, 96);
            ScannerDelay_Label.Name = "ScannerDelay_Label";
            ScannerDelay_Label.Size = new System.Drawing.Size(81, 15);
            ScannerDelay_Label.TabIndex = 7;
            ScannerDelay_Label.Text = "Scanner Delay";
            //
            // ScannerDelay_TrackBar
            //
            ScannerDelay_TrackBar.DataBindings.Add(new System.Windows.Forms.Binding("Value", global::InventoryKamera.Properties.Settings.Default, "ScannerDelay", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged, null, "N0"));
            ScannerDelay_TrackBar.Location = new System.Drawing.Point(6, 114);
            ScannerDelay_TrackBar.Maximum = 2;
            ScannerDelay_TrackBar.Name = "ScannerDelay_TrackBar";
            ScannerDelay_TrackBar.Size = new System.Drawing.Size(260, 45);
            ScannerDelay_TrackBar.TabIndex = 8;
            //
            // OcrConfidenceThreshold_Label
            //
            OcrConfidenceThreshold_Label.AutoSize = true;
            OcrConfidenceThreshold_Label.Location = new System.Drawing.Point(6, 186);
            OcrConfidenceThreshold_Label.Name = "OcrConfidenceThreshold_Label";
            OcrConfidenceThreshold_Label.Size = new System.Drawing.Size(170, 15);
            OcrConfidenceThreshold_Label.TabIndex = 9;
            OcrConfidenceThreshold_Label.Text = "OCR Correction Threshold (%)";
            screenshotsToolTip.SetToolTip(OcrConfidenceThreshold_Label, "When a scanned value's recognition confidence falls below this percentage, you'll be asked to confirm or correct it inline instead of it being used automatically.");
            //
            // OcrConfidenceThreshold_NumericUpDown
            //
            OcrConfidenceThreshold_NumericUpDown.DataBindings.Add(new System.Windows.Forms.Binding("Value", global::InventoryKamera.Properties.Settings.Default, "OcrConfidenceThreshold", true, System.Windows.Forms.DataSourceUpdateMode.OnPropertyChanged));
            OcrConfidenceThreshold_NumericUpDown.Location = new System.Drawing.Point(200, 183);
            OcrConfidenceThreshold_NumericUpDown.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            OcrConfidenceThreshold_NumericUpDown.Name = "OcrConfidenceThreshold_NumericUpDown";
            OcrConfidenceThreshold_NumericUpDown.Size = new System.Drawing.Size(60, 23);
            OcrConfidenceThreshold_NumericUpDown.TabIndex = 10;
            screenshotsToolTip.SetToolTip(OcrConfidenceThreshold_NumericUpDown, "When a scanned value's recognition confidence falls below this percentage, you'll be asked to confirm or correct it inline instead of it being used automatically.");
            //
            // CloseButton
            //
            CloseButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(222, 216, 205);
            CloseButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            CloseButton.Location = new System.Drawing.Point(494, 237);
            CloseButton.Name = "CloseButton";
            CloseButton.Size = new System.Drawing.Size(75, 25);
            CloseButton.TabIndex = 2;
            CloseButton.Text = "Close";
            CloseButton.UseVisualStyleBackColor = true;
            CloseButton.Click += CloseButton_Click;
            //
            // SettingsForm
            //
            AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            BackColor = System.Drawing.Color.FromArgb(245, 244, 237);
            Font = new System.Drawing.Font("Segoe UI", 9F);
            ClientSize = new System.Drawing.Size(581, 274);
            Controls.Add(CloseButton);
            Controls.Add(OutputGroupBox);
            Controls.Add(CharacterNamesGroupBox);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "SettingsForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Advanced Settings";
            CharacterNamesGroupBox.ResumeLayout(false);
            CharacterNamesGroupBox.PerformLayout();
            OutputGroupBox.ResumeLayout(false);
            OutputGroupBox.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)ScannerDelay_TrackBar).EndInit();
            ((System.ComponentModel.ISupportInitialize)OcrConfidenceThreshold_NumericUpDown).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private FlatGroupBox CharacterNamesGroupBox;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox travelerNameTextBox;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox wandererNameTextBox;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.TextBox textBox2;
        private FlatGroupBox OutputGroupBox;
        private System.Windows.Forms.Label FastScannerDelay_Label;
        private System.Windows.Forms.Label MidScannerDelay_Label;
        private System.Windows.Forms.Label SlowScannerDelay_Label;
        private System.Windows.Forms.Label FileLocation_Label;
        private System.Windows.Forms.Button FileSelectButton;
        private System.Windows.Forms.TextBox OutputPath_TextBox;
        private System.Windows.Forms.CheckBox LogScreenshotsCheckBox;
        private System.Windows.Forms.Label ScannerDelay_Label;
        private System.Windows.Forms.TrackBar ScannerDelay_TrackBar;
        private System.Windows.Forms.Label OcrConfidenceThreshold_Label;
        private System.Windows.Forms.NumericUpDown OcrConfidenceThreshold_NumericUpDown;
        private System.Windows.Forms.Button CloseButton;
        private System.Windows.Forms.ToolTip screenshotsToolTip;
    }
}
