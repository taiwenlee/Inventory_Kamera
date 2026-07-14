using InventoryKamera.ui;
using Microsoft.WindowsAPICodePack.Dialogs;
using Newtonsoft.Json;
using NHotkey;
using NHotkey.WindowsForms;
using Octokit;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using WindowsInput.Native;
using Application = System.Windows.Forms.Application;

namespace InventoryKamera
{
    public partial class MainForm : Form
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private static Thread scannerThread;

        // Owns the weapon/artifact/character counter state (first real slice of the MVVM redesign,
        // Phase 2 §2.5) -- long-lived across scans (unlike `data`, which gets recreated per scan below)
        // so subscribers never need to re-subscribe. Constructed in the constructor (in dependency
        // order: scanViewModel first, then `data`, which takes it) rather than via field initializers,
        // so the WinForms designer -- which instantiates this form to render it -- doesn't run
        // DatabaseManager's constructor, which touches the filesystem and can trigger a blocking network
        // data download that hangs/breaks the designer.
        private static ScanViewModel scanViewModel;
        private static InventoryKamera data;
        private static DatabaseManager databaseManager;

        private bool running = false;

        public MainForm()
        {
            InitializeComponent();

            // The WinForms designer instantiates this form in its designer process to render it. Bail
            // out before any runtime-only wiring -- above all the scanViewModel/data/databaseManager
            // construction below: DatabaseManager's constructor creates folders, reads/writes files and,
            // when the data files are absent (as in the designer's working directory), kicks off a
            // blocking network download that hangs/breaks the designer. LicenseManager is the reliable
            // design-time signal inside a constructor (this.DesignMode is still false this early).
            if (LicenseManager.UsageMode == LicenseUsageMode.Designtime) return;

            scanViewModel = new ScanViewModel();
            data = new InventoryKamera(scanViewModel);
            databaseManager = new DatabaseManager();

            BindSettings();

            Language_ComboBox.SelectedItem = "ENG";

            var version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
#if DEBUG
            version = Assembly.GetExecutingAssembly().GetName().Version.ToString(4);
#endif
            Logger.Info("Inventory Kamera version {0}", version);

            Text = $"Inventory Kamera V{version}";

            UserInterface.Init(
                CharacterName_PictureBox,
                CharacterLevel_PictureBox,
                new[] { CharacterTalent1_PictureBox },
                CharacterOutput_TextBox);

            scanViewModel.CountersChanged += OnCountersChanged;
            scanViewModel.ProgramStatusChanged += OnProgramStatusChanged;
            scanViewModel.ErrorAdded += OnErrorAdded;
            scanViewModel.ErrorsReset += OnErrorsReset;
            scanViewModel.GearChanged += OnGearChanged;
            scanViewModel.MaterialChanged += OnMaterialChanged;
            scanViewModel.MoraChanged += OnMoraChanged;
            scanViewModel.NavigationImageChanged += OnNavigationImageChanged;
            scanViewModel.CorrectionRequested += OnCorrectionRequested;

            UiTheme.RoundCorners(StartScan_Button, 8);
            UiTheme.RoundCorners(ManualExportButton, 6);
            UiTheme.RoundCorners(button1, 6);
            DarkModeMenuItem.Checked = Properties.Settings.Default.DarkMode;
            UiTheme.ApplyTheme(this);

            // Phase 3 §6c's controller-navigation debug tools (MODERNIZATION_PLAN.md §6c) are not
            // meant for end users -- only shown in Debug builds.
#if !DEBUG
            menuStrip1.Items.Remove(DebugMenuItem);
#endif
        }

        // Binds the settings-backed controls to Properties.Settings.Default in code rather than the
        // WinForms designer. The designer rewrites these bindings to a throwaway `new Settings()`
        // snapshot on every round-trip, silently disconnecting them from Settings.Default -- the
        // instance ScanSettings/InventoryKamera actually read -- so e.g. unchecking a scan box stopped
        // taking effect. Keeping the bindings here puts them out of the designer's reach. Adding a
        // Binding also pulls each control's initial value from the saved setting.
        private void BindSettings()
        {
            var settings = Properties.Settings.Default;

            void Bind(Control control, string controlProperty, string settingName) =>
                control.DataBindings.Add(new Binding(
                    controlProperty, settings, settingName, true, DataSourceUpdateMode.OnPropertyChanged));

            Bind(Weapons_CheckBox, "Checked", "ScanWeapons");
            Bind(Artifacts_Checkbox, "Checked", "ScanArtifacts");
            Bind(Characters_CheckBox, "Checked", "ScanCharacters");
            Bind(Materials_CheckBox, "Checked", "ScanMaterials");
            Bind(CharDevItems_CheckBox, "Checked", "ScanCharDevItems");

            Bind(EquipWeaponsCheckBox, "Checked", "EquipWeapons");
            Bind(EquipArtifactsCheckBox, "Checked", "EquipArtifacts");

            Bind(NumOfCharToScanControl, "Value", "NumOfCharToScan");
            Bind(SortByObtainedControl, "Value", "SortByObtained");
            Bind(numericUpDown1, "Value", "MinimumArtifactLevel");
            Bind(MinimumWeaponLevelControl, "Value", "MinimumWeaponLevel");
            Bind(ArtifactRarityControl, "Value", "MinimumArtifactRarity");
            Bind(WeaponRarityControl, "Value", "MinimumWeaponRarity");
        }

