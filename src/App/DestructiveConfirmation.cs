using System.Windows.Forms;

namespace MacBookEco.App
{
    /// <summary>
    /// The two destructive confirmations, asked the same way from wherever the
    /// user starts them.
    ///
    /// DestructivePrompts already shared the wording after the tray and the
    /// dashboard drifted apart. The icon and the default button had not been
    /// shared, and had drifted in exactly the same way: the tray defaulted the
    /// power restore to No while the dashboard defaulted it to Yes. Those are
    /// part of the question, not of the surface asking it, so they live here
    /// with the text rather than at each call site.
    /// </summary>
    internal static class DestructiveConfirmation
    {
        internal static bool RemoveDisplaySupport(IWin32Window owner)
        {
            // Removing the override needs a restart to take effect and cannot
            // be undone in place, so the safe answer is the default.
            return Ask(
                owner,
                DestructivePrompts.RemoveDisplaySupport,
                DestructivePrompts.RemoveDisplaySupportTitle,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
        }

        internal static bool RestorePowerPlan(IWin32Window owner)
        {
            // Restoring the original plan is the recovery action rather than
            // the risky one: the app-owned plan is retained, not deleted.
            return Ask(
                owner,
                DestructivePrompts.RestorePowerPlan,
                DestructivePrompts.RestorePowerPlanTitle,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);
        }

        private static bool Ask(
            IWin32Window owner,
            string message,
            string title,
            MessageBoxIcon icon,
            MessageBoxDefaultButton defaultButton)
        {
            return MessageBox.Show(
                owner,
                message,
                title,
                MessageBoxButtons.YesNo,
                icon,
                defaultButton) == DialogResult.Yes;
        }
    }
}
