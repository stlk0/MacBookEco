using System;
using System.Threading;
using System.Threading.Tasks;
using MacBookEco.AppPolicy;

namespace MacBookEco.App
{
    public enum OptimizationCommandKind
    {
        SetDisplayRefreshRate,
        InstallDisplaySupport,
        RemoveDisplaySupport,
        ApplyCpuPreset,
        RestoreCpuPower,
        ApplyCombinedProfile
    }

    public sealed class OptimizationCommand
    {
        private OptimizationCommand(
            OptimizationCommandKind kind,
            int refreshRateHz,
            PowerPreset? cpuPreset,
            bool includesDisplayStep,
            string displayName)
        {
            Kind = kind;
            RefreshRateHz = refreshRateHz;
            CpuPreset = cpuPreset;
            IncludesDisplayStep = includesDisplayStep;
            DisplayName = displayName ?? string.Empty;
        }

        public OptimizationCommandKind Kind { get; private set; }
        public int RefreshRateHz { get; private set; }
        public PowerPreset? CpuPreset { get; private set; }
        public bool IncludesDisplayStep { get; private set; }
        public string DisplayName { get; private set; }

        public static OptimizationCommand SetDisplayRefreshRate(int refreshRateHz)
        {
            return new OptimizationCommand(
                OptimizationCommandKind.SetDisplayRefreshRate,
                refreshRateHz,
                null,
                true,
                string.Empty);
        }

        public static OptimizationCommand InstallDisplaySupport()
        {
            return new OptimizationCommand(
                OptimizationCommandKind.InstallDisplaySupport,
                0,
                null,
                false,
                string.Empty);
        }

        public static OptimizationCommand RemoveDisplaySupport()
        {
            return new OptimizationCommand(
                OptimizationCommandKind.RemoveDisplaySupport,
                0,
                null,
                false,
                string.Empty);
        }

        public static OptimizationCommand ApplyCpuPreset(PowerPreset preset)
        {
            return new OptimizationCommand(
                OptimizationCommandKind.ApplyCpuPreset,
                0,
                preset,
                false,
                string.Empty);
        }

        public static OptimizationCommand RestoreCpuPower()
        {
            return new OptimizationCommand(
                OptimizationCommandKind.RestoreCpuPower,
                0,
                null,
                false,
                string.Empty);
        }

        public static OptimizationCommand ApplyCombinedProfile(
            int refreshRateHz,
            PowerPreset preset,
            bool includesDisplayStep,
            string displayName)
        {
            return new OptimizationCommand(
                OptimizationCommandKind.ApplyCombinedProfile,
                refreshRateHz,
                preset,
                includesDisplayStep,
                displayName);
        }
    }

    public sealed class OptimizationCommandCompletedEventArgs : EventArgs
    {
        public OptimizationCommandCompletedEventArgs(
            OptimizationCommand command,
            OptimizationActionResult result,
            OptimizationStateSnapshot state)
        {
            Command = command;
            Result = result;
            State = state;
        }

        public OptimizationCommand Command { get; private set; }
        public OptimizationActionResult Result { get; private set; }
        public OptimizationStateSnapshot State { get; private set; }
    }

    public sealed class OptimizationCommandRunnerStateChangedEventArgs : EventArgs
    {
        public OptimizationCommandRunnerStateChangedEventArgs(
            bool isBusy,
            string busyReason)
        {
            IsBusy = isBusy;
            BusyReason = busyReason ?? string.Empty;
        }

        public bool IsBusy { get; private set; }
        public string BusyReason { get; private set; }
    }

    // A single instance is shared by dashboard and tray.  It is intentionally
    // the only place that may schedule a mutation, including a combined
    // profile.  A timeout does not kill a helper: it makes state indeterminate
    // and keeps the gate until the still-running command supplies read-back.
    public sealed class OptimizationCommandRunner : IDisposable
    {
        public static readonly TimeSpan DefaultCommandTimeout =
            TimeSpan.FromSeconds(135);

