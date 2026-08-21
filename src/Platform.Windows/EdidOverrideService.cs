using System;
using MacBookEco.Core;

namespace MacBookEco.Platform.Windows
{
    /// <summary>
    /// Coordinates the journaled EDID state machine.  The durable journal
    /// contains ownership facts only; the resolver always obtains a fresh
    /// SetupAPI devnode and never opens a journal-derived registry path.
    /// </summary>
    public sealed class EdidOverrideService
    {
        private readonly HardwareDiscoveryService discovery;
        private readonly InternalDisplayTargetResolver targetResolver;

        public EdidOverrideService()
            : this(
                new HardwareDiscoveryService(),
                new InternalDisplayTargetResolver())
        {
        }

        internal EdidOverrideService(
            HardwareDiscoveryService discovery,
            InternalDisplayTargetResolver targetResolver)
        {
            if (discovery == null)
                throw new ArgumentNullException(nameof(discovery));
            if (targetResolver == null)
                throw new ArgumentNullException(nameof(targetResolver));

            this.discovery = discovery;
            this.targetResolver = targetResolver;
        }

        public EdidOverrideOperationResult InstallVerifiedProfile()
        {
            PrivilegeGuard.RequireAdministrator();
            using (JournalStore journals = JournalStore.OpenEdidMutation())
            {
                EdidJournal journal = journals.ReadEdid();
                if (journal != null)
                {
                    if (journal.State == EdidJournalState.RestorePending)
                    {
                        // Every command reconciles an interrupted operation
                        // first.  This may complete an already-durable delete
                        // without consulting an active CCD route.
                        EdidOverrideOperationResult restoreResult =
                            ReconcileRestore(journals, journal);
                        if (!restoreResult.Succeeded)
                            return restoreResult;

                        journal = journals.ReadEdid();
                    }

                    if (journal.State == EdidJournalState.InstallPending ||
                        journal.State == EdidJournalState.Installed ||
                        journal.State == EdidJournalState.Conflict)
                    {
                        EdidOverrideOperationResult reconcileResult =
                            ReconcileInstall(journals, journal);
                        if (!reconcileResult.Succeeded)
                        {
                            return reconcileResult;
                        }

                        journal = journals.ReadEdid();
                        if (!ShouldUpgradeInstalledProfile(journal))
                        {
                            return reconcileResult;
                        }

                        // Prove every new-install prerequisite before removing
                        // the exact legacy override. The actual removal and
                        // replacement remain separate durable transactions, so
                        // every crash boundary can be reconciled without
                        // treating foreign bytes as app-owned.
                        ValidateUpgradePreconditions(journal);
                        EdidOverrideOperationResult restoreResult =
                            ReconcileRestore(journals, journal);
                        if (!restoreResult.Succeeded)
                        {
                            return restoreResult;
                        }

                        return BeginNewInstall(
                            journals,
                            journals.ReadEdid());
                    }
                }

                return BeginNewInstall(journals, journal);
            }
        }

        private bool ShouldUpgradeInstalledProfile(EdidJournal journal)
        {
            if (journal == null ||
                journal.State != EdidJournalState.Installed ||
                journal.Payload == null ||
                journal.Payload.Target == null)
            {
                return false;
            }

            HardwareSnapshot hardware = discovery.Discover().ToCoreSnapshot();
            ProfileSelectionResult selection = ProfileCatalog.Select(hardware);
            return selection.HardwareSupported &&
                ProfileCatalog.ShouldRefreshInstalledProfile(
                    journal.Payload.Target.ProfileId,
                    selection.Profile.Id);
        }

        private void ValidateUpgradePreconditions(EdidJournal journal)
        {
            WindowsHardwareSnapshot snapshot = discovery.Discover();
            HardwareSnapshot observedHardware = snapshot.ToCoreSnapshot();
            ResolvedMonitorTarget target = targetResolver.ResolveActive();
            ValidateActiveTarget(snapshot, observedHardware, target);
            HardwareSnapshot installHardware = ResolveJournaledOriginalHardware(
                journal,
                observedHardware,
                target,
                true);
            DisplayProfile profile = ProfileCatalog.Select(
                installHardware).Profile;
            ValidateInstallPreconditions(
                snapshot,
                installHardware,
                profile);
        }

