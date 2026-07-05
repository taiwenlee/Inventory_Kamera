using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace InventoryKamera.ui
{
    /// <summary>
    /// Phase 3 §6c region-calibration helper: load a saved capture (e.g.
    /// <c>logging/SelectedWeaponDetailsCard.png</c>), drag a rectangle over it, and get the region
    /// back as percentage coordinates — both window-relative (<c>Navigation.GetWidth()</c> style)
    /// and image-relative (<c>card.Width</c> style) — as a copy-ready C# snippet. Replaces the
    /// guess-percentages → run test → send screenshot → adjust loop for tuning OCR crop regions.
    /// Built entirely in code rather than via the WinForms Designer surface, same as
    /// <see cref="OcrCorrectionForm"/> and for the same reason (MODERNIZATION_PLAN.md §3.0).
    /// </summary>
    internal sealed class CoordinatePickerForm : Form
    {
        private readonly DoubleBufferedPanel imagePanel;
        private readonly TextBox snippetTextBox;
        private readonly Label statusLabel;
        private readonly Label fileLabel;

        private Bitmap loadedImage;
        private string loadedPath;

        // Selection state, in image-pixel coordinates.
        private Point dragStart;
        private Rectangle selection = Rectangle.Empty;
        private bool dragging;

        public CoordinatePickerForm()
        {
            Text = "Coordinate Picker";
            StartPosition = FormStartPosition.CenterParent;
            BackColor = UiTheme.CurrentBackground;
            ForeColor = UiTheme.CurrentTextColor;
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(900, 700);
            MinimumSize = new Size(500, 400);

            var topPanel = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 6, 8, 6) };

            var openButton = MakeButton("Open…", 0);
            openButton.Click += (s, e) => OpenImageDialog();

            var reloadButton = MakeButton("Reload", 90);
            reloadButton.Click += (s, e) => ReloadImage();

            fileLabel = new Label
            {
                Text = "No image loaded — Open a capture from logging/, or drag a file onto this window.",
                AutoSize = false,
                Location = new Point(190, 6),
                Size = new Size(690, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.CurrentTextColor,
            };

            topPanel.Controls.Add(openButton);
            topPanel.Controls.Add(reloadButton);
            topPanel.Controls.Add(fileLabel);

            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 190, Padding = new Padding(8) };

            statusLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Text = "Cursor: —",
                ForeColor = UiTheme.CurrentTextColor,
            };

            snippetTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F),
                BackColor = UiTheme.CurrentSurfaceColor,
                ForeColor = UiTheme.CurrentTextColor,
                Text = "Drag a rectangle on the image to generate coordinates.",
            };

            var copyButton = new Button
            {
                Dock = DockStyle.Right,
                Width = 90,
                Text = "Copy",
                BackColor = UiTheme.Accent,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            copyButton.FlatAppearance.BorderSize = 0;
            copyButton.Click += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(snippetTextBox.Text) && selection != Rectangle.Empty)
                    Clipboard.SetText(snippetTextBox.Text);
            };

            bottomPanel.Controls.Add(snippetTextBox);
            bottomPanel.Controls.Add(copyButton);
            bottomPanel.Controls.Add(statusLabel);

            imagePanel = new DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.CurrentSurfaceColor,
                Cursor = Cursors.Cross,
            };
            imagePanel.Paint += ImagePanel_Paint;
            imagePanel.MouseDown += ImagePanel_MouseDown;
            imagePanel.MouseMove += ImagePanel_MouseMove;
            imagePanel.MouseUp += ImagePanel_MouseUp;
            imagePanel.Resize += (s, e) => imagePanel.Invalidate();

            Controls.Add(imagePanel);
            Controls.Add(bottomPanel);
            Controls.Add(topPanel);

            AllowDrop = true;
            DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            DragDrop += (s, e) =>
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0) LoadImage(files[0]);
            };

            FormClosed += (s, e) => loadedImage?.Dispose();
        }

        private Button MakeButton(string text, int x)
        {
            var button = new Button
            {
                Text = text,
                Location = new Point(8 + x, 6),
                Size = new Size(80, 28),
                BackColor = UiTheme.CurrentSurfaceColor,
                ForeColor = UiTheme.CurrentTextColor,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderColor = UiTheme.CurrentBorderColor },
            };
            return button;
        }

        private void OpenImageDialog()
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
                Title = "Open capture",
            })
            {
                // The controller-nav tests save all their diagnostic captures to ./logging -- start
                // there when it exists so tuning doesn't require navigating from scratch every time.
                string loggingDir = Path.GetFullPath("./logging");
                if (Directory.Exists(loggingDir)) dialog.InitialDirectory = loggingDir;

                if (dialog.ShowDialog(this) == DialogResult.OK) LoadImage(dialog.FileName);
            }
        }

        private void LoadImage(string path)
        {
            try
            {
                // Load via a byte copy instead of Image.FromFile -- FromFile keeps the file locked
                // for the Image's lifetime, and these captures get rewritten by every test run.
                Bitmap fresh;
                using (var stream = new MemoryStream(File.ReadAllBytes(path)))
                using (var image = Image.FromStream(stream))
                {
                    fresh = new Bitmap(image);
                }

                loadedImage?.Dispose();
                loadedImage = fresh;
                loadedPath = path;
                selection = Rectangle.Empty;
                fileLabel.Text = $"{path}  ({loadedImage.Width}x{loadedImage.Height})";
                snippetTextBox.Text = "Drag a rectangle on the image to generate coordinates.";
                imagePanel.Invalidate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not load \"{path}\": {ex.Message}", "Coordinate Picker",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ReloadImage()
        {
            if (loadedPath != null) LoadImage(loadedPath);
        }

        /// <summary>
        /// The rectangle the image is actually drawn into: scaled to fit the panel (up or down),
        /// aspect ratio preserved, centered. All mouse-to-image-pixel mapping goes through this.
        /// </summary>
        private Rectangle GetImageDisplayRect()
        {
            if (loadedImage == null || imagePanel.Width < 1 || imagePanel.Height < 1)
                return Rectangle.Empty;

            double scale = Math.Min((double)imagePanel.Width / loadedImage.Width,
                                    (double)imagePanel.Height / loadedImage.Height);
            int width = Math.Max(1, (int)(loadedImage.Width * scale));
            int height = Math.Max(1, (int)(loadedImage.Height * scale));
            return new Rectangle((imagePanel.Width - width) / 2, (imagePanel.Height - height) / 2, width, height);
        }

        private Point? PanelToImage(Point panelPoint)
        {
            var display = GetImageDisplayRect();
            if (display == Rectangle.Empty) return null;

            int x = (int)Math.Round((panelPoint.X - display.X) / (double)display.Width * loadedImage.Width);
            int y = (int)Math.Round((panelPoint.Y - display.Y) / (double)display.Height * loadedImage.Height);
            return new Point(
                Math.Max(0, Math.Min(loadedImage.Width, x)),
                Math.Max(0, Math.Min(loadedImage.Height, y)));
        }

        private Rectangle ImageToPanel(Rectangle imageRect)
        {
            var display = GetImageDisplayRect();
            double scaleX = display.Width / (double)loadedImage.Width;
            double scaleY = display.Height / (double)loadedImage.Height;
            return new Rectangle(
                display.X + (int)Math.Round(imageRect.X * scaleX),
                display.Y + (int)Math.Round(imageRect.Y * scaleY),
                Math.Max(1, (int)Math.Round(imageRect.Width * scaleX)),
                Math.Max(1, (int)Math.Round(imageRect.Height * scaleY)));
        }

        private void ImagePanel_Paint(object sender, PaintEventArgs e)
        {
            if (loadedImage == null) return;

            var display = GetImageDisplayRect();
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            e.Graphics.DrawImage(loadedImage, display);

            if (selection != Rectangle.Empty)
            {
                var panelRect = ImageToPanel(selection);
                using (var fill = new SolidBrush(Color.FromArgb(60, UiTheme.Accent)))
                using (var outline = new Pen(UiTheme.Accent, 1))
                {
                    e.Graphics.FillRectangle(fill, panelRect);
                    e.Graphics.DrawRectangle(outline, panelRect);
                }
            }
        }

        private void ImagePanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var imagePoint = PanelToImage(e.Location);
            if (imagePoint == null) return;

            dragging = true;
            dragStart = imagePoint.Value;
            selection = new Rectangle(dragStart, Size.Empty);
            imagePanel.Invalidate();
        }

        private void ImagePanel_MouseMove(object sender, MouseEventArgs e)
        {
            var imagePoint = PanelToImage(e.Location);
            if (imagePoint == null) return;

            var p = imagePoint.Value;
            if (loadedImage != null)
            {
                statusLabel.Text = string.Format(CultureInfo.InvariantCulture,
                    "Cursor: x={0} y={1}  ({2:0.0000}, {3:0.0000})",
                    p.X, p.Y, p.X / (double)loadedImage.Width, p.Y / (double)loadedImage.Height);
            }

            if (dragging)
            {
                selection = Rectangle.FromLTRB(
                    Math.Min(dragStart.X, p.X), Math.Min(dragStart.Y, p.Y),
                    Math.Max(dragStart.X, p.X), Math.Max(dragStart.Y, p.Y));
                imagePanel.Invalidate();
            }
        }

        private void ImagePanel_MouseUp(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false;

            if (selection.Width < 1 || selection.Height < 1)
            {
                selection = Rectangle.Empty;
                imagePanel.Invalidate();
                return;
            }

            snippetTextBox.Text = BuildSnippet(selection);
            imagePanel.Invalidate();
        }

        private string BuildSnippet(Rectangle r)
        {
            double w = loadedImage.Width, h = loadedImage.Height;
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();

            sb.AppendLine(string.Format(inv, "Pixels: x={0} y={1} width={2} height={3}  (image {4}x{5})",
                r.X, r.Y, r.Width, r.Height, loadedImage.Width, loadedImage.Height));
            sb.AppendLine();
            sb.AppendLine("// Window-relative (full-window capture loaded):");
            sb.AppendLine(string.Format(inv, "x: (int)({0:0.0000} * Navigation.GetWidth()),", r.X / w));
            sb.AppendLine(string.Format(inv, "y: (int)({0:0.0000} * Navigation.GetHeight()),", r.Y / h));
            sb.AppendLine(string.Format(inv, "width: (int)({0:0.0000} * Navigation.GetWidth()),", r.Width / w));
            sb.AppendLine(string.Format(inv, "height: (int)({0:0.0000} * Navigation.GetHeight())", r.Height / h));
            sb.AppendLine();
            sb.AppendLine("// Image-relative (card/region capture loaded):");
            sb.AppendLine(string.Format(inv, "x: (int)(card.Width * {0:0.0000}),", r.X / w));
            sb.AppendLine(string.Format(inv, "y: (int)(card.Height * {0:0.0000}),", r.Y / h));
            sb.AppendLine(string.Format(inv, "width: (int)(card.Width * {0:0.0000}),", r.Width / w));
            sb.Append(string.Format(inv, "height: (int)(card.Height * {0:0.0000})", r.Height / h));

            return sb.ToString();
        }

        // Plain Panel repaints flicker badly during drag-selection without double buffering, and
        // Panel.DoubleBuffered is protected -- the standard workaround is this trivial subclass.
        private sealed class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel() { DoubleBuffered = true; }
        }
    }
}
