using System;
using System.Drawing;
using System.Windows.Forms;
using MacBookEco.AppPolicy;

namespace MacBookEco.App
{
    internal sealed class DisplayModeChoice
    {
        internal DisplayModeChoice(int refreshRateHz, string displayName)
        {
            RefreshRateHz = refreshRateHz;
            DisplayName = displayName ?? string.Empty;
        }

        internal int RefreshRateHz { get; private set; }

        internal string DisplayName { get; private set; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    // Owns the Profiles & controls view tree. The dashboard shell retains the
    // selected-profile state and passes its use-case callbacks explicitly.
    public sealed class DashboardProfilesPage
    {
        private readonly object _customProfileItem;
        private readonly EventHandler _recommendedProfileChanged;
        private readonly EventHandler _cpuPresetChanged;
        private readonly EventHandler _displayModeChanged;
        private readonly Action<int> _applyDisplayMode;
        private readonly Action _installDisplaySupport;
        private readonly Action _removeDisplaySupport;
        private readonly Action _applyCpuPreset;
        private readonly Action _restoreOriginalPower;
        private readonly Action _applyRecommendedProfile;

        public DashboardProfilesPage(
            object customProfileItem,
            EventHandler recommendedProfileChanged,
            EventHandler cpuPresetChanged,
            EventHandler displayModeChanged,
            Action<int> applyDisplayMode,
            Action installDisplaySupport,
            Action removeDisplaySupport,
            Action applyCpuPreset,
            Action restoreOriginalPower,
            Action applyRecommendedProfile)
        {
            if (customProfileItem == null)
            {
                throw new ArgumentNullException(nameof(customProfileItem));
            }

            _customProfileItem = customProfileItem;
            _recommendedProfileChanged = RequireCallback(
                recommendedProfileChanged,
                nameof(recommendedProfileChanged));
            _cpuPresetChanged = RequireCallback(cpuPresetChanged, nameof(cpuPresetChanged));
            _displayModeChanged = RequireCallback(
                displayModeChanged,
                nameof(displayModeChanged));
            _applyDisplayMode = RequireCallback(
                applyDisplayMode,
                nameof(applyDisplayMode));
            _installDisplaySupport = RequireCallback(
                installDisplaySupport,
                nameof(installDisplaySupport));
            _removeDisplaySupport = RequireCallback(
                removeDisplaySupport,
                nameof(removeDisplaySupport));
            _applyCpuPreset = RequireCallback(applyCpuPreset, nameof(applyCpuPreset));
            _restoreOriginalPower = RequireCallback(
                restoreOriginalPower,
                nameof(restoreOriginalPower));
            _applyRecommendedProfile = RequireCallback(
                applyRecommendedProfile,
                nameof(applyRecommendedProfile));

            View = Build();
        }

        public Control View { get; private set; }
        public ComboBox RecommendedProfile { get; private set; }
        public Label RecommendedCurrent { get; private set; }
        public Label RecommendedDescription { get; private set; }
        public Button ApplyRecommendedButton { get; private set; }
        public ComboBox CpuPreset { get; private set; }
        public CpuPresetPreview CpuDetails { get; private set; }
        public Label CpuState { get; private set; }
        public Label DisplayState { get; private set; }
        public Label DisplayCurrent { get; private set; }
        public ComboBox DisplayMode { get; private set; }
        public Button DisplayApplyButton { get; private set; }
        public Button InstallDisplayButton { get; private set; }
        public Button RemoveDisplayButton { get; private set; }
        public Button CpuApplyButton { get; private set; }
        public Button CpuRestoreButton { get; private set; }

        private Control Build()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.BackColor = DashboardTheme.CanvasColor;
            root.AutoScroll = true;
            root.ColumnCount = 2;
            root.RowCount = 2;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46.0f));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54.0f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));

            root.Controls.Add(BuildRecommendedProfiles(), 0, 0);
            root.Controls.Add(BuildDisplayControls(), 1, 0);
            Control cpu = BuildCpuControls();
            root.Controls.Add(cpu, 0, 1);
            root.SetColumnSpan(cpu, 2);
            return root;
        }

        private Control BuildRecommendedProfiles()
        {
            Panel surface = CreateSurface(new Padding(18));
            surface.Margin = new Padding(4, 4, 8, 8);
            surface.AutoSize = true;
            surface.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Top;
            layout.AutoSize = true;
            layout.ColumnCount = 1;
            layout.RowCount = 8;
            layout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100.0f));
            for (int row = 0; row < layout.RowCount; row++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            layout.Controls.Add(
                DashboardTheme.CreateSectionTitle("Combined profiles"), 0, 0);
            layout.Controls.Add(CreateWrappingCaption(
                "One click applies a transparent display + CPU combination."), 0, 1);

            RecommendedCurrent = DashboardTheme.CreateCaptionLabel(
                "Current combined state: detecting...");
            RecommendedCurrent.Font = DashboardTheme.CaptionStrongFont;
            RecommendedCurrent.ForeColor = DashboardTheme.AccentColor;
            layout.Controls.Add(RecommendedCurrent, 0, 2);

            Label chooseProfile = DashboardTheme.CreateCaptionLabel("Profile to apply");
            chooseProfile.Font = DashboardTheme.CaptionStrongFont;
            layout.Controls.Add(chooseProfile, 0, 3);

            RecommendedProfile = CreateComboBox();
            RecommendedProfile.Items.Add(_customProfileItem);
            foreach (OptimizationProfileDefinition profile
                in OptimizationProfileCatalog.Profiles)
            {
                RecommendedProfile.Items.Add(profile);
            }

            RecommendedProfile.Format += delegate(
                object sender,
                ListControlConvertEventArgs eventArgs)
            {
                if (ReferenceEquals(eventArgs.ListItem, _customProfileItem))
                {
                    eventArgs.Value = "Custom \u2014 current settings";
                    return;
                }

                OptimizationProfileDefinition profile =
                    eventArgs.ListItem as OptimizationProfileDefinition;
                if (profile != null)
                {
                    eventArgs.Value = profile.DisplayName
                        + "  \u2014  "
                        + profile.DisplayRefreshRate
                        + " Hz + "
                        + PowerPresetCatalog.Get(profile.CpuPreset).DisplayName;
                }
            };
            layout.Controls.Add(RecommendedProfile, 0, 4);

            RecommendedDescription = CreateWrappingCaption(string.Empty);
            RecommendedDescription.ForeColor = DashboardTheme.PrimaryTextColor;
            RecommendedDescription.Text =
                "The current display and CPU settings do not exactly match "
                + "a named profile. Choose one below to replace both.";
            layout.Controls.Add(RecommendedDescription, 0, 5);

            RecommendedProfile.SelectedItem = _customProfileItem;
            RecommendedProfile.SelectedIndexChanged += _recommendedProfileChanged;

            ApplyRecommendedButton = DashboardTheme.CreatePrimaryButton(
                "Apply display + CPU profile",
                delegate { _applyRecommendedProfile(); });
            ApplyRecommendedButton.Anchor = AnchorStyles.Left;
            ApplyRecommendedButton.Enabled = false;
            layout.Controls.Add(ApplyRecommendedButton, 0, 6);
            layout.Controls.Add(DashboardTheme.CreateCaptionLabel(
                "Display confirmation and UAC remain separate safety steps."), 0, 7);
            surface.Controls.Add(layout);
            return surface;
        }

        private Control BuildDisplayControls()
        {
            Panel surface = CreateSurface(new Padding(18));
            surface.Margin = new Padding(8, 4, 4, 8);
            surface.AutoSize = true;
            surface.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Top;
            layout.AutoSize = true;
            layout.ColumnCount = 1;
            layout.RowCount = 6;
            layout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100.0f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1.0f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(DashboardTheme.CreateSectionTitle("Display"), 0, 0);
            DisplayCurrent = DashboardTheme.CreateBodyLabel(
                "Current mode: waiting for telemetry");
            layout.Controls.Add(DisplayCurrent, 0, 1);

            FlowLayoutPanel modes = CreateButtonRow();
            DisplayMode = CreateComboBox();
            DisplayMode.Width = 230;
            DisplayMode.SelectedIndexChanged += _displayModeChanged;
            modes.Controls.Add(DisplayMode);
            DisplayApplyButton = DashboardTheme.CreateSecondaryButton(
                "Apply selected mode",
                delegate
                {
                    DisplayModeChoice selected =
                        DisplayMode.SelectedItem as DisplayModeChoice;
                    if (selected != null)
                    {
                        _applyDisplayMode(selected.RefreshRateHz);
                    }
                });
            ConfigureDisplayModeButton(DisplayApplyButton);
            DisplayApplyButton.Enabled = false;
            modes.Controls.Add(DisplayApplyButton);
            layout.Controls.Add(modes, 0, 2);

            layout.Controls.Add(CreateWrappingCaption(
                "48 Hz is the Apple-supported compatibility mode. 58 Hz uses "
                + "the native pixel clock with a longer V-blank to allow lower "
                + "idle GPU memory clocks; unverified profiles are marked experimental."),
                0,
                3);

            Panel separator = new Panel();
            separator.Dock = DockStyle.Fill;
            separator.BackColor = DashboardTheme.BorderColor;
            layout.Controls.Add(separator, 0, 4);

            TableLayoutPanel support = new TableLayoutPanel();
            support.Dock = DockStyle.Fill;
            support.Padding = new Padding(0, 8, 0, 0);
            support.ColumnCount = 2;
            support.RowCount = 2;
            support.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
            support.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            support.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            support.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label supportTitle = DashboardTheme.CreateCaptionLabel(
                "Eco display modes setup");
            supportTitle.Font = DashboardTheme.CaptionStrongFont;
            support.Controls.Add(supportTitle, 0, 0);
            DisplayState = DashboardTheme.CreateCaptionLabel(
                "MacBook Eco ownership: checking...");
            support.Controls.Add(DisplayState, 0, 1);

            FlowLayoutPanel supportActions = CreateButtonRow();
            supportActions.Anchor = AnchorStyles.Right;
            supportActions.AutoSize = true;
            InstallDisplayButton = DashboardTheme.CreateSecondaryButton(
                "Install 48 + 58 Hz support",
                delegate { _installDisplaySupport(); });
            InstallDisplayButton.Enabled = false;
            supportActions.Controls.Add(InstallDisplayButton);
            RemoveDisplayButton = DashboardTheme.CreateDangerOutlineButton(
                "Remove Eco display support",
                delegate { _removeDisplaySupport(); });
            RemoveDisplayButton.Visible = false;
            RemoveDisplayButton.Enabled = false;
            supportActions.Controls.Add(RemoveDisplayButton);
            support.Controls.Add(supportActions, 1, 0);
            support.SetRowSpan(supportActions, 2);

            layout.Controls.Add(support, 0, 5);
            surface.Controls.Add(layout);
            return surface;
        }

        private Control BuildCpuControls()
        {
            Panel surface = CreateSurface(new Padding(18));
            surface.Margin = new Padding(4, 8, 4, 4);

            TableLayoutPanel outer = new TableLayoutPanel();
            outer.Dock = DockStyle.Fill;
            outer.ColumnCount = 1;
            outer.RowCount = 3;
            outer.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100.0f));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));
            outer.Controls.Add(DashboardTheme.CreateSectionTitle("CPU power plan"), 0, 0);
            CpuState = DashboardTheme.CreateCaptionLabel("Current plan: checking...");
            outer.Controls.Add(CpuState, 0, 1);

            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.ColumnCount = 2;
            content.RowCount = 1;
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 285.0f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));

            TableLayoutPanel choices = new TableLayoutPanel();
            choices.Dock = DockStyle.Fill;
            choices.AutoSize = true;
            choices.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            choices.Padding = new Padding(0, 6, 16, 0);
            choices.ColumnCount = 1;
            choices.RowCount = 6;
            choices.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100.0f));
            choices.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            choices.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            choices.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            choices.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            choices.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            choices.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));

            Label chooseCpu = DashboardTheme.CreateCaptionLabel("CPU preset to apply");
            chooseCpu.Font = DashboardTheme.CaptionStrongFont;
            choices.Controls.Add(chooseCpu, 0, 0);

            CpuPreset = CreateComboBox();
            CpuPreset.Items.Add(PowerPreset.Normal);
            CpuPreset.Items.Add(PowerPreset.Cool);
            CpuPreset.Items.Add(PowerPreset.MaximumBattery);
            CpuPreset.Format += delegate(
                object sender,
                ListControlConvertEventArgs eventArgs)
            {
                if (eventArgs.ListItem is PowerPreset)
                {
                    eventArgs.Value = PowerPresetCatalog.Get(
                        (PowerPreset)eventArgs.ListItem).DisplayName;
                }
            };
            choices.Controls.Add(CpuPreset, 0, 1);

            CpuApplyButton = DashboardTheme.CreatePrimaryButton(
                "Apply selected CPU preset", delegate { _applyCpuPreset(); });
            CpuApplyButton.Anchor = AnchorStyles.Left;
            choices.Controls.Add(CpuApplyButton, 0, 2);

            CpuRestoreButton = DashboardTheme.CreateSecondaryButton(
                "Restore original Windows power plan",
                delegate { _restoreOriginalPower(); });
            CpuRestoreButton.Anchor = AnchorStyles.Left;
            choices.Controls.Add(CpuRestoreButton, 0, 4);
            choices.Controls.Add(CreateWrappingCaption(
                "Restore is different from Everyday: it selects the exact "
                + "plan that was active before MacBook Eco."), 0, 5);
            content.Controls.Add(choices, 0, 0);

            CpuDetails = new CpuPresetPreview();
            content.Controls.Add(CpuDetails, 1, 0);
            CpuPreset.SelectedIndex = 0;
            CpuDetails.SetPreset(PowerPreset.Normal);
            CpuPreset.SelectedIndexChanged += _cpuPresetChanged;
            outer.Controls.Add(content, 0, 2);
            surface.Controls.Add(outer);
            return surface;
        }

        private static TCallback RequireCallback<TCallback>(
            TCallback callback,
            string name)
            where TCallback : class
        {
            if (callback == null)
            {
                throw new ArgumentNullException(name);
            }

            return callback;
        }

        private static Panel CreateSurface(Padding padding)
        {
            Panel panel = DashboardTheme.CreateSurfacePanel();
            panel.Dock = DockStyle.Fill;
            panel.Padding = padding;
            return panel;
        }

        private static FlowLayoutPanel CreateButtonRow()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.AutoSize = true;
            panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panel.Anchor = AnchorStyles.Left;
            panel.FlowDirection = FlowDirection.LeftToRight;
            panel.WrapContents = false;
            panel.Padding = new Padding(0, 2, 0, 0);
            return panel;
        }

        private static void ConfigureDisplayModeButton(Button button)
        {
            button.AutoSize = true;
            button.MinimumSize = new Size(
                170,
                Math.Max(
                    DashboardTheme.StandardControlHeight,
                    button.Font.Height + 12));
            button.Margin = new Padding(0, 3, 10, 3);
        }

        private static ComboBox CreateComboBox()
        {
            ComboBox combo = new ComboBox();
            combo.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.FlatStyle = FlatStyle.Standard;
            combo.Font = DashboardTheme.BodyFont;
            combo.FormattingEnabled = true;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = Math.Max(28, combo.Font.Height + 8);
            combo.MaxDropDownItems = 8;
            combo.BackColor = Color.White;
            combo.ForeColor = DashboardTheme.PrimaryTextColor;
            combo.Margin = new Padding(0, 2, 0, 2);
            combo.DrawItem += DrawComboBoxItem;
            return combo;
        }

        private static void DrawComboBoxItem(
            object sender,
            DrawItemEventArgs eventArgs)
        {
            ComboBox combo = sender as ComboBox;
            if (combo == null)
            {
                return;
            }

            eventArgs.DrawBackground();
            if (eventArgs.Index >= 0 && eventArgs.Index < combo.Items.Count)
            {
                string text = combo.GetItemText(combo.Items[eventArgs.Index]);
                Color color = (eventArgs.State & DrawItemState.Selected) != 0
                    ? SystemColors.HighlightText
                    : DashboardTheme.PrimaryTextColor;
                TextRenderer.DrawText(
                    eventArgs.Graphics,
                    text,
                    combo.Font,
                    eventArgs.Bounds,
                    color,
                    TextFormatFlags.Left
                        | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.NoPrefix
                        | TextFormatFlags.EndEllipsis);
            }

            eventArgs.DrawFocusRectangle();
        }

        private static Label CreateWrappingCaption(string text)
        {
            Label label = DashboardTheme.CreateCaptionLabel(text);
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.AutoEllipsis = true;
            label.TextAlign = ContentAlignment.TopLeft;
            label.MinimumSize = new Size(0, (label.Font.Height * 2) + 4);
            return label;
        }
    }
}