        public EdidOverrideOperationResult RestoreOriginal()
        {
            PrivilegeGuard.RequireAdministrator();
            using (JournalStore journals = JournalStore.OpenEdidMutation())
            {
                EdidJournal journal = journals.ReadEdid();
                if (journal == null || journal.State == EdidJournalState.NotInstalled)
                {
                    throw new InvalidOperationException(
                        "No trusted MacBook Eco display transaction exists.");
                }

                if (journal.State == EdidJournalState.Conflict)
                {
                    throw new SecureStateConflictException(
                        "The display journal is in Conflict. Automatic mutation is disabled.");
                }

                // Do not switch the active desktop mode here.  Restore must
                // remain available when the internal panel is non-present,
                // not primary, or no longer has an active CCD endpoint.
                return ReconcileRestore(journals, journal);
            }
        }

        private EdidOverrideOperationResult BeginNewInstall(
            JournalStore journals,
            EdidJournal previous)
        {
            WindowsHardwareSnapshot snapshot = discovery.Discover();
            HardwareSnapshot observedHardware = snapshot.ToCoreSnapshot();
            ResolvedMonitorTarget target = targetResolver.ResolveActive();
            ValidateActiveTarget(snapshot, observedHardware, target);

            HardwareSnapshot installHardware = observedHardware;
            if (previous != null &&
                EdidRecoveryPolicy.RequiresOriginalForNewInstall(
                    previous.State))
            {
                installHardware = ResolveJournaledOriginalHardware(
                    previous,
                    observedHardware,
                    target,
                    false);
            }
            ProfileSelectionResult selection = ProfileCatalog.Select(
                installHardware);

            DisplayProfile profile = selection.Profile;
            ValidateInstallPreconditions(
                snapshot,
                installHardware,
                profile);

            // The pre-intent comparison proves that a new operation cannot
            // take ownership of an existing or incorrectly typed value.
            if (target.ReadOverride() != null)
            {
                throw new InvalidOperationException(
                    "A pre-existing EDID override was found. MacBook Eco will not merge or overwrite it.");
            }

            byte[] expectedOverride = profile.BuildOverride(
                installHardware.Edid).ToByteArray();
            MonitorIdentity originalIdentity =
                MonitorIdentity.FromExactBaseEdid(
                    target.DeviceInstanceId,
                    target.HardwareId,
                    installHardware.Edid);
            EdidJournalPayload payload = new EdidJournalPayload(
                new EdidTargetIdentity(profile.Id, originalIdentity),
                Sha256Digest.Compute(expectedOverride));
            DateTime now = DateTime.UtcNow;
            EdidJournal intent = new EdidJournal(
                JournalOperationId.NewId(),
                NextGeneration(previous),
                now,
                now,
                EdidJournalState.InstallPending,
                payload);

            // All recovery identity and exact expected ownership bytes are
            // durably recorded before the first HKLM mutation.
            intent = journals.SaveEdid(intent);
            return ReconcileInstall(journals, intent);
        }

