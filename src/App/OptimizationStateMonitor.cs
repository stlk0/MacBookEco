using System;
using System.Threading;

namespace MacBookEco.App
{
    public sealed class OptimizationStateChangedEventArgs : EventArgs
    {
        public OptimizationStateChangedEventArgs(OptimizationStateSnapshot state)
        {
            State = state;
        }

        public OptimizationStateSnapshot State { get; private set; }
    }

    /// <summary>
    /// Single owner of the current optimization state.
    ///
    /// Reading that state is not cheap: it opens and parses the protected
    /// journals, calls PowerGetActiveScheme and PowerReadFriendlyName, and for
    /// a restored EDID transaction enumerates every installed monitor devnode
    /// through SetupAPI, including non-present ones, then hashes the EDID. The
    /// tray and the dashboard used to do all of that on the WinForms thread on
    /// a timer, which both stalls the UI and contradicts the project rule that
    /// monitoring must not materially change what it measures.
    ///
    /// This class performs the read on a timer thread and marshals the result
    /// back through the UI synchronization context, so consumers only ever
    /// touch a cached snapshot.
    /// </summary>
    public sealed class OptimizationStateMonitor : IDisposable
    {
        private static readonly TimeSpan VisibleInterval = TimeSpan.FromSeconds(5.0);
        private static readonly TimeSpan HiddenInterval = TimeSpan.FromSeconds(30.0);

        private readonly IOptimizationActionService _actions;
        private readonly SynchronizationContext _uiContext;
        private readonly object _gate = new object();
        private readonly Timer _timer;

        private OptimizationStateSnapshot _current;
        private bool _started;
        private bool _dashboardVisible;
        private bool _refreshing;
        private bool _refreshQueued;
        private bool _disposed;

        public OptimizationStateMonitor(
            IOptimizationActionService actions,
            SynchronizationContext uiContext)
        {
            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions));
            }

            _actions = actions;
            _uiContext = uiContext;
            _current = OptimizationStateSnapshot.Unavailable(
                "The optimization state has not been read yet.");
            _timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
        }

        public event EventHandler<OptimizationStateChangedEventArgs> Changed;

        public OptimizationStateSnapshot Current
        {
            get
            {
                lock (_gate)
                {
                    return _current;
                }
            }
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_disposed || _started)
                {
                    return;
                }

                _started = true;
            }

            RequestRefresh();
        }

        public void SetDashboardVisible(bool visible)
        {
            bool refresh = false;
            lock (_gate)
            {
                if (_disposed || _dashboardVisible == visible)
                {
                    return;
                }

                _dashboardVisible = visible;
                refresh = visible;
            }

            if (refresh)
            {
                RequestRefresh();
            }
            else
            {
                ScheduleNext();
            }
        }

        /// <summary>
        /// Reads the state again as soon as a pool thread is free. Safe to
        /// call from the UI thread: it never blocks on the read.
        /// </summary>
        public void RequestRefresh()
        {
            lock (_gate)
            {
                if (_disposed || !_started)
                {
                    return;
                }

                // A read that is already in flight cannot answer this request:
                // it may have sampled before whatever prompted it. Recording
                // the request means the in-flight read re-arms the timer
                // instead of the request being dropped, which used to leave
                // the post-command read-back invisible for a whole interval.
                _refreshQueued = true;
            }

            _timer.Change(0, Timeout.Infinite);
        }

        /// <summary>
        /// Accepts the read-back a completed command already performed, so the
        /// UI does not have to wait a further poll interval to agree with it.
        /// </summary>
        public void Publish(OptimizationStateSnapshot state)
        {
            if (state == null)
            {
                return;
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _current = state;
            }

            RaiseChanged(state);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _started = false;
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                _timer.Dispose();
            }
        }

        private void OnTimer(object ignored)
        {
            lock (_gate)
            {
                if (_disposed || !_started || _refreshing)
                {
                    // _refreshQueued stays set, so the read in flight re-arms.
                    return;
                }

                _refreshQueued = false;
                _refreshing = true;
            }

            OptimizationStateSnapshot state;
            try
            {
                state = _actions.ReadState()
                    ?? OptimizationStateSnapshot.Unavailable(
                        "The action provider returned no state.");
            }
            catch (Exception exception)
            {
                state = OptimizationStateSnapshot.Unavailable(
                    "Optimization state could not be read: " + exception.Message);
            }
            finally
            {
                lock (_gate)
                {
                    _refreshing = false;
                }
            }

            bool publish;
            lock (_gate)
            {
                publish = !_disposed;
                if (publish)
                {
                    _current = state;
                }
            }

            if (publish)
            {
                RaiseChanged(state);
            }

            ScheduleNext();
        }

        private void ScheduleNext()
        {
            lock (_gate)
            {
                if (_disposed || !_started)
                {
                    return;
                }

                if (_refreshQueued)
                {
                    _timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
                    return;
                }

                TimeSpan interval = _dashboardVisible
                    ? VisibleInterval
                    : HiddenInterval;
                _timer.Change(interval, Timeout.InfiniteTimeSpan);
            }
        }

        private void RaiseChanged(OptimizationStateSnapshot state)
        {
            PostToUi(
                delegate {
                    EventHandler<OptimizationStateChangedEventArgs> handler = Changed;
                    if (handler != null)
                    {
                        handler(
                            this,
                            new OptimizationStateChangedEventArgs(state));
                    }
                });
        }

        private void PostToUi(Action callback)
        {
            if (_uiContext == null || SynchronizationContext.Current == _uiContext)
            {
                callback();
                return;
            }

            _uiContext.Post(delegate(object ignored) { callback(); }, null);
        }
    }
}
