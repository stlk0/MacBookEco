using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MacBookEco.AppPolicy;

namespace MacBookEco.App
{
    /// <summary>
    /// Read-only, structured explanation of the selected CPU preset. This is
    /// a preview of the policy, not an editable Windows power-plan editor.
    /// </summary>
    public sealed class CpuPresetPreview : Panel
    {
        private readonly Label _description;
        private readonly Label[] _pluggedIn;
        private readonly Label[] _battery;

        public CpuPresetPreview()
        {
            _pluggedIn = new Label[5];
            _battery = new Label[5];
            Dock = DockStyle.Fill;
            BackColor = DashboardTheme.MutedSurfaceColor;
            ForeColor = DashboardTheme.PrimaryTextColor;
            Font = DashboardTheme.BodyFont;
            Padding = new Padding(14, 10, 14, 10);
            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = "Selected CPU preset details";

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 3;
            layout.RowCount = 8;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48.0f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26.0f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26.0f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            for (int row = 0; row < 5; row++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));

            _description = DashboardTheme.CreateBodyLabel(string.Empty);
            _description.AutoSize = false;
            _description.Dock = DockStyle.Fill;
            _description.TextAlign = ContentAlignment.MiddleLeft;
            _description.MinimumSize = new Size(
                0,
                _description.Font.Height + 6);
            layout.Controls.Add(_description, 0, 0);
            layout.SetColumnSpan(_description, 3);

            AddHeader(layout, "Setting", 0);
            AddHeader(layout, "Plugged in", 1);
            AddHeader(layout, "On battery", 2);
            AddRow(layout, 2, 0, "Minimum CPU");
            AddRow(layout, 3, 1, "Maximum CPU");
            AddRow(layout, 4, 2, "Turbo / boost");
            AddRow(layout, 5, 3, "Energy preference");
            AddRow(layout, 6, 4, "Cooling policy");

            Label note = DashboardTheme.CreateCaptionLabel(
                "0 = performance, 100 = efficiency. Applied to the app-owned "
                    + "MacBook Eco plan; Restore returns the prior Windows plan.");
            note.AutoSize = false;
            note.Dock = DockStyle.Fill;
            note.TextAlign = ContentAlignment.BottomLeft;
            note.Margin = new Padding(0, 4, 0, 0);
            layout.Controls.Add(note, 0, 7);
            layout.SetColumnSpan(note, 3);
            Controls.Add(layout);
        }

        public void SetPreset(PowerPreset preset)
        {
            PowerPresetDefinition definition = PowerPresetCatalog.Get(preset);
            _description.Text = definition.ShortDescription;
            SetRow(0,
                Percent(definition.MinimumProcessorAc),
                Percent(definition.MinimumProcessorDc));
            SetRow(1,
                Percent(definition.MaximumProcessorAc),
                Percent(definition.MaximumProcessorDc));
            SetRow(2,
                PowerPresetDefinition.BoostLabel(definition.BoostModeAc),
                PowerPresetDefinition.BoostLabel(definition.BoostModeDc));
            SetRow(3,
                Preference(definition.EnergyPreferenceAc),
                Preference(definition.EnergyPreferenceDc));
            SetRow(4,
                PowerPresetDefinition.CoolingLabel(definition.CoolingPolicyAc),
                PowerPresetDefinition.CoolingLabel(definition.CoolingPolicyDc));
            AccessibleDescription = definition.DisplayName + ". "
                + definition.ShortDescription;
        }

        private void AddHeader(TableLayoutPanel layout, string text, int column)
        {
            Label label = DashboardTheme.CreateCaptionLabel(text);
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = column == 0
                ? ContentAlignment.MiddleLeft
                : ContentAlignment.MiddleRight;
            label.Font = DashboardTheme.CaptionStrongFont;
            label.Margin = Padding.Empty;
            label.MinimumSize = new Size(0, label.Font.Height + 4);
            layout.Controls.Add(label, column, 1);
        }

        private void AddRow(
            TableLayoutPanel layout,
            int row,
            int index,
            string setting)
        {
            Label label = DashboardTheme.CreateCaptionLabel(setting);
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.ForeColor = DashboardTheme.PrimaryTextColor;
            label.Margin = Padding.Empty;
            label.MinimumSize = new Size(0, label.Font.Height + 4);
            layout.Controls.Add(label, 0, row);

            _pluggedIn[index] = CreateValueLabel();
            _battery[index] = CreateValueLabel();
            layout.Controls.Add(_pluggedIn[index], 1, row);
            layout.Controls.Add(_battery[index], 2, row);
        }

        private static Label CreateValueLabel()
        {
            Label label = DashboardTheme.CreateCaptionLabel(string.Empty);
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleRight;
            label.ForeColor = DashboardTheme.PrimaryTextColor;
            label.Margin = Padding.Empty;
            label.MinimumSize = new Size(0, label.Font.Height + 4);
            return label;
        }

        private void SetRow(int index, string pluggedIn, string battery)
        {
            _pluggedIn[index].Text = pluggedIn;
            _battery[index].Text = battery;
        }

        private static string Percent(uint value)
        {
            return value.ToString(CultureInfo.InvariantCulture) + "%";
        }

        private static string Preference(uint value)
        {
            return value.ToString(CultureInfo.InvariantCulture) + " / 100";
        }
    }
}