        private EdidOverrideOperationResult ReconcileInstall(
            JournalStore journals,
            EdidJournal journal)
        {
            if (journal == null ||
                (journal.State != EdidJournalState.InstallPending &&
                 journal.State != EdidJournalState.Installed &&
                 journal.State != EdidJournalState.Conflict))
            {
                throw new ArgumentException(
                    "Install reconciliation requires an install journal state.",
                    nameof(journal));
            }

            InstallContext context;
            try
            {
                context = ResolveInstallContext(journal);
            }
            catch (Exception exception)
            {
                return ReconciliationFailure(
                    journals,
                    journal,
                    "The pending EDID install could not revalidate its exact target.",
                    exception,
                    false);
            }

            EdidLiveOverrideState liveState;
            try
            {
                liveState = context.ProvenLiveState.HasValue
                    ? context.ProvenLiveState.Value
                    : context.Target.ClassifyOverride(
                        context.ExpectedOverride);
            }
            catch (Exception exception)
            {
                return ReconciliationFailure(
                    journals,
                    journal,
                    "The EDID override could not be classified safely during install reconciliation.",
                    exception,
                    false);
            }

            EdidReconciliationAction action = EdidRecoveryPolicy.ForInstall(
                journal.State,
                liveState);
            switch (action)
            {
                case EdidReconciliationAction.ConfirmInstalled:
                    return EdidOverrideOperationResult.Success(
                        "The verified display override is already installed.",
                        journal.Payload.Target.ProfileId,
                        true);

                case EdidReconciliationAction.MarkInstalled:
                    return PersistInstalled(journals, journal);

                case EdidReconciliationAction.WriteOwnedOverride:
                    // Recovery must retain the existing InstallPending state
                    // when a reversible current-topology prerequisite is not
                    // met (for example, an external display is attached).
                    // It is not evidence that the durable identity or owned
                    // bytes are foreign, so do not convert it to Conflict.
                    ValidateInstallMutationPreconditions(
                        context.Snapshot,
                        context.Hardware,
                        context.Profile);
                    try
                    {
                        // The resolver repeats compare-before-write and exact
                        // read-back through a newly proven SetupAPI devnode.
                        context.Target.WriteOwnedOverride(
                            context.ExpectedOverride);
                        if (context.Target.ClassifyOverride(
                                context.ExpectedOverride) !=
                            EdidLiveOverrideState.ExactOwned)
                        {
                            throw new SecureStateConflictException(
                                "The EDID write did not converge to the exact owned bytes.");
                        }
                    }
                    catch (Exception exception)
                    {
                        return ReconciliationFailure(
                            journals,
                            journal,
                            "The pending EDID install did not complete with the exact owned bytes.",
                            exception,
                            true);
                    }

                    return PersistInstalled(journals, journal);

                case EdidReconciliationAction.Conflict:
                    throw RecordConflict(
                        journals,
                        journal,
                        "The current EDID override differs from the durable app-owned value.",
                        null);

                case EdidReconciliationAction.Blocked:
                    throw new SecureStateConflictException(
                        "The conflicted display transaction does not match the "
                            + "exact app-owned override. Repair is disabled.");

                default:
                    throw RecordConflict(
                        journals,
                        journal,
                        "The EDID install journal has no safe reconciliation action.",
                        null);
            }
        }

        private EdidOverrideOperationResult ReconcileRestore(
            JournalStore journals,
            EdidJournal journal)
        {
            while (true)
            {
                if (journal == null)
                {
                    throw new InvalidOperationException(
                        "No trusted MacBook Eco display transaction exists.");
                }

                if (journal.State == EdidJournalState.Conflict ||
                    journal.State == EdidJournalState.NotInstalled)
                {
                    throw new SecureStateConflictException(
                        "The display journal has no safe automatic restore action.");
                }

                RestoreContext context;
                try
                {
                    context = ResolveRestoreContext(journal);
                }
                catch (Exception exception)
                {
                    return ReconciliationFailure(
                        journals,
                        journal,
                        "The EDID restore could not revalidate its stored monitor identity.",
                        exception,
                        false);
                }

                EdidLiveOverrideState liveState;
                try
                {
                    liveState = context.ProvenLiveState.HasValue
                        ? context.ProvenLiveState.Value
                        : context.Target.ClassifyOverride(
                            context.ExpectedOverride);
                }
                catch (Exception exception)
                {
                    return ReconciliationFailure(
                        journals,
                        journal,
                        "The EDID override could not be classified safely during restore reconciliation.",
                        exception,
                        false);
                }

                EdidReconciliationAction action = EdidRecoveryPolicy.ForRestore(
                    journal.State,
                    liveState);
                switch (action)
                {
                    case EdidReconciliationAction.StartRestore:
                        journal = journals.SaveEdid(journal.TransitionTo(
                            EdidJournalState.RestorePending,
                            journal.Generation.Next(),
                            DateTime.UtcNow));
                        // The intent is durable before delete.  Re-resolve all
                        // identity and live state afterwards instead of reusing
                        // this endpoint or registry handle.
                        continue;

                    case EdidReconciliationAction.DeleteOwnedOverride:
                        try
                        {
                            context.Target.DeleteExactOwnedOverride(
                                context.ExpectedOverride);
                        }
                        catch (Exception exception)
                        {
                            return ReconciliationFailure(
                                journals,
                                journal,
                                "The app-owned EDID override could not be removed safely.",
                                exception,
                                true);
                        }

                        return PersistRestored(journals, journal);

                    case EdidReconciliationAction.MarkRestored:
                        // This is the crash-after-delete boundary: durable
                        // RestorePending plus an absent value is sufficient to
                        // finish without repeating a destructive call.
                        return PersistRestored(journals, journal);

                    case EdidReconciliationAction.ConfirmRestored:
                        return EdidOverrideOperationResult.Success(
                            "The original monitor state is already restored.",
                            journal.Payload.Target.ProfileId,
                            false);

                    case EdidReconciliationAction.Conflict:
                        throw RecordConflict(
                            journals,
                            journal,
                            "The current EDID override differs from the durable app-owned value.",
                            null);

                    default:
                        throw RecordConflict(
                            journals,
                            journal,
                            "The EDID restore journal has no safe reconciliation action.",
                            null);
                }
            }
        }

