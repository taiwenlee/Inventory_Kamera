using System.Drawing;
using System.Windows.Forms;

namespace InventoryKamera.ui
{
    /// <summary>
    /// A GroupBox that owner-draws a thin flat border and title instead of WinForms' default
    /// OS-themed notched/beveled border, to match the app's flat visual style.
    /// </summary>
    public class FlatGroupBox : GroupBox
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);

            using var titleFont = new Font(Font, FontStyle.Bold);
            var textSize = g.MeasureString(Text, titleFont);
            int top = (int)(textSize.Height / 2);
            var borderRect = new Rectangle(0, top, Width - 1, Height - top - 1);

            using (var borderPen = new Pen(UiTheme.CurrentBorderColor))
            {
                g.DrawLine(borderPen, borderRect.Left, borderRect.Top, borderRect.Left + 6, borderRect.Top);
                g.DrawLine(borderPen, borderRect.Left + 10 + (int)textSize.Width, borderRect.Top, borderRect.Right, borderRect.Top);
                g.DrawLine(borderPen, borderRect.Left, borderRect.Top, borderRect.Left, borderRect.Bottom);
                g.DrawLine(borderPen, borderRect.Right, borderRect.Top, borderRect.Right, borderRect.Bottom);
                g.DrawLine(borderPen, borderRect.Left, borderRect.Bottom, borderRect.Right, borderRect.Bottom);
            }

            using var textBrush = new SolidBrush(ForeColor);
            g.DrawString(Text, titleFont, textBrush, borderRect.Left + 8, 0);
        }
    }
}
