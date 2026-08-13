using System;
using System.Drawing;
using System.Windows.Forms;

namespace MacBookEco.App
{
    /// <summary>
    /// Shared visual tokens and small styling helpers for the dashboard.
    /// The theme intentionally uses only inbox WinForms and System.Drawing APIs.
    /// </summary>
    public static class DashboardTheme
    {
        private const float DesignDpi = 96.0f;

        public static readonly Color CanvasColor = Color.FromArgb(244, 247, 248);
        public static readonly Color SurfaceColor = Color.FromArgb(255, 255, 255);
        public static readonly Color MutedSurfaceColor = Color.FromArgb(237, 242, 244);
        public static readonly Color HoverSurfaceColor = Color.FromArgb(230, 237, 239);
        public static readonly Color BorderColor = Color.FromArgb(214, 223, 226);
        public static readonly Color StrongBorderColor = Color.FromArgb(188, 202, 207);

        public static readonly Color PrimaryTextColor = Color.FromArgb(31, 43, 48);
        public static readonly Color SecondaryTextColor = Color.FromArgb(96, 111, 118);
        public static readonly Color DisabledTextColor = Color.FromArgb(144, 156, 162);
        public static readonly Color DisabledSurfaceColor = Color.FromArgb(228, 235, 238);

        public static readonly Color AccentColor = Color.FromArgb(38, 111, 105);
        public static readonly Color AccentHoverColor = Color.FromArgb(31, 94, 89);
        public static readonly Color AccentPressedColor = Color.FromArgb(24, 76, 72);
        public static readonly Color SuccessColor = Color.FromArgb(43, 125, 91);
        public static readonly Color WarningColor = Color.FromArgb(166, 103, 25);
        public static readonly Color DangerColor = Color.FromArgb(177, 68, 68);

        // Graph card chrome. These are a shade cooler than the metric card's
        // border and surface on purpose, so a plot reads as a recessed area
        // rather than another card; they are named here rather than left as
        // literals in the renderer so the difference is a decision.
        public static readonly Color GraphBorderColor = Color.FromArgb(218, 224, 232);
        public static readonly Color GraphPlotColor = Color.FromArgb(249, 251, 253);
        public static readonly Color GraphGridColor = Color.FromArgb(227, 233, 240);
        public static readonly Color GraphMutedTextColor = Color.FromArgb(104, 114, 128);

        public const int SpaceLarge = 16;
        public const int StandardControlHeight = 36;
        public const int CardCornerRadius = 9;

        public static readonly Padding CardPadding = new Padding(SpaceLarge);

        private static readonly Font BodyFontValue =
            CreateFont(9.0f, FontStyle.Regular);
        private static readonly Font CaptionFontValue =
            CreateFont(8.25f, FontStyle.Regular);
        private static readonly Font CaptionStrongFontValue =
            CreateFont(8.25f, FontStyle.Bold);
        private static readonly Font SectionTitleFontValue =
            CreateFont(11.0f, FontStyle.Bold);
        private static readonly Font PageTitleFontValue =
            CreateFont(18.0f, FontStyle.Bold);
        private static readonly Font MetricFontValue =
            CreateFont(18.0f, FontStyle.Bold);
        private static readonly Font MonospaceFontValue =
            CreateMonospaceFont(8.75f);

        public static Font BodyFont => BodyFontValue;

        public static Font CaptionFont => CaptionFontValue;

        public static Font CaptionStrongFont => CaptionStrongFontValue;

        public static Font SectionTitleFont => SectionTitleFontValue;

        public static Font PageTitleFont => PageTitleFontValue;

        public static Font MetricFont => MetricFontValue;

        public static Font MonospaceFont => MonospaceFontValue;

        public static void StyleForm(Form form)
        {
            if (form == null)
            {
                throw new ArgumentNullException(nameof(form));
            }

            form.BackColor = CanvasColor;
            form.ForeColor = PrimaryTextColor;
            form.Font = BodyFont;
            form.AutoScaleDimensions = new SizeF(DesignDpi, DesignDpi);
            form.AutoScaleMode = AutoScaleMode.Dpi;
        }

        public static Panel CreateSurfacePanel()
        {
            Panel panel = new Panel();
            StyleSurfacePanel(panel);
            return panel;
        }