        private InstallContext ResolveInstallContext(EdidJournal journal)
        {
            RequirePayload(journal);
            DisplayProfile profile = ProfileCatalog.GetById(
                journal.Payload.Target.ProfileId);
            if (profile == null)
            {
                return ResolveHistoricalInstallContext(journal);
            }

            WindowsHardwareSnapshot snapshot = discovery.Discover();
            HardwareSnapshot hardware = snapshot.ToCoreSnapshot();
            ValidateInstallHardware(hardware, profile);

            ResolvedMonitorTarget target = targetResolver.ResolveActive();
            ValidateActiveTarget(snapshot, hardware, target);
            if (!target.MatchesIdentity(
                    journal.Payload.Target.Monitor,
                    journal.Payload.OwnedOverrideHash))
            {
                throw new SecureStateConflictException(
                    "The active internal display no longer matches the durable EDID identity.");
            }

            EdidBaseBlock originalEdid;
            if (!target.TryResolveOriginalBaseEdid(
                    journal.Payload.Target.Monitor,
                    out originalEdid))
            {
                throw new SecureStateConflictException(
                    "The original EDID could not be proven during install " +
                    "reconciliation.");
            }

            HardwareSnapshot installHardware = WithEdid(
                hardware,
                originalEdid);
            ValidateInstallHardware(installHardware, profile);

            byte[] expectedOverride = CompileOwnedOverride(
                profile,
                target,
                journal.Payload.Target.Monitor,
                journal.Payload.OwnedOverrideHash);
            VerifyJournaledOwnershipHash(journal, expectedOverride);
            return new InstallContext(
                target,
                expectedOverride,
                snapshot,
                installHardware,
                profile);
        }

        private InstallContext ResolveHistoricalInstallContext(
            EdidJournal journal)
        {
            if (journal.State != EdidJournalState.Installed &&
                journal.State != EdidJournalState.Conflict)
            {
                throw new SecureStateConflictException(
                    "An unfinished install references no compiled display profile.");
            }

            ResolvedMonitorTarget target = targetResolver.ResolveStoredForRestore(
                journal.Payload.Target.Monitor,
                journal.Payload.OwnedOverrideHash);
            if (!target.MatchesIdentity(
                    journal.Payload.Target.Monitor,
                    journal.Payload.OwnedOverrideHash))
            {
                throw new SecureStateConflictException(
                    "The historical display target no longer matches its " +
                    "durable identity.");
            }

            byte[] currentOverride = target.ReadOverride();
            // The profile ID may come from a build newer or older than this
            // catalog. The protected journal still proves the exact resource:
            // stable monitor identity plus the digest recorded before write.
            EdidLiveOverrideState liveState =
                EdidRecoveryPolicy.ClassifyProtectedJournalOverride(
                    currentOverride,
                    journal.Payload.OwnedOverrideHash);
            return new InstallContext(
                target,
                liveState == EdidLiveOverrideState.ExactOwned
                    ? currentOverride
                    : null,
                null,
                null,
                null,
                liveState);
        }

