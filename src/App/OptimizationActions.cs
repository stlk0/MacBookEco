using System;
using MacBookEco.AppPolicy;

namespace MacBookEco.App
{
    public enum OperationOutcome
    {
        Succeeded,
        Unsupported,
        Cancelled,
        Busy,
        Failed,
        Indeterminate
    }

    // These values form the stable, presentation-independent result contract.
    // Message is deliberately human-readable. DiagnosticDetail is internal
    // troubleshooting context and must not be exported verbatim in a public
    // issue report because exception messages can contain machine identifiers.
    public enum OperationCode
    {
        None,
        InvalidRequest,
        UnsupportedCapability,
        UserCancelled,
        RunnerBusy,
        HelperMissing,
        HelperRejected,
        HelperUnsupported,
        HelperFailed,
        HelperTimeout,
        HelperIndeterminate,
        DisplayConfirmationTimedOut,
        DisplayReverted,
        DisplayRollbackUnverified,
        StateVerificationFailed,
        ReadBackFailed,
        CombinedProfileDisplayIncomplete,
        CombinedProfileApplied,
        UnhandledException
    }

    public enum DisplayModeConfirmationDecision
    {
        Keep,
        Revert
    }

    public sealed class DisplayModeConfirmationRequest
    {
        public DisplayModeConfirmationRequest(int refreshRateHz, TimeSpan timeout)
        {
            RefreshRateHz = refreshRateHz;
            Timeout = timeout;
        }

        public int RefreshRateHz { get; private set; }
        public TimeSpan Timeout { get; private set; }
    }

    public sealed class OptimizationStateSnapshot
    {
        public OptimizationStateSnapshot(
            bool available,
            bool cpuProfileActive,
            PowerPreset? activeCpuPreset,
            string cpuState,
            string displaySupportState,
            string displayProfileId,
            string detail,
            bool display48HzAvailable = false,
            bool displayEcoHzAvailable = false,
            bool display60HzAvailable = false)
        {
            Available = available;
            CpuProfileActive = cpuProfileActive;
            ActiveCpuPreset = activeCpuPreset;
            CpuState = cpuState ?? string.Empty;
            DisplaySupportState = displaySupportState ?? string.Empty;
            DisplayProfileId = displayProfileId ?? string.Empty;
            Detail = detail ?? string.Empty;
            Display48HzAvailable = display48HzAvailable;
            DisplayEcoHzAvailable = displayEcoHzAvailable;
            Display60HzAvailable = display60HzAvailable;
        }

        public bool Available { get; private set; }
        public bool CpuProfileActive { get; private set; }
        public PowerPreset? ActiveCpuPreset { get; private set; }
        public string CpuState { get; private set; }
        public string DisplaySupportState { get; private set; }
        public string DisplayProfileId { get; private set; }
        public string Detail { get; private set; }
        public bool Display48HzAvailable { get; private set; }
        public bool DisplayEcoHzAvailable { get; private set; }
        public bool Display60HzAvailable { get; private set; }

        public static OptimizationStateSnapshot Unavailable(string detail)
        {
            return new OptimizationStateSnapshot(
                false,
                false,
                null,
                "Unavailable",
                "Unavailable",
                string.Empty,
                detail);
        }
    }

    /// <summary>
    /// The wording of the two destructive confirmations, shared by the tray
    /// menu and the dashboard. Both offer the same two commands, and when each
    /// kept its own copy of the text the two had already drifted apart.
    /// </summary>
    public static class DestructivePrompts
    {
        public const string RemoveDisplaySupportTitle =
            "Remove Eco display support";

        public const string RemoveDisplaySupport =
            "Remove only the exact MacBook Eco-owned display override? "
            + "Foreign overrides are preserved.\r\n\r\n"
            + "Windows must be restarted afterward.";

        public const string RestorePowerPlanTitle = "Restore original power plan";

        public const string RestorePowerPlan =
            "Restore the exact Windows power plan that was active before "
            + "MacBook Eco? The app-owned plan will be retained for manual "
            + "cleanup.";
    }

    public sealed class OptimizationActionResult
    {
        private OptimizationActionResult(
            OperationOutcome outcome,
            OperationCode code,
            bool restartRequired,
            string message,
            string diagnosticDetail)
        {
            Outcome = outcome;
            Code = code;
            RestartRequired = restartRequired;
            Message = message ?? string.Empty;
            DiagnosticDetail = diagnosticDetail ?? string.Empty;
        }

        public OperationOutcome Outcome { get; private set; }
        public OperationCode Code { get; private set; }
        public bool RestartRequired { get; private set; }
        public string Message { get; private set; }
        public string DiagnosticDetail { get; private set; }

        public bool Succeeded => Outcome == OperationOutcome.Succeeded;