        public static void StyleSurfacePanel(Panel panel)
        {
            StylePanel(panel, SurfaceColor);
        }

        public static Label CreatePageTitle(string text)
        {
            Label label = new Label();
            label.Text = text ?? string.Empty;
            StylePageTitle(label);
            return label;
        }

        public static void StylePageTitle(Label label)
        {
            StyleLabel(label, PageTitleFont, PrimaryTextColor);
        }

        public static Label CreateSectionTitle(string text)
        {
            Label label = new Label();
            label.Text = text ?? string.Empty;
            StyleSectionTitle(label);
            return label;
        }

        public static void StyleSectionTitle(Label label)
        {
            StyleLabel(label, SectionTitleFont, PrimaryTextColor);
        }

        public static Label CreateBodyLabel(string text)
        {
            Label label = new Label();
            label.Text = text ?? string.Empty;
            StyleBodyLabel(label);
            return label;
        }

        public static void StyleBodyLabel(Label label)
        {
            StyleLabel(label, BodyFont, PrimaryTextColor);
        }

        public static Label CreateCaptionLabel(string text)
        {
            Label label = new Label();
            label.Text = text ?? string.Empty;
            StyleCaptionLabel(label);
            return label;
        }

        public static void StyleCaptionLabel(Label label)
        {
            StyleLabel(label, CaptionFont, SecondaryTextColor);
        }

        public static Button CreatePrimaryButton(
            string text,
            EventHandler clickHandler)
        {
            Button button = CreateButton(text, clickHandler);
            StylePrimaryButton(button);
            return button;
        }

        public static void StylePrimaryButton(Button button)
        {
            StyleButtonBase(button);
            button.BackColor = AccentColor;
            button.ForeColor = Color.White;
            SetButtonBorder(button, AccentPressedColor);
            button.FlatAppearance.MouseOverBackColor = AccentHoverColor;
            button.FlatAppearance.MouseDownBackColor = AccentPressedColor;
        }

        public static Button CreateSecondaryButton(
            string text,
            EventHandler clickHandler)
        {
            Button button = CreateButton(text, clickHandler);
            StyleSecondaryButton(button);
            return button;
        }

        public static void StyleSecondaryButton(Button button)
        {
            StyleButtonBase(button);
            button.BackColor = SurfaceColor;
            button.ForeColor = PrimaryTextColor;
            SetButtonBorder(button, StrongBorderColor);
            button.FlatAppearance.MouseOverBackColor = MutedSurfaceColor;
            button.FlatAppearance.MouseDownBackColor = HoverSurfaceColor;
        }

        public static Button CreateDangerOutlineButton(
            string text,
            EventHandler clickHandler)
        {
            Button button = CreateButton(text, clickHandler);
            StyleDangerOutlineButton(button);
            return button;
        }

        public static void StyleDangerOutlineButton(Button button)
        {
            StyleButtonBase(button);
            button.BackColor = SurfaceColor;
            button.ForeColor = DangerColor;
            SetButtonBorder(button, DangerColor);
            button.FlatAppearance.MouseOverBackColor =
                Color.FromArgb(252, 238, 238);
            button.FlatAppearance.MouseDownBackColor =
                Color.FromArgb(247, 224, 224);
        }

        private static Font CreateFont(float size, FontStyle style)
        {
            return new Font(
                SystemFonts.MessageBoxFont.FontFamily,
                size,
                style,
                GraphicsUnit.Point);
        }

        /// <summary>
        /// Consolas ships with Windows but can be removed. Naming a missing
        /// family silently substitutes a proportional face, which would
        /// misalign every diagnostics column, so the substitution is detected
        /// and answered with the generic monospace family instead.
        /// </summary>
        private static Font CreateMonospaceFont(float size)
        {
            Font preferred = new Font(
                "Consolas",
                size,
                FontStyle.Regular,
                GraphicsUnit.Point);
            if (string.Equals(
                preferred.Name,
                "Consolas",
                StringComparison.OrdinalIgnoreCase))
            {
                return preferred;
            }

            preferred.Dispose();
            return new Font(
                FontFamily.GenericMonospace,
                size,
                FontStyle.Regular,
                GraphicsUnit.Point);
        }