        private RestoreContext ResolveRestoreContext(EdidJournal journal)
        {
            RequirePayload(journal);
            DisplayProfile profile = ProfileCatalog.GetById(
                journal.Payload.Target.ProfileId);

            // This resolver has no active CCD/GDI dependency.  It enumerates
            // monitor-class devnodes, including non-present devices, and
            // independently proves every durable monitor field first.
            ResolvedMonitorTarget target = targetResolver.ResolveStoredForRestore(
                journal.Payload.Target.Monitor,
                journal.Payload.OwnedOverrideHash);
            if (!target.MatchesIdentity(
                    journal.Payload.Target.Monitor,
                    journal.Payload.OwnedOverrideHash))
            {
                throw new SecureStateConflictException(
                    "The re-resolved monitor does not match the durable EDID identity.");
            }

            if (profile == null)
            {
                byte[] currentOverride = target.ReadOverride();
                // DeleteExactOwnedOverride later compares these same bytes
                // again on the mutation handle; the journal hash alone never
                // authorizes a blind delete.
                EdidLiveOverrideState liveState =
                    EdidRecoveryPolicy.ClassifyProtectedJournalOverride(
                        currentOverride,
                        journal.Payload.OwnedOverrideHash);
                return new RestoreContext(
                    target,
                    liveState == EdidLiveOverrideState.ExactOwned
                        ? currentOverride
                        : null,
                    liveState);
            }

            EdidBaseBlock originalEdid;
            if (!target.TryResolveOriginalBaseEdid(
                    journal.Payload.Target.Monitor,
                    out originalEdid))
            {
                throw new SecureStateConflictException(
                    "The original EDID could not be proven for restore.");
            }

            ValidateRestoreProfile(profile, target, originalEdid);
            byte[] expectedOverride = CompileOwnedOverride(
                profile,
                target,
                journal.Payload.Target.Monitor,
                journal.Payload.OwnedOverrideHash);
            VerifyJournaledOwnershipHash(journal, expectedOverride);
            return new RestoreContext(target, expectedOverride, null);
        }

        private static void ValidateInstallPreconditions(
            WindowsHardwareSnapshot snapshot,
            HardwareSnapshot hardware,
            DisplayProfile profile)
        {
            ValidateInstallHardware(hardware, profile);
            ValidateInstallMutationPreconditions(snapshot, hardware, profile);
        }

        private static HardwareSnapshot ResolveJournaledOriginalHardware(
            EdidJournal journal,
            HardwareSnapshot observedHardware,
            ResolvedMonitorTarget target,
            bool requireOwnedOverride)
        {
            RequirePayload(journal);
            if (observedHardware == null || target == null ||
                !target.MatchesIdentity(
                    journal.Payload.Target.Monitor,
                    journal.Payload.OwnedOverrideHash))
            {
                throw new SecureStateConflictException(
                    "The cached display cannot be tied to the protected " +
                    "journal identity.");
            }

            byte[] liveOverride = target.ReadOverride();
            EdidLiveOverrideState liveState =
                EdidRecoveryPolicy.ClassifyProtectedJournalOverride(
                    liveOverride,
                    journal.Payload.OwnedOverrideHash);
            if ((requireOwnedOverride &&
                 liveState != EdidLiveOverrideState.ExactOwned) ||
                (!requireOwnedOverride &&
                 liveState != EdidLiveOverrideState.Absent))
            {
                throw new SecureStateConflictException(
                    "The live override does not match the protected " +
                    "refresh transition boundary.");
            }

            EdidBaseBlock originalEdid;
            if (!target.TryResolveOriginalBaseEdid(
                    journal.Payload.Target.Monitor,
                    out originalEdid))
            {
                throw new SecureStateConflictException(
                    "The exact original EDID could not be recovered from " +
                    "the protected journal fingerprint.");
            }

            return WithEdid(observedHardware, originalEdid);
        }

        private static HardwareSnapshot WithEdid(
            HardwareSnapshot hardware,
            EdidBaseBlock edid)
        {
            if (hardware == null)
            {
                throw new ArgumentNullException(nameof(hardware));
            }

            return new HardwareSnapshot(
                hardware.SystemManufacturer,
                hardware.SystemModel,
                hardware.IsInternalDisplay,
                hardware.PanelHardwareId,
                edid,
                hardware.GpuName,
                hardware.GpuDeviceId,
                hardware.DriverVersion);
        }