        // Renders scanViewModel's counter state into the labels MainForm owns directly -- the
        // counterpart to UserInterface's Control.Invoke-based updates, but for the one control group
        // that's been carved out into real observable state (Phase 2 §2.5, first slice).
        private void OnCountersChanged()
        {
            System.Windows.Forms.MethodInvoker render = delegate
            {
                WeaponsScannedCount_Label.Text = scanViewModel.WeaponCount.ToString();
                WeaponsMax_Labell.Text = scanViewModel.WeaponMax?.ToString() ?? "?";
                ArtifactsScanned_Label.Text = scanViewModel.ArtifactCount.ToString();
                ArtifactsMax_Label.Text = scanViewModel.ArtifactMax?.ToString() ?? "?";
                CharactersScanned_Label.Text = scanViewModel.CharacterCount.ToString();
                MaterialsScanned_Label.Text = scanViewModel.MaterialCount.ToString();
                EstimatedTimeRemaining_Label.Text = "ETA: " + FormatEta(scanViewModel.EstimatedTimeRemaining);
            };
            WeaponsScannedCount_Label.Invoke(render);
        }

        private static string FormatEta(TimeSpan? eta)
        {
            if (!eta.HasValue) return "--";
            if (eta.Value < TimeSpan.FromSeconds(1)) return "almost done";
            return eta.Value.TotalHours >= 1
                ? $"{(int)eta.Value.TotalHours}h {eta.Value.Minutes}m"
                : eta.Value.TotalMinutes >= 1
                    ? $"{(int)eta.Value.TotalMinutes}m {eta.Value.Seconds}s"
                    : $"{eta.Value.Seconds}s";
        }

        private void OnProgramStatusChanged()
        {
            System.Windows.Forms.MethodInvoker render = delegate
            {
                ProgramStatus_Label.Text = scanViewModel.ProgramStatus;
                ProgramStatus_Label.ForeColor = scanViewModel.ProgramStatusOk ? Color.Green : Color.Red;
                ProgramStatus_Label.Font = new Font(ProgramStatus_Label.Font.FontFamily, 15);
            };
            ProgramStatus_Label.Invoke(render);
        }

        private void OnErrorAdded(string error)
        {
            System.Windows.Forms.MethodInvoker render = delegate
            {
                ErrorLog_TextBox.AppendText(error.Replace("\n", Environment.NewLine) + Environment.NewLine);
            };
            ErrorLog_TextBox.Invoke(render);
        }