        private readonly IOptimizationActionService _actions;
        private readonly SynchronizationContext _uiContext;
        private readonly TimeSpan _commandTimeout;
        private readonly object _gate = new object();
        private Func<DisplayModeConfirmationRequest, DisplayModeConfirmationDecision>
            _displayConfirmation;
        private bool _operationRunning;
        private bool _reconciliationRequired;
        private bool _disposed;

        public OptimizationCommandRunner(
            IOptimizationActionService actions,
            SynchronizationContext uiContext)
            : this(actions, uiContext, DefaultCommandTimeout)
        {
        }

        public OptimizationCommandRunner(
            IOptimizationActionService actions,
            SynchronizationContext uiContext,
            TimeSpan commandTimeout)
        {
            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions));
            }

            if (commandTimeout <= TimeSpan.Zero || commandTimeout > DefaultCommandTimeout)
            {
                throw new ArgumentOutOfRangeException(nameof(commandTimeout));
            }

            _actions = actions;
            _uiContext = uiContext;
            _commandTimeout = commandTimeout;
        }

        /// <summary>
        /// Reports the outcome of a command.
        ///
        /// One <see cref="Execute"/> can raise this twice, and that is by
        /// design: when a command reaches the safety timeout the helper is
        /// never killed, so the runner publishes an Indeterminate result to
        /// release the UI and publishes the real result again if the helper
        /// later finishes. A subscriber must therefore treat a completion as
        /// the latest word on a command rather than as a one-shot signal.
        /// </summary>
        public event EventHandler<OptimizationCommandCompletedEventArgs> Completed;
        public event EventHandler<OptimizationCommandRunnerStateChangedEventArgs>
            StateChanged;

        public bool IsBusy
        {
            get
            {
                lock (_gate)
                {
                    return _operationRunning || _reconciliationRequired;
                }
            }
        }

        public string BusyReason
        {
            get
            {
                lock (_gate)
                {
                    return CurrentBusyReasonLocked();
                }
            }
        }

        public void SetDisplayConfirmationHandler(
            Func<DisplayModeConfirmationRequest, DisplayModeConfirmationDecision>
                confirmation)
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                _displayConfirmation = confirmation;
            }
        }

        public void Execute(OptimizationCommand command)
        {
            if (command == null)
            {
                PublishCompleted(new OptimizationCommandCompletedEventArgs(
                    null,
                    OptimizationActionResult.Failed(
                        OperationCode.InvalidRequest,
                        "No optimization command was supplied.",
                        string.Empty),
                    null));
                return;
            }

            bool rejectedAsBusy = false;
            string busyReason = string.Empty;
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_operationRunning || _reconciliationRequired)
                {
                    rejectedAsBusy = true;
                    busyReason = CurrentBusyReasonLocked();
                }
                else
                {
                    _operationRunning = true;
                }
            }

            if (rejectedAsBusy)
            {
                PublishCompleted(new OptimizationCommandCompletedEventArgs(
                    command,
                    OptimizationActionResult.Busy(busyReason),
                    null));
                return;
            }

            PublishStateChanged();
            Task<OptimizationCommandCompletedEventArgs> operation =
                Task.Factory.StartNew(
                    delegate { return ExecuteAndReadState(command); });
            Task.Factory.StartNew(
                delegate { Observe(command, operation); });
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _displayConfirmation = null;
            }
        }

        private void Observe(
            OptimizationCommand command,
            Task<OptimizationCommandCompletedEventArgs> operation)
        {
            if (!operation.Wait(_commandTimeout))
            {
                bool disposed;
                lock (_gate)
                {
                    disposed = _disposed;
                    if (!disposed)
                    {
                        _reconciliationRequired = true;
                    }
                }

                if (disposed)
                {
                    // Still observe the task. Abandoning it here would leave a
                    // faulted task unobserved, which escalates on the finalizer
                    // thread on the .NET Framework configurations that keep the
                    // legacy unobserved-exception policy.
                    ObserveQuietly(operation);
                    return;
                }

                PublishStateChanged();
                PublishCompleted(new OptimizationCommandCompletedEventArgs(
                    command,
                    OptimizationActionResult.Indeterminate(
                        OperationCode.HelperTimeout,
                        "The command reached its safety timeout. The helper was not "
                            + "terminated; recovery is waiting for read-back.",
                        "runner-timeout=" + _commandTimeout.TotalSeconds),
                    null));

                try
                {
                    OptimizationCommandCompletedEventArgs lateResult = operation.Result;
                    Complete(lateResult);
                }
                catch (Exception exception)
                {
                    Complete(new OptimizationCommandCompletedEventArgs(
                        command,
                        OptimizationActionResult.Indeterminate(
                            OperationCode.HelperIndeterminate,
                            "The timed-out command ended without a usable result. "
                                + "Recovery remains required.",
                            exception.Message),
                        null));
                }

                return;
            }

            try
            {
                Complete(operation.Result);
            }
            catch (Exception exception)
            {
                Complete(new OptimizationCommandCompletedEventArgs(
                    command,
                    OptimizationActionResult.Failed(
                        OperationCode.UnhandledException,
                        "The command runner could not collect the operation result.",
                        exception.Message),
                    null));
            }
        }

        private static void ObserveQuietly(
            Task<OptimizationCommandCompletedEventArgs> operation)
        {
            operation.ContinueWith(
                delegate(Task<OptimizationCommandCompletedEventArgs> completed)
                {
                    AggregateException ignored = completed.Exception;
                },
                TaskContinuationOptions.OnlyOnFaulted
                    | TaskContinuationOptions.ExecuteSynchronously);
        }

        private OptimizationCommandCompletedEventArgs ExecuteAndReadState(
            OptimizationCommand command)
        {
            OptimizationActionResult result;
            try
            {
                result = ExecuteCommand(command);
            }
            catch (Exception exception)
            {
                result = OptimizationActionResult.Failed(
                    OperationCode.UnhandledException,
                    "The requested operation failed before it could be verified.",
                    exception.Message);
            }

            OptimizationStateSnapshot state;
            try
            {
                state = _actions.ReadState();
            }
            catch (Exception exception)
            {
                state = OptimizationStateSnapshot.Unavailable(
                    "Read-back failed: " + exception.Message);
            }

            return new OptimizationCommandCompletedEventArgs(
                command,
                result,
                state);
        }

        private OptimizationActionResult ExecuteCommand(OptimizationCommand command)
        {
            switch (command.Kind)
            {
                case OptimizationCommandKind.SetDisplayRefreshRate:
                    return _actions.SetDisplayRefreshRate(
                        command.RefreshRateHz,
                        RequestDisplayConfirmation);
                case OptimizationCommandKind.InstallDisplaySupport:
                    return _actions.InstallDisplaySupport();
                case OptimizationCommandKind.RemoveDisplaySupport:
                    return _actions.RemoveDisplaySupport();
                case OptimizationCommandKind.ApplyCpuPreset:
                    return command.CpuPreset.HasValue
                        ? _actions.ApplyCpuPreset(command.CpuPreset.Value)
                        : OptimizationActionResult.Failed(
                            OperationCode.InvalidRequest,
                            "The CPU preset was not supplied.",
                            string.Empty);
                case OptimizationCommandKind.RestoreCpuPower:
                    return _actions.RestoreCpuPower();
                case OptimizationCommandKind.ApplyCombinedProfile:
                    return ApplyCombinedProfile(command);
                default:
                    return OptimizationActionResult.Failed(
                        OperationCode.InvalidRequest,
                        "The requested optimization command is not recognized.",
                        string.Empty);
            }
        }

        private OptimizationActionResult ApplyCombinedProfile(
            OptimizationCommand command)
        {
            if (!command.CpuPreset.HasValue)
            {
                return OptimizationActionResult.Failed(
                    OperationCode.InvalidRequest,
                    "The combined profile did not include a CPU preset.",
                    string.Empty);
            }

            if (command.IncludesDisplayStep)
            {
                OptimizationActionResult display = _actions.SetDisplayRefreshRate(
                    command.RefreshRateHz,
                    RequestDisplayConfirmation);
                if (display == null || !display.Succeeded)
                {
                    return display == null
                        ? OptimizationActionResult.Failed(
                            OperationCode.CombinedProfileDisplayIncomplete,
                            "The display step returned no result; CPU was not started.",
                            string.Empty)
                        : display.WithMessage(
                            "The display step did not complete; CPU was not started. "
                                + display.Message,
                            OperationCode.CombinedProfileDisplayIncomplete);
                }
            }

            OptimizationActionResult cpu = _actions.ApplyCpuPreset(
                command.CpuPreset.Value);
            if (cpu == null || !cpu.Succeeded)
            {
                return cpu == null
                    ? OptimizationActionResult.Failed(
                        OperationCode.StateVerificationFailed,
                        "The CPU step returned no result after the display step completed.",
                        string.Empty)
                    : cpu.WithMessage(
                        "The display step completed; the CPU step did not. "
                            + cpu.Message,
                        cpu.Code);
            }

            return OptimizationActionResult.Successful(
                string.IsNullOrWhiteSpace(command.DisplayName)
                    ? "The display step and CPU step completed sequentially."
                    : command.DisplayName
                        + " completed: display first, then CPU. The operations remain "
                        + "independently recoverable.",
                OperationCode.CombinedProfileApplied,
                cpu.RestartRequired);
        }

        private DisplayModeConfirmationDecision RequestDisplayConfirmation(
            DisplayModeConfirmationRequest request)
        {
            Func<DisplayModeConfirmationRequest, DisplayModeConfirmationDecision>
                confirmation;
            lock (_gate)
            {
                confirmation = _displayConfirmation;
            }

            if (confirmation == null)
            {
                return DisplayModeConfirmationDecision.Revert;
            }

            if (_uiContext == null || SynchronizationContext.Current == _uiContext)
            {
                return confirmation(request);
            }

            DisplayModeConfirmationDecision decision =
                DisplayModeConfirmationDecision.Revert;
            _uiContext.Send(
                delegate(object ignored)
                {
                    decision = confirmation(request);
                },
                null);
            return decision;
        }

        private void Complete(
            OptimizationCommandCompletedEventArgs completion)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _operationRunning = false;
                _reconciliationRequired = completion == null
                    || completion.Result == null
                    || completion.Result.Outcome == OperationOutcome.Indeterminate;
            }

            PublishStateChanged();
            PublishCompleted(completion);
        }

        private string CurrentBusyReasonLocked()
        {
            if (_operationRunning)
            {
                return "An optimization command is already running.";
            }

            if (_reconciliationRequired)
            {
                return "The previous command is indeterminate; recovery read-back is required.";
            }

            return string.Empty;
        }

        private void PublishStateChanged()
        {
            bool isBusy;
            string reason;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                isBusy = _operationRunning || _reconciliationRequired;
                reason = CurrentBusyReasonLocked();
            }

            PostToUi(
                delegate {
                    EventHandler<OptimizationCommandRunnerStateChangedEventArgs>
                        handler = StateChanged;
                    if (handler != null)
                    {
                        handler(this,
                            new OptimizationCommandRunnerStateChangedEventArgs(
                                isBusy,
                                reason));
                    }
                });
        }

        private void PublishCompleted(
            OptimizationCommandCompletedEventArgs completion)
        {
            PostToUi(
                delegate {
                    EventHandler<OptimizationCommandCompletedEventArgs> handler =
                        Completed;
                    if (handler != null)
                    {
                        handler(this, completion);
                    }
                });
        }

        private void PostToUi(Action callback)
        {
            if (callback == null)
            {
                return;
            }

            if (_uiContext == null || SynchronizationContext.Current == _uiContext)
            {
                callback();
                return;
            }

            _uiContext.Post(delegate(object ignored) { callback(); }, null);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("OptimizationCommandRunner");
            }
        }
    }
}