        private static void ValidateInstallHardware(
            HardwareSnapshot hardware,
            DisplayProfile profile)
        {
            if (profile == null)
            {
                throw new NotSupportedException(
                    "No reviewed display profile matches this Mac, panel, EDID and adapter.");
            }

            DisplayProfileMatch match = profile.Match(hardware);
            if (!match.HardwareSupported)
            {
                throw new NotSupportedException(
                    "The discovered hardware no longer matches the reviewed display profile.");
            }
        }

        private static void ValidateInstallMutationPreconditions(
            WindowsHardwareSnapshot snapshot,
            HardwareSnapshot hardware,
            DisplayProfile profile)
        {
            if (snapshot == null || snapshot.InternalDisplay == null ||
                string.IsNullOrEmpty(snapshot.InternalDisplay.DeviceInstanceId))
            {
                throw new InvalidOperationException(
                    "The active internal display could not be mapped to its monitor devnode.");
            }

            if (snapshot.ActiveDisplayCount != 1)
            {
                throw new InvalidOperationException(
                    "Disconnect external displays before installing a panel override.");
            }

            if (snapshot.CurrentDisplayMode == null ||
                snapshot.CurrentDisplayMode.RefreshRate != 60)
            {
                throw new InvalidOperationException(
                    "Installation requires the current desktop mode to be at 60 Hz.");
            }

            DisplayProfileMatch match = profile.Match(hardware);
            if (!match.CanInstall)
            {
                throw new InvalidOperationException(
                    "The supported panel does not have enough free EDID "
                        + "descriptor slots for the owned override.");
            }
        }

        private static void ValidateActiveTarget(
            WindowsHardwareSnapshot snapshot,
            HardwareSnapshot hardware,
            ResolvedMonitorTarget target)
        {
            if (target == null || target.Endpoint == null ||
                snapshot == null || snapshot.InternalDisplay == null ||
                hardware == null)
            {
                throw new SecureStateConflictException(
                    "The active internal display target is incomplete.");
            }

            if (!string.Equals(
                    NormalizeInstanceId(snapshot.InternalDisplay.DeviceInstanceId),
                    target.DeviceInstanceId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    hardware.PanelHardwareId,
                    target.HardwareId,
                    StringComparison.Ordinal) ||
                !FixedTimeComparer.AreEqual(
                    hardware.Edid.ToByteArray(),
                    target.BaseEdid))
            {
                throw new SecureStateConflictException(
                    "Discovery and the active SetupAPI target do not describe the same internal panel.");
            }
        }

        private static void ValidateRestoreProfile(
            DisplayProfile profile,
            ResolvedMonitorTarget target,
            EdidBaseBlock originalEdid)
        {
            if (profile == null || target == null || originalEdid == null)
            {
                throw new SecureStateConflictException(
                    "The restore profile or monitor target is unavailable.");
            }

            if (!string.Equals(
                    profile.PanelHardwareId,
                    target.HardwareId,
                    StringComparison.Ordinal) ||
                !profile.NormalizedEdidSignature.Equals(
                    originalEdid.NormalizedSignature) ||
                !profile.NativeTiming.Equals(originalEdid.PreferredTiming))
            {
                throw new SecureStateConflictException(
                    "The stored monitor identity no longer matches the compiled display profile.");
            }
        }

        private static byte[] CompileOwnedOverride(
            DisplayProfile profile,
            ResolvedMonitorTarget target,
            MonitorIdentity originalIdentity,
            Sha256Digest ownedOverrideHash)
        {
            if (profile == null || target == null || originalIdentity == null ||
                ownedOverrideHash == null)
            {
                throw new ArgumentNullException(
                    profile == null
                        ? "profile"
                        : target == null
                            ? "target"
                            : originalIdentity == null
                                ? "originalIdentity"
                                : "ownedOverrideHash");
            }

            if (target.BaseEdidHash.Equals(ownedOverrideHash))
            {
                return target.BaseEdid;
            }

            EdidBaseBlock originalEdid;
            if (!target.TryResolveOriginalBaseEdid(
                    originalIdentity,
                    out originalEdid))
            {
                throw new SecureStateConflictException(
                    "The original EDID could not be proven while compiling " +
                    "the owned override.");
            }

            return profile.BuildOverride(originalEdid).ToByteArray();
        }