        public static OptimizationActionResult Successful(
            string message,
            OperationCode code,
            bool restartRequired)
        {
            return new OptimizationActionResult(
                OperationOutcome.Succeeded,
                code,
                restartRequired,
                message,
                string.Empty);
        }

        public static OptimizationActionResult Unsupported(
            OperationCode code,
            string message)
        {
            return new OptimizationActionResult(
                OperationOutcome.Unsupported,
                code,
                false,
                message,
                string.Empty);
        }

        public static OptimizationActionResult Cancelled(
            OperationCode code,
            string message)
        {
            return new OptimizationActionResult(
                OperationOutcome.Cancelled,
                code,
                false,
                message,
                string.Empty);
        }

        public static OptimizationActionResult Busy(string message)
        {
            return new OptimizationActionResult(
                OperationOutcome.Busy,
                OperationCode.RunnerBusy,
                false,
                message,
                string.Empty);
        }

        public static OptimizationActionResult Failed(
            OperationCode code,
            string message,
            string diagnosticDetail)
        {
            return new OptimizationActionResult(
                OperationOutcome.Failed,
                code,
                false,
                message,
                diagnosticDetail);
        }

        /// <summary>
        /// The unhandled-exception outcome, phrased the same way wherever an
        /// operation is wrapped. Three call sites used to keep a private copy
        /// of this, which is one way for the diagnostic detail to start
        /// differing between paths that mean the same thing.
        /// </summary>
        public static OptimizationActionResult Faulted(
            string context,
            Exception exception)
        {
            return Failed(
                OperationCode.UnhandledException,
                context + ": " + exception.Message,
                exception.Message);
        }

        public static OptimizationActionResult Indeterminate(
            OperationCode code,
            string message,
            string diagnosticDetail)
        {
            return new OptimizationActionResult(
                OperationOutcome.Indeterminate,
                code,
                false,
                message,
                diagnosticDetail);
        }

        public OptimizationActionResult WithMessage(
            string message,
            OperationCode code)
        {
            return new OptimizationActionResult(
                Outcome,
                code,
                RestartRequired,
                message,
                DiagnosticDetail);
        }

    }

    public interface IOptimizationActionService
    {
        OptimizationActionResult SetDisplayRefreshRate(
            int refreshRateHz,
            Func<DisplayModeConfirmationRequest, DisplayModeConfirmationDecision>
                confirmation);

        OptimizationActionResult InstallDisplaySupport();

        OptimizationActionResult RemoveDisplaySupport();

        OptimizationActionResult ApplyCpuPreset(PowerPreset preset);

        OptimizationActionResult RestoreCpuPower();

        OptimizationStateSnapshot ReadState();

    }

    /// <summary>
    /// Stands in when the Windows action adapters could not be constructed.
    /// It carries the reason so Diagnostics can say why the application went
    /// read-only instead of only that it did.
    /// </summary>
    public sealed class ReadOnlyOptimizationActionService : IOptimizationActionService
    {
        private const string BaseExplanation =
            "The safe read-only build is active. The Windows platform action adapter "
            + "must be present before settings can be changed.";

        private readonly string _explanation;

        public ReadOnlyOptimizationActionService()
            : this(null)
        {
        }

        public ReadOnlyOptimizationActionService(string reason)
        {
            _explanation = string.IsNullOrWhiteSpace(reason)
                ? BaseExplanation
                : BaseExplanation + " Reason: " + reason.Trim();
        }

        private string Explanation => _explanation;

        public OptimizationActionResult SetDisplayRefreshRate(
            int refreshRateHz,
            Func<DisplayModeConfirmationRequest, DisplayModeConfirmationDecision>
                confirmation)
        {
            return OptimizationActionResult.Unsupported(
                OperationCode.UnsupportedCapability,
                Explanation);
        }

        public OptimizationActionResult InstallDisplaySupport()
        {
            return OptimizationActionResult.Unsupported(
                OperationCode.UnsupportedCapability,
                Explanation);
        }

        public OptimizationActionResult RemoveDisplaySupport()
        {
            return OptimizationActionResult.Unsupported(
                OperationCode.UnsupportedCapability,
                Explanation);
        }

        public OptimizationActionResult ApplyCpuPreset(PowerPreset preset)
        {
            return OptimizationActionResult.Unsupported(
                OperationCode.UnsupportedCapability,
                Explanation);
        }

        public OptimizationActionResult RestoreCpuPower()
        {
            return OptimizationActionResult.Unsupported(
                OperationCode.UnsupportedCapability,
                Explanation);
        }

        public OptimizationStateSnapshot ReadState()
        {
            return OptimizationStateSnapshot.Unavailable(Explanation);
        }

    }
}