        // Runs on the scan thread. Invoke() blocks the caller until the delegate returns, and
        // ShowDialog() blocks until the user closes the dialog -- together that's the entire
        // pause-the-scan-thread mechanism (Phase 3 §3.3), no separate wait handle needed. If the form
        // is closing/disposed when this fires, args.ResolvedText simply stays null and
        // ScanViewModel.RequestCorrection falls back to the original OCR text.
        private void OnCorrectionRequested(OcrCorrectionEventArgs args)
        {
            try
            {
                Invoke((System.Windows.Forms.MethodInvoker)delegate
                {
                    using (var dialog = new ui.OcrCorrectionForm(args.Image, args.RecognizedText, args.ConfidencePercent, args.FieldLabel))
                    {
                        if (dialog.ShowDialog(this) == DialogResult.OK)
                        {
                            args.ResolvedText = dialog.CorrectedText;
                        }
                    }
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void OnErrorsReset()
        {
            System.Windows.Forms.MethodInvoker render = delegate { ErrorLog_TextBox.Clear(); };
            ErrorLog_TextBox.Invoke(render);
        }

        private void OnGearChanged()
        {
            // CloneGearImage() (not the GearImage property) so this control owns an independent copy
            // -- concurrent scan worker threads can dispose-and-replace scanViewModel's image at any
            // time, and a shared reference here would risk the PictureBox painting a disposed Bitmap.
            var image = scanViewModel.CloneGearImage();
            System.Windows.Forms.MethodInvoker render = delegate
            {
                var previous = GearPictureBox.Image;
                GearPictureBox.Image = image;
                previous?.Dispose();
                // ToString() separates each field with '\n', but a WinForms multiline TextBox only
                // breaks lines on '\r\n' -- convert so name/level/refinement/etc. each land on their
                // own line instead of running together (same fix the error log uses).
                ArtifactOutput_TextBox.Text = scanViewModel.GearText?.Replace("\n", Environment.NewLine);
            };
            GearPictureBox.Invoke(render);
        }

        private void OnMaterialChanged()
        {
            // Same clone-not-reference reasoning as OnGearChanged -- material images are also written
            // from scan logic, so this control needs its own independently-owned copy.
            var nameplate = scanViewModel.CloneMaterialNameplateImage();
            var quantity = scanViewModel.CloneMaterialQuantityImage();
            System.Windows.Forms.MethodInvoker render = delegate
            {
                var previousName = CharacterName_PictureBox.Image;
                CharacterName_PictureBox.Image = nameplate;
                previousName?.Dispose();

                var previousLevel = CharacterLevel_PictureBox.Image;
                CharacterLevel_PictureBox.Image = quantity;
                previousLevel?.Dispose();

                CharacterOutput_TextBox.Text = scanViewModel.MaterialText;
            };
            CharacterName_PictureBox.Invoke(render);
        }

        private void OnMoraChanged()
        {
            var image = scanViewModel.CloneMoraImage();
            System.Windows.Forms.MethodInvoker render = delegate
            {
                var previous = Navigation_Image.Image;
                Navigation_Image.Image = image;
                previous?.Dispose();

                CharacterOutput_TextBox.Text = scanViewModel.MoraText;
            };
            Navigation_Image.Invoke(render);
        }

        private void OnNavigationImageChanged()
        {
            // Shares Navigation_Image with OnMoraChanged -- the original UserInterface.SetMora and
            // SetNavigation_Image wrote into the same navigation_PictureBox too, so this preserves the
            // existing coupling rather than introducing a new one.
            var image = scanViewModel.CloneNavigationImage();
            System.Windows.Forms.MethodInvoker render = delegate
            {
                var previous = Navigation_Image.Image;
                Navigation_Image.Image = image;
                previous?.Dispose();
            };
            Navigation_Image.Invoke(render);
        }

        private double ScannerDelayValue(int value)
        {
            switch (value)
            {
                case 0:
                    return 0.5;

                case 1:
                    return 1;

                case 2:
                    return 1.5;

                default:
                    return 1;
            }
        }

        private void Hotkey_Pressed(object sender, HotkeyEventArgs e)
        {
            Logger.Info("Hotkey pressed");
            e.Handled = true;
            // Check if scanner is running
            if (scannerThread.IsAlive)
            {
                // Stop navigating weapons/artifacts. .NET no longer supports Thread.Abort, so the
                // scanner thread is asked to stop cooperatively; it checks this flag between scan
                // phases and between items within a phase (see InventoryKamera.CancelRequested).
                InventoryKamera.CancelRequested = true;

                scanViewModel.SetProgramStatus("Stopping scan...");
            }
        }

        private void ResetUI()
        {
            Navigation.Reset();

            // Need to invoke method from the UI's handle, not the worker thread
            BeginInvoke((System.Windows.Forms.MethodInvoker)delegate { RemoveHotkey(); });
            Logger.Info("Hotkey removed");
        }

        private void RemoveHotkey()
        {
            HotkeyManager.Current.Remove("Stop");
        }

        public static void UnexpectedError(string error)
        {
            if (scannerThread.IsAlive)
            {
                scanViewModel.AddError(error);
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            if (Properties.Settings.Default.UpgradeNeeded)
            {
                try
                {
                    Properties.Settings.Default.Upgrade();
                    Logger.Info("Application settings loaded from previous version");
                }
                catch (Exception) { }
                Properties.Settings.Default.UpgradeNeeded = false;
                Properties.Settings.Default.Save();
            }

            UpdateKeyTextBoxes();

            ProgramStatus_Label.Text = "";
            if (string.IsNullOrWhiteSpace(Properties.Settings.Default.OutputPath))
            {
                Properties.Settings.Default.OutputPath = Directory.GetCurrentDirectory() + @"\GenshinData";
            }

            ScanAllArtifactPages_CheckBox.Checked = SortByObtainedControl.Value == 0;
            ScanAllCharacters_CheckBox.Checked = NumOfCharToScanControl.Value == 0;
        }

        private void ScanAllArtifactPages_CheckBox_CheckedChanged(object sender, EventArgs e)
        {
            SortByObtainedControl.Enabled = !ScanAllArtifactPages_CheckBox.Checked;
            if (ScanAllArtifactPages_CheckBox.Checked)
            {
                SortByObtainedControl.Value = 0;
            }
            else if (SortByObtainedControl.Value == 0)
            {
                SortByObtainedControl.Value = 1;
            }
        }

        private void ScanAllCharacters_CheckBox_CheckedChanged(object sender, EventArgs e)
        {
            NumOfCharToScanControl.Enabled = !ScanAllCharacters_CheckBox.Checked;
            if (ScanAllCharacters_CheckBox.Checked)
            {
                NumOfCharToScanControl.Value = 0;
            }
            else if (NumOfCharToScanControl.Value == 0)
            {
                NumOfCharToScanControl.Value = 1;
            }
        }

        private void UpdateKeyTextBoxes()
        {
            Navigation.inventoryKey = (VirtualKeyCode)Properties.Settings.Default.InventoryKey;
            Navigation.characterKey = (VirtualKeyCode)Properties.Settings.Default.CharacterKey;
            Navigation.slotOneKey = (VirtualKeyCode)Properties.Settings.Default.Slot1Key;

            inventoryToolStripTextBox.Text = new KeysConverter().ConvertToString((Keys)Navigation.inventoryKey);
            characterToolStripTextBox.Text = new KeysConverter().ConvertToString((Keys)Navigation.characterKey);
            slot1StripTextBox.Text = new KeysConverter().ConvertToString((Keys)Navigation.slotOneKey);

            // Make sure text boxes show key glyph and not "OEM..."
            if (inventoryToolStripTextBox.Text.ToUpper().Contains("OEM"))
            {
                inventoryToolStripTextBox.Text = KeyCodeToUnicode((Keys)Navigation.inventoryKey);
            }
            if (characterToolStripTextBox.Text.ToUpper().Contains("OEM"))
            {
                characterToolStripTextBox.Text = KeyCodeToUnicode((Keys)Navigation.characterKey);
            }
            if (slot1StripTextBox.Text.ToUpper().Contains("OEM"))
            {
                slot1StripTextBox.Text = KeyCodeToUnicode((Keys)Navigation.slotOneKey);
            }
        }

        // Phase 3 §3.2 pre-flight validation. Runs synchronously on the UI thread, before any scan
        // state is touched (hotkey registration, "Scanning" status, etc.), so a bad setup fails
        // immediately and visibly instead of surfacing as a generic error a few seconds into a scan
        // that already looked like it started. Locates the game window as a side effect (via
        // Navigation.Initialize()) -- the scan thread re-initializes on its own right after, which is
        // redundant but harmless (the window won't move in that split second) and keeps this check
        // fully independent of the scan thread's own error handling.
        private bool PreflightChecksPass()
        {
            var settings = Properties.Settings.Default;
            int[] navigationKeys = { settings.InventoryKey, settings.CharacterKey, settings.Slot1Key };
            if (navigationKeys.Distinct().Count() != navigationKeys.Length)
            {
                MessageBox.Show(
                    "Two or more of your Inventory/Character Screen/Slot 1 keybinds are set to the " +
                    "same key. Set them to different keys under Options.",
                    "Pre-flight Check Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (navigationKeys.Contains((int)Keys.Enter))
            {
                MessageBox.Show(
                    "One of your keybinds is set to Enter, which is reserved for stopping a scan in " +
                    "progress. Choose a different key under Options.",
                    "Pre-flight Check Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            try
            {
                Navigation.Initialize();
            }
            catch (NullReferenceException)
            {
                MessageBox.Show("Genshin Impact isn't running. Please start the game and try again.",
                    "Pre-flight Check Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            catch (NotImplementedException ex)
            {
                MessageBox.Show(ex.Message, "Pre-flight Check Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            List<Size> supportedAspectRatios = new List<Size> { new Size(16, 9), new Size(8, 5) };
            if (!supportedAspectRatios.Contains(Navigation.GetAspectRatio()))
            {
                MessageBox.Show(
                    $"{Navigation.GetSize().Width}x{Navigation.GetSize().Height} is an unsupported resolution. " +
                    "Inventory Kamera supports 16:9 and 8:5 (16:10) aspect ratios.",
                    "Pre-flight Check Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            // CaptureWindow() downscales above 1080p (Navigation.CaptureScale), so compare against the
            // expected scaled size rather than the real window size directly.
            using (var capture = Navigation.CaptureWindow())
            {
                var expectedSize = new Size(
                    (int)(Navigation.GetSize().Width * Navigation.CaptureScale),
                    (int)(Navigation.GetSize().Height * Navigation.CaptureScale));
                if (capture.Size != expectedSize)
                {
                    MessageBox.Show(
                        "Window size and screenshot size mismatch. Please make sure the game is not in fullscreen mode.",
                        "Pre-flight Check Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }

            return true;
        }

        private void StartButton_Clicked(object sender, EventArgs e)
        {
            if (!PreflightChecksPass()) return;

            GC.Collect();

            scanViewModel.ResetAll();

            scanViewModel.SetProgramStatus("Scanning");
            Logger.Info("Starting scan");

            if (Directory.Exists(Properties.Settings.Default.OutputPath) || Directory.CreateDirectory(Properties.Settings.Default.OutputPath).Exists)
            {
                if (running)
                {
                    Logger.Debug("Already running");
                    return;
                }
                running = true;

                HotkeyManager.Current.AddOrReplace("Stop", Keys.Enter, Hotkey_Pressed);
                Logger.Info("Hotkey registered");
                var settings = Properties.Settings.Default;
                var gameVersion = new DatabaseManager().LocalVersion.ToString(2);
                var options =
                    $"\n\tGame Version Data:\t\t\t {gameVersion}\n" +
                    $"\tWeapons:\t\t\t\t {settings.ScanWeapons}\n" +
                    $"\tArtifacts:\t\t\t\t {settings.ScanArtifacts}\n" +
                    $"\tCharacters:\t\t\t\t {settings.ScanCharacters}\n" +
                    $"\tDev Items:\t\t\t\t {settings.ScanCharDevItems}\n" +
                    $"\tMaterials:\t\t\t\t {settings.ScanMaterials}\n" +
                    $"\tMin Weapon Rarity:\t\t {settings.MinimumWeaponRarity}\n" +
                    $"\tMin Weapon Level:\t\t {settings.MinimumWeaponLevel}\n" +
                    $"\tEquip Weapons:\t\t\t {settings.EquipWeapons}\n" +
                    $"\tMin Artifact Rarity:\t {settings.MinimumArtifactRarity}\n" +
                    $"\tMin Artifact Level:\t\t {settings.MinimumArtifactLevel}\n" +
                    $"\tEquip Artifacts:\t\t {settings.EquipArtifacts}\n" +
                    $"\tDelay:\t\t\t\t\t {settings.ScannerDelay}";

                Logger.Info("Scan settings: {0}", options);

                scannerThread = new Thread(() =>
                {
                    try
                    {
                        // Get Screen Location and Size
                        Navigation.Initialize();

                        List<Size> sizes = new List<Size>
                        {
                            new Size(16,9),
                            new Size(8,5),
                        };

                        if (!sizes.Contains(Navigation.GetAspectRatio()))
                        {
                            throw new NotImplementedException($"{Navigation.GetSize().Width}x{Navigation.GetSize().Height} is an unsupported resolution.");
                        }

                        using (var capture = Navigation.CaptureWindow())
                        {
                            var expectedSize = new Size(
                                (int)(Navigation.GetSize().Width * Navigation.CaptureScale),
                                (int)(Navigation.GetSize().Height * Navigation.CaptureScale));
                            if (capture.Size != expectedSize) throw new FormatException("Window size and screenshot size mismatch. Please make sure the game is not in a fullscreen mode.");
                        }

                        data = new InventoryKamera(scanViewModel);

                        Logger.Info("Resolution: {0}x{1}", Navigation.GetSize().Width, Navigation.GetSize().Height);

                        // Add navigation delay
                        Navigation.SetDelay(ScannerDelayValue(Properties.Settings.Default.ScannerDelay));


                        // The Data object of json object
                        data.GatherData();

                        // Tear down the global "Stop" hotkey as soon as the scan itself is done, not
                        // in the `finally` block below -- that only runs once OpenOptimizerDialog
                        // returns, which (since it now blocks on Invoke+MessageBox.Show) doesn't
                        // happen until the completion dialog is dismissed. A registered global hotkey
                        // (RegisterHotKey) intercepts Enter system-wide, for every application, not
                        // just this one -- leaving it registered for the dialog's whole lifetime meant
                        // Enter didn't work in *other* programs either while it was up. ResetUI() is
                        // still safe to call again in `finally` (e.g. on the cancelled/exception paths).
                        ResetUI();

                        if (InventoryKamera.CancelRequested)
                        {
                            // Scan was stopped cooperatively (Stop hotkey). Matches the previous
                            // Thread.Abort behaviour: skip GOOD conversion/export/optimizer dialog.
                            // The user can still use "Export Scanned Data" to export what was collected.
                            data?.StopImageProcessorWorkers();
                            scanViewModel.SetProgramStatus("Scan stopped");
                        }
                        else
                        {
                            // Covert to GOOD
                            GOOD good = new GOOD(data);
                            Logger.Info("Data converted to GOOD");

                            // Make Json File
                            good.WriteToJSON(Properties.Settings.Default.OutputPath, scanViewModel);
                            Logger.Info("Exported data");

                            scanViewModel.SetProgramStatus("Finished");
                            OpenOptimizerDialog(good);
                        }
                    }
                    catch (NotImplementedException ex)
                    {
                        scanViewModel.AddError(ex.ToString());
                    }
                    catch (Exception ex)
                    {
                        // Workers can get stuck if the thread is aborted or an exception is raised
                        data?.StopImageProcessorWorkers();
                        while (ex.InnerException != null) ex = ex.InnerException;
                        scanViewModel.AddError(ex.ToString());
                        scanViewModel.SetProgramStatus("Scan aborted", ok: false);
                    }
                    finally
                    {
                        ResetUI();
                        running = false;
                        ManualExportButton.Invoke((System.Windows.Forms.MethodInvoker)delegate
                        {
                            ManualExportButton.Enabled = data.HasData;
                        });
                        MainForm_Activate();
                    }
                })
                {
                    IsBackground = true
                };
                scannerThread.Start();
            }
            else
            {
                if (string.IsNullOrWhiteSpace(Properties.Settings.Default.OutputPath))
                    scanViewModel.AddError("Please set an output directory");
                else
                    scanViewModel.AddError($"{Properties.Settings.Default.OutputPath} is not a valid directory");
            }
        }

        private void OpenOptimizerDialog(GOOD data, bool skip = false)
        {
            // Marshaled onto the UI thread -- this is called from the background scan thread (scan
            // finishing normally) as well as directly from a button click (Export_Button_Click).
            // Previously ran unmarshaled MessageBox.Show calls with no owner window when called from
            // the scan thread: that meant the dialog had no owner to size/foreground itself against
            // (it wouldn't reliably come to the front over the game/other windows), and it was running
            // its own message loop on a thread the rest of the UI -- including the global "Stop" hotkey
            // (Enter) registered via HotkeyManager on this form's handle -- doesn't expect to be
            // competing with. Passing `this` as owner and running on the UI thread fixes both.
            Invoke((System.Windows.Forms.MethodInvoker)delegate
            {
                if (!skip)
                {
                    Activate();
                    var message = "Scan complete! Would you like to upload the database to Genshin Optimizer?";
                    var result = MessageBox.Show(this, message, "Scan Complete", MessageBoxButtons.YesNo);
                    if (result == DialogResult.No)
                        return;
                }
                var t = new Thread(() => Clipboard.SetText(data.ToString()));
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                t.Join();
                MessageBox.Show(this, "Content copied to your clipboard! Paste the content into the textbox when prompted.", "Data Copied", MessageBoxButtons.OK);
                Process.Start(new ProcessStartInfo("https://frzyc.github.io/genshin-optimizer/#/setting") { UseShellExecute = true });
            });
        }

        private void Github_Label_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://github.com/taiwenlee/Inventory_Kamera/");
        }

        private void Releases_Label_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://github.com/taiwenlee/Inventory_Kamera/releases");
        }

        private void IssuesPage_Label_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://github.com/taiwenlee/Inventory_Kamera/issues");
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveSettings();
            RemoveHotkey();
            NLog.LogManager.Shutdown();
        }

        private void SaveSettings()
        {
            Properties.Settings.Default.Save();
        }

        private void Exit_MenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void OptionsMenuItem_KeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;

            // Virtual keys for 0-9, A-Z
            bool vk = e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.Z;
            // Numpad keys and function keys (internally accepts up to F24)
            bool np = e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.F24;
            // OEM keys (Keys that vary depending on keyboard layout)
            bool oem = e.KeyCode >= Keys.Oem1 && e.KeyCode <= Keys.Oem7;
            // Arrow keys, spacebar, INS, DEL, HOME, END, PAGEUP, PAGEDOWN
            bool misc = e.KeyCode == Keys.Space || (e.KeyCode >= Keys.Left && e.KeyCode <= Keys.Down) || (e.KeyCode >= Keys.Prior && e.KeyCode <= Keys.Home) || e.KeyCode == Keys.Insert || e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back;

            // Validate that key is an acceptable Genshin keybind.
            if (!vk && !np && !oem && !misc)
            {
                Logger.Debug("Invalid {key} key pressed", e.KeyCode);
                return;
            }
            ToolStripTextBox s = (ToolStripTextBox)sender;

            // Needed to differentiate between NUMPAD numbers and numbers at top of keyboard
            s.Text = np || e.KeyCode == Keys.Back ? new KeysConverter().ConvertToString(e.KeyCode) : KeyCodeToUnicode(e.KeyData);

            // Spacebar or upper navigation keys (INSERT-PAGEDOWN keys) make textbox empty
            if (string.IsNullOrWhiteSpace(s.Text) || string.IsNullOrEmpty(s.Text))
            {
                s.Text = new KeysConverter().ConvertToString(e.KeyCode);
            }


            switch (s.Tag)
            {
                case "InventoryKey":
                    Navigation.inventoryKey = (VirtualKeyCode)e.KeyCode;
                    Logger.Debug("Inv key set to: {key}", Navigation.inventoryKey);
                    Properties.Settings.Default.InventoryKey = e.KeyValue;
                    break;

                case "CharacterKey":
                    Navigation.characterKey = (VirtualKeyCode)e.KeyCode;
                    Logger.Debug("Char key set to: {key}", Navigation.characterKey);
                    Properties.Settings.Default.CharacterKey = e.KeyValue;
                    break;

                case "slot1Key":
                    Navigation.slotOneKey = (VirtualKeyCode)e.KeyCode;
                    Logger.Debug("Slot 1 key set to: {key}", Navigation.slotOneKey);
                    Properties.Settings.Default.Slot1Key = e.KeyValue;
                    break;

                default:
                    break;
            }
        }

        private void DatabaseUpdateMenuItem_Click(object sender, EventArgs e)
        {
            var status = databaseManager.UpdateGameData();
            switch (status)
            {
                case UpdateStatus.Fail:
                    MessageBox.Show("Unable to update game data. Please check the log for more details", "Update failed", buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Stop);
                    break;
                case UpdateStatus.Success:
                    MessageBox.Show($"Update for game version {databaseManager.LocalVersion.ToString(2)} successful.", "Update status", buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Information);
                    Logger.Info("Updated game date to {0}", databaseManager.LocalVersion.ToString(2));
                    break;
                case UpdateStatus.Skipped:
                    if (MessageBox.Show($"No update necessary! You are already using the latest game data ({databaseManager.LocalVersion.ToString(2)})." +
                        $" Would you like to force an update?",
                        "Already Up to Date",
                        buttons: MessageBoxButtons.YesNo,
                        icon: MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        status = databaseManager.UpdateGameData(force: true);
                        switch (status)
                        {
                            case UpdateStatus.Fail:
                                MessageBox.Show("Unable to update game data. Please check the log for more details", "Update failed", buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Stop);
                                break;
                            default:
                                MessageBox.Show($"Update for game version {databaseManager.LocalVersion.ToString(2)} successful.", "Update success", buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Information);
                                Logger.Info("Successfully updated game data to {0}", new DatabaseManager().LocalVersion.ToString(2));
                                break;
                        }
                    }
                    break;
                default:
                    break;
            }
        }

        #region Unicode Helper Functions

        // Needed to display OEM keys as glyphs from keyboard. Should work for other languages
        // and keyboard layouts but only tested with QWERTY layout.
        private string KeyCodeToUnicode(Keys key)
        {
            byte[] keyboardState = new byte[255];
            bool keyboardStateStatus = GetKeyboardState(keyboardState);

            if (!keyboardStateStatus)
            {
                return "";
            }
            uint virtualKeyCode = (uint)key;
            uint scanCode = MapVirtualKey(virtualKeyCode, 0);
            IntPtr inputLocaleIdentifier = GetKeyboardLayout(0);

            StringBuilder result = new StringBuilder();
            ToUnicodeEx(virtualKeyCode, scanCode, keyboardState, result, 5, 0, inputLocaleIdentifier);

            return result.ToString();
        }

        [DllImport("user32.dll")]
        private static extern bool GetKeyboardState(byte[] lpKeyState);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

        #endregion Unicode Helper Functions

        private void ExportFolderMenuItem_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(Properties.Settings.Default.OutputPath) || Directory.CreateDirectory(Properties.Settings.Default.OutputPath).Exists)
            {
                Process.Start($@"{Properties.Settings.Default.OutputPath}");
            }
            else
            {
                Process.Start("explorer.exe");
            }
        }

        private void AdvancedSettingsMenuItem_Click(object sender, EventArgs e)
        {
            new ui.SettingsForm().ShowDialog(this);
        }

        private void DarkModeMenuItem_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.DarkMode = DarkModeMenuItem.Checked;
            Properties.Settings.Default.Save();
            UiTheme.ApplyTheme(this);
            // ProgramStatus_Label's color is semantic (green/red), not part of the base palette --
            // re-render it against the new background instead of leaving whatever ApplyTheme left it
            // at (which is the themed default text color, wrong for an in-progress/error status).
            OnProgramStatusChanged();
        }

        // Phase 3 §6c controller-navigation test handlers. Logic lives in
        // game/ControllerNavigationTests.cs, not here -- keeps this file (and especially
        // MainForm.Designer.cs) untouched by frequent coordinate/timing tweaks, since editing either
        // risks tripping the WinForms Designer regeneration bug (see MODERNIZATION_PLAN.md §3.0).
        private void TestControllerMashBackMenuItem_Click(object sender, EventArgs e) => game.ControllerNavigationTests.RunMashBackTest();
        private void TestControllerCharacterScanMenuItem_Click(object sender, EventArgs e) => game.ControllerNavigationTests.RunControllerCharacterScanTest(scanViewModel);
        private void CoordinatePickerMenuItem_Click(object sender, EventArgs e) => new ui.CoordinatePickerForm().Show(this);

        private void MainForm_Shown(object sender, EventArgs e)
        {
            CheckForKameraUpdates();
            CheckForGenshinUpdates();
        }

        private async void CheckForKameraUpdates()
        {
            var client = new GitHubClient(new ProductHeaderValue("Inventory_Kamera"));
            try
            {
                var releases = await client.Repository.Release.GetAll("taiwenlee", "Inventory_Kamera");
                var latest = releases.First();


                Version latestVersion = new Version(Regex.Replace(latest.TagName, "[a-zA-Z]", string.Empty));
                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                if (currentVersion.CompareTo(latestVersion) < 0)
                {
                    var message = $"A new version of Inventory Kamera is available.\n\n" +
                        $"Current Version: {currentVersion}\nLatest Version: {latestVersion}\n\n" +
                        $"Would you like to download the update?";
                    var result = MessageBox.Show(message, "Inventory Kamera Update", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo(latest.HtmlUrl) { UseShellExecute = true });
                    }
                }
            }
            catch (RateLimitExceededException) { Logger.Warn("Rate limit exceeded checking for Kamera Update!!!! This warning should be resolved in an hour."); }

        }

        private void CheckForGenshinUpdates()
        {
            var databaseManager = new DatabaseManager();
            try
            {
                var updatesAvailable = databaseManager.UpdateAvailable();
                if (updatesAvailable)
                {
                    var message = "A new version for Genshin Impact has been found. Would you like to update Kamera's lookup tables? (Recommended)";
                    var result = MessageBox.Show(message, "Game Version Update", MessageBoxButtons.YesNo);
                    if (result == DialogResult.Yes)
                    {
                        switch (databaseManager.UpdateGameData())
                        {
                            case UpdateStatus.Fail:
                                MessageBox.Show("Unable to update game data. Please check the log for more details", "Update failed", buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Stop);
                                break;
                            case UpdateStatus.Success:
                                MessageBox.Show($"Update for game version {databaseManager.LocalVersion.ToString(2)} successful.", "Update status", buttons: MessageBoxButtons.OK, icon: MessageBoxIcon.Information);
                                Logger.Info("Updated game data to {0}", databaseManager.LocalVersion.ToString(2));
                                break;
                            default:
                                break;
                        }
                    }
                    else if (result == DialogResult.No)
                    {
                        MessageBox.Show("Update skipped. Please know that skipping this update will likely result in incorrect scans.\n" +
                            "\nYou may check for updates again on restarting this application or by using the update manager found" +
                            " under 'options'", "Update declined", MessageBoxButtons.OK);
                    }
                }
                else
                    Logger.Info("Current game data is up to date with data for {0}", databaseManager.LocalVersion.ToString(2));
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not check for list updates");
                MessageBox.Show("Could not check for updates. Consider trying again in an hour or so.", "Game Version Update", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            Properties.Settings.Default.LastUpdateCheck = DateTime.Now;
        }

        private void Export_Button_Click(object sender, EventArgs e)
        {
            OpenOptimizerDialog(new GOOD(data), true);
        }

        private void MainForm_Activate()
        {
            BeginInvoke((System.Windows.Forms.MethodInvoker)delegate { Activate(); });
        }

        private void ErrorLog_Label_Click(object sender, EventArgs e)
        {
            Process.Start($@"logging");
        }

        private void updateExecutablesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new ExecutablesForm().Show();
        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void WeaponsScannedCount_Label_Click(object sender, EventArgs e)
        {

        }

        private void EquipWeaponsCheckBox_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void ArtifactsScanned_Label_Click(object sender, EventArgs e)
        {

        }

        private void ArtifactOutput_TextBox_TextChanged(object sender, EventArgs e)
        {

        }

        private void CharacterOutput_TextBox_TextChanged(object sender, EventArgs e)
        {

        }

        private void CharacterTalent1_PictureBox_Click(object sender, EventArgs e)
        {

        }

        private void CharacterLevel_PictureBox_Click(object sender, EventArgs e)
        {

        }

        private void Language_ComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void SortByObtained_Click(object sender, EventArgs e)
        {

        }

        private void label3_Click(object sender, EventArgs e)
        {

        }
    }
}