        private static void VerifyJournaledOwnershipHash(
            EdidJournal journal,
            byte[] expectedOverride)
        {
            RequirePayload(journal);
            if (!Sha256Digest.Compute(expectedOverride).Equals(
                    journal.Payload.OwnedOverrideHash))
            {
                throw new SecureStateConflictException(
                    "The compiled EDID override does not match the journaled ownership hash.");
            }
        }

        private static EdidOverrideOperationResult PersistInstalled(
            JournalStore journals,
            EdidJournal journal)
        {
            try
            {
                EdidJournal installed = journals.SaveEdid(journal.TransitionTo(
                    EdidJournalState.Installed,
                    journal.Generation.Next(),
                    DateTime.UtcNow));
                return EdidOverrideOperationResult.Success(
                    "The verified Eco display descriptors were installed. " +
                    "A display-adapter reload or reboot is required before the mode appears.",
                    installed.Payload.Target.ProfileId,
                    true);
            }
            catch (Exception)
            {
                // The exact owned bytes were already read back, but the
                // durable InstallPending record could not be finalized.  Do
                // not claim success: the next run must reconcile that intent
                // against live bytes before allowing another mutation.
                return EdidOverrideOperationResult.Indeterminate(
                    "The EDID override was written and verified, but its final " +
                    "journal state could not be saved. Run recovery before another display change.",
                    journal.Payload.Target.ProfileId,
                    true);
            }
        }

        private static EdidOverrideOperationResult PersistRestored(
            JournalStore journals,
            EdidJournal journal)
        {
            try
            {
                EdidJournal restored = journals.SaveEdid(journal.TransitionTo(
                    EdidJournalState.Restored,
                    journal.Generation.Next(),
                    DateTime.UtcNow));
                return EdidOverrideOperationResult.Success(
                    "The original EDID state was restored. " +
                    "A display-adapter reload or reboot is required.",
                    restored.Payload.Target.ProfileId,
                    true);
            }
            catch (Exception)
            {
                // RestorePending is already durable and the owned value is
                // absent after read-back.  Preserve that recovery evidence
                // and make the uncertainty explicit instead of returning a
                // false success.
                return EdidOverrideOperationResult.Indeterminate(
                    "The owned EDID override is absent, but its final journal " +
                    "state could not be saved. Run recovery before another display change.",
                    journal.Payload.Target.ProfileId,
                    true);
            }
        }

        private static SecureStateConflictException RecordConflict(
            JournalStore journals,
            EdidJournal journal,
            string message,
            Exception cause)
        {
            try
            {
                if (journal == null ||
                    !EdidJournal.CanTransition(
                        journal.State,
                        EdidJournalState.Conflict))
                {
                    return cause == null
                        ? new SecureStateConflictException(message)
                        : new SecureStateConflictException(message, cause);
                }

                journals.SaveEdid(journal.TransitionTo(
                    EdidJournalState.Conflict,
                    journal.Generation.Next(),
                    DateTime.UtcNow));
            }
            catch (Exception persistException)
            {
                return new SecureStateConflictException(
                    message + " The durable conflict state could not be saved; recovery is indeterminate.",
                    persistException);
            }

            return cause == null
                ? new SecureStateConflictException(message)
                : new SecureStateConflictException(message, cause);
        }

        private static EdidOverrideOperationResult ReconciliationFailure(
            JournalStore journals,
            EdidJournal journal,
            string message,
            Exception cause,
            bool deviceReloadRequired)
        {
            if (cause is SecureStateConflictException)
                throw RecordConflict(journals, journal, message, cause);

            return EdidOverrideOperationResult.Indeterminate(
                message + " The live state could not be proven; the durable "
                    + "journal remains unchanged for retry. "
                    + SafeExceptionMessage(cause),
                journal != null && journal.Payload != null
                    ? journal.Payload.Target.ProfileId
                    : null,
                deviceReloadRequired);
        }