        private static void StylePanel(Panel panel, Color backColor)
        {
            if (panel == null)
            {
                throw new ArgumentNullException(nameof(panel));
            }

            panel.BackColor = backColor;
            panel.ForeColor = PrimaryTextColor;
            panel.Font = BodyFont;
            panel.Padding = CardPadding;
            panel.BorderStyle = BorderStyle.None;
        }

        private static void StyleLabel(
            Label label,
            Font font,
            Color foreColor)
        {
            if (label == null)
            {
                throw new ArgumentNullException(nameof(label));
            }

            label.AutoSize = true;
            label.BackColor = Color.Transparent;
            label.ForeColor = foreColor;
            label.Font = font;
            label.UseMnemonic = false;
            label.UseCompatibleTextRendering = false;
        }

        private static Button CreateButton(
            string text,
            EventHandler clickHandler)
        {
            Button button = new DashboardButton();
            button.Text = text ?? string.Empty;
            if (clickHandler != null)
            {
                button.Click += clickHandler;
            }

            return button;
        }

        private static void StyleButtonBase(Button button)
        {
            if (button == null)
            {
                throw new ArgumentNullException(nameof(button));
            }

            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Cursor = Cursors.Hand;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Font = BodyFont;
            button.MinimumSize = new Size(0, StandardControlHeight);
            button.Margin = new Padding(0, 3, 8, 3);
            button.Padding = new Padding(14, 4, 14, 5);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.UseCompatibleTextRendering = false;
            button.UseVisualStyleBackColor = false;
            button.AccessibleRole = AccessibleRole.PushButton;
        }

        private static void SetButtonBorder(Button button, Color color)
        {
            DashboardButton dashboardButton = button as DashboardButton;
            if (dashboardButton != null)
            {
                button.FlatAppearance.BorderSize = 0;
                dashboardButton.SetPalette(
                    button.BackColor,
                    button.ForeColor,
                    color);
                return;
            }

            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = color;
        }
    }

    internal sealed class DashboardButton : Button
    {
        private Color _enabledBackColor;
        private Color _enabledForeColor;
        private Color _enabledBorderColor;
        private bool _hasPalette;

        internal DashboardButton()
        {
            BorderColor = DashboardTheme.StrongBorderColor;
        }

        internal Color BorderColor { get; private set; }

        internal void SetPalette(Color backColor, Color foreColor, Color borderColor)
        {
            _enabledBackColor = backColor;
            _enabledForeColor = foreColor;
            _enabledBorderColor = borderColor;
            _hasPalette = true;
            ApplyVisualState();
        }

        protected override void OnEnabledChanged(EventArgs eventArgs)
        {
            base.OnEnabledChanged(eventArgs);
            ApplyVisualState();
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            using (SolidBrush border = new SolidBrush(BorderColor))
            {
                Rectangle client = ClientRectangle;
                if (client.Width <= 0 || client.Height <= 0)
                {
                    return;
                }

                // Keep the explicit outline one pixel inside the native
                // button window. Windows can repaint the outermost bottom
                // row after OnPaint; the inset outline remains fully visible
                // on all four sides.
                eventArgs.Graphics.FillRectangle(
                    border,
                    client.Left + 1,
                    client.Top + 1,
                    client.Width - 2,
                    1);
                eventArgs.Graphics.FillRectangle(
                    border,
                    client.Left + 1,
                    client.Bottom - 2,
                    client.Width - 2,
                    1);
                eventArgs.Graphics.FillRectangle(
                    border,
                    client.Left + 1,
                    client.Top + 1,
                    1,
                    client.Height - 2);
                eventArgs.Graphics.FillRectangle(
                    border,
                    client.Right - 2,
                    client.Top + 1,
                    1,
                    client.Height - 2);
            }
        }

        private void ApplyVisualState()
        {
            if (!_hasPalette)
            {
                return;
            }

            if (Enabled)
            {
                BackColor = _enabledBackColor;
                ForeColor = _enabledForeColor;
                BorderColor = _enabledBorderColor;
                Cursor = Cursors.Hand;
            }
            else
            {
                BackColor = DashboardTheme.DisabledSurfaceColor;
                ForeColor = DashboardTheme.DisabledTextColor;
                BorderColor = DashboardTheme.StrongBorderColor;
                Cursor = Cursors.Default;
            }

            Invalidate();
        }
    }
}
