using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ChildSessionDesktop
{
    internal static class ChromeColors
    {
        internal static Color Canvas { get { return SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(12, 17, 28); } }
        internal static Color Surface { get { return SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(23, 31, 48); } }
        internal static Color Elevated { get { return SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(31, 43, 65); } }
        internal static Color Hover { get { return SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(51, 71, 105); } }
        internal static Color Selection { get { return SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(36, 61, 94); } }
        internal static Color Border { get { return SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(66, 85, 116); } }
        internal static Color Text { get { return SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(239, 246, 255); } }
        internal static Color SelectedText { get { return SystemInformation.HighContrast ? SystemColors.HighlightText : Text; } }
        internal static Color Muted { get { return SystemInformation.HighContrast ? SystemColors.GrayText : Color.FromArgb(163, 181, 207); } }
        internal static Color Accent { get { return SystemInformation.HighContrast ? SystemColors.ControlText : Color.FromArgb(128, 212, 253); } }

        internal static void StyleButton(Button button, bool primary = false)
        {
            if (SystemInformation.HighContrast) return;
            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = primary ? Selection : Elevated;
            button.ForeColor = Text;
            button.FlatAppearance.BorderColor = primary ? Accent : Border;
            button.FlatAppearance.MouseOverBackColor = Hover;
            button.FlatAppearance.MouseDownBackColor = Selection;
        }

        internal static void StyleField(Control field)
        {
            if (SystemInformation.HighContrast) return;
            field.BackColor = Canvas;
            field.ForeColor = Text;
        }
    }

    internal class ChromeForm : Form
    {
        private static readonly Font InterfaceFont = new Font("Segoe UI", 9f);
        private const int ImmersiveDarkMode = 20, CornerPreference = 33, CaptionColor = 35, TextColor = 36;
        private const int UseDefaultColor = -1, Rounded = 2;

        internal ChromeForm()
        {
            Font = InterfaceFont;
            BackColor = ChromeColors.Canvas;
            ForeColor = ChromeColors.Text;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyChrome();
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x001A || message.Msg == 0x031A) ApplyChrome(); // WM_SETTINGCHANGE / WM_THEMECHANGED
        }

        private void ApplyChrome()
        {
            if (!IsHandleCreated || FormBorderStyle == FormBorderStyle.None || IsDisposed) return;
            bool contrast = SystemInformation.HighContrast;
            try
            {
                if (SetAttribute(ImmersiveDarkMode, contrast ? 0 : 1) < 0)
                    SetAttribute(19, contrast ? 0 : 1); // Older Windows 10 builds.
                SetAttribute(CornerPreference, contrast ? 0 : Rounded);
                SetAttribute(CaptionColor, contrast ? UseDefaultColor : ColorTranslator.ToWin32(ChromeColors.Canvas));
                SetAttribute(TextColor, contrast ? UseDefaultColor : ColorTranslator.ToWin32(ChromeColors.Text));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        private int SetAttribute(int attribute, int value)
        {
            return DwmSetWindowAttribute(Handle, attribute, ref value, sizeof(int));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }
}