        private static string SafeExceptionMessage(Exception exception)
        {
            return exception == null || string.IsNullOrWhiteSpace(exception.Message)
                ? "Unknown error."
                : exception.Message.Trim();
        }

        private static void RequirePayload(EdidJournal journal)
        {
            if (journal == null || journal.Payload == null ||
                journal.Payload.Target == null ||
                journal.Payload.Target.Monitor == null ||
                journal.Payload.OwnedOverrideHash == null)
            {
                throw new SecureStateConflictException(
                    "The trusted EDID journal has no complete ownership payload.");
            }
        }

        private static JournalGeneration NextGeneration(EdidJournal previous)
        {
            return previous == null
                ? new JournalGeneration(1)
                : previous.Generation.Next();
        }

        private static string NormalizeInstanceId(string value)
        {
            return string.IsNullOrEmpty(value)
                ? null
                : value.Trim().ToUpperInvariant();
        }

        private sealed class InstallContext
        {
            internal InstallContext(
                ResolvedMonitorTarget target,
                byte[] expectedOverride,
                WindowsHardwareSnapshot snapshot,
                HardwareSnapshot hardware,
                DisplayProfile profile,
                EdidLiveOverrideState? provenLiveState = null)
            {
                Target = target;
                ExpectedOverride = expectedOverride;
                Snapshot = snapshot;
                Hardware = hardware;
                Profile = profile;
                ProvenLiveState = provenLiveState;
            }

            internal ResolvedMonitorTarget Target { get; private set; }

            internal byte[] ExpectedOverride { get; private set; }

            internal WindowsHardwareSnapshot Snapshot { get; private set; }

            internal HardwareSnapshot Hardware { get; private set; }

            internal DisplayProfile Profile { get; private set; }

            internal EdidLiveOverrideState? ProvenLiveState { get; private set; }
        }

        private sealed class RestoreContext
        {
            internal RestoreContext(
                ResolvedMonitorTarget target,
                byte[] expectedOverride,
                EdidLiveOverrideState? provenLiveState)
            {
                Target = target;
                ExpectedOverride = expectedOverride;
                ProvenLiveState = provenLiveState;
            }

            internal ResolvedMonitorTarget Target { get; private set; }

            internal byte[] ExpectedOverride { get; private set; }

            internal EdidLiveOverrideState? ProvenLiveState { get; private set; }
        }
    }

    public enum EdidOverrideOperationOutcome
    {
        Succeeded = 1,
        Indeterminate = 2
    }

    public sealed class EdidOverrideOperationResult
    {
        private EdidOverrideOperationResult(
            EdidOverrideOperationOutcome outcome,
            string message,
            string profileId,
            bool deviceReloadRequired)
        {
            if (outcome != EdidOverrideOperationOutcome.Succeeded &&
                outcome != EdidOverrideOperationOutcome.Indeterminate)
            {
                throw new ArgumentOutOfRangeException(nameof(outcome));
            }

            Outcome = outcome;
            Message = message ?? string.Empty;
            ProfileId = profileId;
            DeviceReloadRequired = deviceReloadRequired;
        }

        public EdidOverrideOperationOutcome Outcome { get; private set; }

        public bool Succeeded => Outcome == EdidOverrideOperationOutcome.Succeeded;

        public bool DeviceReloadRequired { get; private set; }
        public string ProfileId { get; private set; }
        public string Message { get; private set; }

        internal static EdidOverrideOperationResult Success(
            string message,
            string profileId,
            bool deviceReloadRequired)
        {
            return new EdidOverrideOperationResult(
                EdidOverrideOperationOutcome.Succeeded,
                message,
                profileId,
                deviceReloadRequired);
        }

        internal static EdidOverrideOperationResult Indeterminate(
            string message,
            string profileId,
            bool deviceReloadRequired)
        {
            return new EdidOverrideOperationResult(
                EdidOverrideOperationOutcome.Indeterminate,
                message,
                profileId,
                deviceReloadRequired);
        }
    }

}
