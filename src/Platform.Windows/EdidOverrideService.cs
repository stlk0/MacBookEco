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
            return InstallProfile(DisplayProfileKind.Verified, null);
        }

        public EdidOverrideOperationResult InstallExperimentalProfile(
            string acknowledgementToken)
        {
            Sha256Digest parsed;
            if (!Sha256Digest.TryParseCanonical(
                    acknowledgementToken,
                    out parsed))
            {
                throw new NotSupportedException(
                    "The experimental install requires one canonical "
                        + "acknowledgement token.");
            }

            return InstallProfile(
                DisplayProfileKind.Experimental,
                parsed.ToString());
        }

        private EdidOverrideOperationResult InstallProfile(
            DisplayProfileKind expectedKind,
            string acknowledgementToken)
        {
            PrivilegeGuard.RequireAdministrator();
            using (JournalStore journals = JournalStore.OpenEdidMutation())
            {
                EdidJournal journal = journals.ReadEdid();
                if (journal != null)
                {
                    ValidateInstallCommandForJournal(
                        journal,
                        expectedKind,
                        acknowledgementToken);

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
                        ValidateInstallCommandForJournal(
                            journal,
                            expectedKind,
                            acknowledgementToken);
                        return ReconcileInstall(journals, journal);
                    }
                }

                return BeginNewInstall(
                    journals,
                    journal,
                    expectedKind,
                    acknowledgementToken);
            }
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
            EdidJournal previous,
            DisplayProfileKind expectedKind,
            string acknowledgementToken)
        {
            WindowsHardwareSnapshot snapshot = discovery.Discover();
            HardwareSnapshot hardware = snapshot.ToCoreSnapshot();
            DisplayProfile profile = SelectReviewedProfile(hardware);
            ResolvedMonitorTarget target;
            if (profile == null)
            {
                if (expectedKind != DisplayProfileKind.Experimental)
                {
                    throw new NotSupportedException(
                        "No reviewed display profile matches this hardware.");
                }

                // Generation is deliberately last: every platform, topology,
                // full-document and ownership gate is proven first.
                ValidateExperimentalGenerationPlatformPreconditions(
                    snapshot,
                    hardware);
                target = targetResolver.ResolveActive();
                ValidateActiveTarget(snapshot, hardware, target);
                ValidateExperimentalSourceDocument(hardware, target);
                RequireAbsentOverride(target);

                ExperimentalProfileGenerationResult generated =
                    Experimental48HzProfileGenerator.GenerateFallback(hardware);
                if (!generated.Succeeded)
                {
                    throw new NotSupportedException(
                        "No experimental 48 Hz profile passes every generation gate.");
                }

                profile = generated.Profile;
            }
            else
            {
                if (expectedKind != DisplayProfileKind.Verified)
                {
                    throw new NotSupportedException(
                        "A reviewed profile has priority over experimental generation.");
                }

                target = targetResolver.ResolveActive();
                ValidateActiveTarget(snapshot, hardware, target);
                RequireAbsentOverride(target);
            }

            if (profile.Kind != expectedKind)
            {
                throw new SecureStateConflictException(
                    "The fixed Admin command does not match the selected profile kind.");
            }

            if (profile.IsExperimental)
            {
                RequireExperimentalAcknowledgement(
                    profile.Id,
                    acknowledgementToken);
            }

            ValidateInstallPreconditions(snapshot, hardware, profile);

            byte[] expectedOverride = CompileOwnedOverride(profile, target);
            ValidateCompiledExperimentalDocument(
                profile,
                target,
                expectedOverride);
            EdidJournalPayload payload = new EdidJournalPayload(
                target.CreateIdentity(profile.Id),
                Sha256Digest.Compute(expectedOverride),
                profile.IsExperimental
                    ? profile.SourceEdidSignature
                    : null);
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
                liveState = context.Target.ClassifyOverride(
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
                        IsExperimental(journal)
                            ? "The experimental local display override is already installed."
                            : "The reviewed display override is already installed.",
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
                    if (context.Profile.IsExperimental)
                    {
                        ValidateExperimentalSourceDocument(
                            context.Hardware,
                            context.Target);
                    }
                    ValidateInstallMutationPreconditions(
                        context.Snapshot,
                        context.Hardware,
                        context.Profile);
                    try
                    {
                        // The resolver repeats compare-before-write and exact
                        // read-back through a newly proven SetupAPI devnode.
                        context.Target.WriteOwnedOverride(
                            context.ExpectedOverride,
                            context.Profile.IsExperimental
                                ? context.Profile.SourceEdidSignature
                                : null);
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
                    liveState = context.Target.ClassifyOverride(
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
            WindowsHardwareSnapshot snapshot = discovery.Discover();
            HardwareSnapshot hardware = snapshot.ToCoreSnapshot();
            DisplayProfile profile = ResolveActiveProfile(
                journal.Payload.Target.ProfileId,
                hardware,
                journal.Payload.SourceEdidSignature);
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

            ValidateRecoverySourceDocument(journal, target);

            byte[] expectedOverride = CompileOwnedOverride(profile, target);
            VerifyJournaledOwnershipHash(journal, expectedOverride);
            return new InstallContext(
                target,
                expectedOverride,
                snapshot,
                hardware,
                profile);
        }

        private RestoreContext ResolveRestoreContext(EdidJournal journal)
        {
            RequirePayload(journal);
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

            ValidateRecoverySourceDocument(journal, target);

            DisplayProfile profile = ResolveRestoreProfile(
                journal.Payload.Target.ProfileId,
                target,
                journal.Payload.SourceEdidSignature);
            ValidateRestoreProfile(profile, target);
            byte[] expectedOverride = CompileOwnedOverride(profile, target);
            VerifyJournaledOwnershipHash(journal, expectedOverride);
            return new RestoreContext(target, expectedOverride);
        }

        private static DisplayProfile SelectReviewedProfile(
            HardwareSnapshot hardware)
        {
            if (hardware == null ||
                !hardware.Edid.IsDetailedTimingDescriptor(0))
            {
                return null;
            }

            ProfileSelectionResult selection = ProfileCatalog.Select(hardware);
            return selection.HardwareSupported ? selection.Profile : null;
        }

        private static void ValidateExperimentalGenerationPlatformPreconditions(
            WindowsHardwareSnapshot snapshot,
            HardwareSnapshot hardware)
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
                    "Experimental generation requires one active internal display.");
            }

            if (snapshot.CurrentDisplayMode == null ||
                snapshot.CurrentDisplayMode.RefreshRate != 60)
            {
                throw new InvalidOperationException(
                    "Experimental generation requires the native 60 Hz desktop mode.");
            }

            if (!snapshot.InternalDisplay.ExistingEdidOverrideReadSucceeded ||
                snapshot.InternalDisplay.ExistingEdidOverride != null)
            {
                throw new InvalidOperationException(
                    "Experimental generation requires a proven absent EDID override.");
            }

            if (hardware == null || !hardware.CompleteEdidIsValid ||
                hardware.NormalizedSourceEdidSignature == null)
            {
                throw new InvalidOperationException(
                    "Experimental generation requires a valid complete EDID document.");
            }
        }

        private static void RequireAbsentOverride(ResolvedMonitorTarget target)
        {
            if (target == null || target.ReadOverride() != null)
            {
                throw new InvalidOperationException(
                    "A pre-existing EDID override was found. MacBook Eco will not "
                        + "merge or overwrite it.");
            }
        }

        private static void ValidateExperimentalSourceDocument(
            HardwareSnapshot hardware,
            ResolvedMonitorTarget target)
        {
            if (hardware == null ||
                hardware.NormalizedSourceEdidSignature == null ||
                target == null ||
                !EdidBaseBlock.HasValidCompleteDocument(target.SourceEdid) ||
                !hardware.NormalizedSourceEdidSignature.Equals(
                    EdidBaseBlock.ComputeNormalizedDocumentSignature(
                        target.SourceEdid)))
            {
                throw new SecureStateConflictException(
                    "Discovery and SetupAPI do not expose the same valid complete EDID document.");
            }
        }

        private static void ValidateCompiledExperimentalDocument(
            DisplayProfile profile,
            ResolvedMonitorTarget target,
            byte[] expectedOverride)
        {
            if (profile == null || !profile.IsExperimental)
            {
                return;
            }

            byte[] sourceDocument = target == null ? null : target.SourceEdid;
            if (!EdidBaseBlock
                    .HasValidCompleteDocumentWithReplacementBase(
                        sourceDocument,
                        expectedOverride))
            {
                throw new SecureStateConflictException(
                    "The compiled experimental override is not a valid complete EDID.");
            }
        }

        private static void ValidateRecoverySourceDocument(
            EdidJournal journal,
            ResolvedMonitorTarget target)
        {
            if (!IsExperimental(journal))
            {
                return;
            }

            if (journal.Payload.SourceEdidSignature == null || target == null)
            {
                throw new SecureStateConflictException(
                    "The experimental journal lacks its source EDID identity.");
            }

            byte[] sourceEdid = target.SourceEdid;
            if (EdidBaseBlock.HasValidCompleteDocument(sourceEdid))
            {
                if (!journal.Payload.SourceEdidSignature.Equals(
                        EdidBaseBlock.ComputeNormalizedDocumentSignature(
                            sourceEdid)))
                {
                    throw new SecureStateConflictException(
                        "The complete source EDID no longer matches the experimental journal.");
                }

                return;
            }

            // Windows may replace the devnode EDID view with the exact owned
            // 128-byte base after reboot. Only that already-owned identity is
            // a safe fallback for recovery; it never authorizes a fresh write.
            if (sourceEdid == null ||
                sourceEdid.Length != EdidBaseBlock.Length ||
                !target.BaseEdidHash.Equals(
                    journal.Payload.OwnedOverrideHash))
            {
                throw new SecureStateConflictException(
                    "The complete source EDID cannot be re-proven for recovery.");
            }
        }

        private static void ValidateInstallPreconditions(
            WindowsHardwareSnapshot snapshot,
            HardwareSnapshot hardware,
            DisplayProfile profile)
        {
            ValidateInstallHardware(hardware, profile);
            ValidateInstallMutationPreconditions(snapshot, hardware, profile);
        }

        private static void ValidateInstallHardware(
            HardwareSnapshot hardware,
            DisplayProfile profile)
        {
            if (profile == null)
            {
                throw new NotSupportedException(
                    "No reviewed or experimental display profile matches this "
                        + "Mac, panel, EDID and controlling adapter.");
            }

            DisplayProfileMatch match = profile.Match(hardware);
            if (!match.HardwareSupported)
            {
                throw new NotSupportedException(
                    "The discovered hardware no longer matches the selected "
                        + "display profile.");
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

            if (profile.IsExperimental &&
                !snapshot.InternalDisplay.ExistingEdidOverrideReadSucceeded)
            {
                throw new InvalidOperationException(
                    "The current EDID override state could not be read safely.");
            }

            if (profile.IsExperimental &&
                snapshot.InternalDisplay.ExistingEdidOverride != null)
            {
                throw new InvalidOperationException(
                    "A pre-existing EDID override was found. MacBook Eco will not "
                        + "merge or overwrite it.");
            }

            if (snapshot.CurrentDisplayMode == null ||
                snapshot.CurrentDisplayMode.RefreshRate != 60)
            {
                throw new InvalidOperationException(
                    "Installation requires the current desktop mode to be at 60 Hz.");
            }

            if (profile.IsExperimental && !hardware.CompleteEdidIsValid)
            {
                throw new InvalidOperationException(
                    "The complete EDID document is not valid for an experimental install.");
            }

            if (profile.IsExperimental &&
                (hardware.NormalizedSourceEdidSignature == null ||
                 !hardware.NormalizedSourceEdidSignature.Equals(
                     profile.SourceEdidSignature)))
            {
                throw new InvalidOperationException(
                    "The complete source EDID no longer matches the experimental profile.");
            }

            if (profile.IsExperimental &&
                hardware.Edid.ContainsDetailedTiming(profile.TargetTiming))
            {
                throw new InvalidOperationException(
                    "The generated target already exists in the base EDID, so "
                        + "MacBook Eco cannot establish fresh ownership.");
            }

            DisplayProfileMatch match = profile.Match(hardware);
            if (!match.CanInstall)
            {
                throw new InvalidOperationException(
                    "The supported panel has no free EDID descriptor slot for the owned override.");
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
                snapshot.InternalDisplay.Endpoint == null ||
                target.Endpoint == null ||
                !snapshot.InternalDisplay.Endpoint.Equals(target.Endpoint) ||
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
            ResolvedMonitorTarget target)
        {
            if (profile == null || target == null)
            {
                throw new SecureStateConflictException(
                    "The restore profile or monitor target is unavailable.");
            }

            EdidBaseBlock baseEdid = new EdidBaseBlock(target.BaseEdid);
            if (!string.Equals(
                    profile.PanelHardwareId,
                    target.HardwareId,
                    StringComparison.Ordinal) ||
                !profile.NormalizedEdidSignature.Equals(
                    baseEdid.NormalizedSignature) ||
                !profile.NativeTiming.Equals(baseEdid.PreferredTiming))
            {
                throw new SecureStateConflictException(
                    "The stored monitor identity no longer matches the compiled display profile.");
            }
        }

        private static byte[] CompileOwnedOverride(
            DisplayProfile profile,
            ResolvedMonitorTarget target)
        {
            if (profile == null || target == null)
                throw new ArgumentNullException(profile == null ? "profile" : "target");

            EdidBaseBlock baseEdid = new EdidBaseBlock(target.BaseEdid);
            return profile.CompileOverride(baseEdid).ToByteArray();
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
                    IsExperimental(journal)
                        ? "The experimental local 48 Hz descriptor was installed. "
                            + "A display-adapter reload or reboot is required before "
                            + "the mode appears."
                        : "The reviewed 48 Hz descriptor was installed. "
                            + "A display-adapter reload or reboot is required before "
                            + "the mode appears.",
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

        private static DisplayProfile ResolveActiveProfile(
            string profileId,
            HardwareSnapshot hardware,
            Sha256Digest persistedSourceEdidSignature)
        {
            DisplayProfile reviewed = ProfileCatalog.GetById(profileId);
            if (reviewed != null)
            {
                return reviewed;
            }

            if (hardware == null || !string.Equals(
                    hardware.SystemManufacturer,
                    "Apple Inc.",
                    StringComparison.Ordinal))
            {
                throw new SecureStateConflictException(
                    "The experimental profile cannot re-prove exact Apple SMBIOS identity.");
            }

            ExperimentalProfileGenerationResult generated =
                Experimental48HzProfileGenerator.ResolveForRecovery(
                    profileId,
                    hardware.SystemModel,
                    hardware.PanelHardwareId,
                    hardware.Edid,
                    persistedSourceEdidSignature);
            if (!generated.Succeeded)
            {
                throw new SecureStateConflictException(
                    "The journaled experimental profile could not be regenerated.");
            }

            return generated.Profile;
        }

        private static DisplayProfile ResolveRestoreProfile(
            string profileId,
            ResolvedMonitorTarget target,
            Sha256Digest sourceEdidSignature)
        {
            DisplayProfile reviewed = ProfileCatalog.GetById(profileId);
            if (reviewed != null)
            {
                return reviewed;
            }

            SmbiosIdentity identity = SmbiosReader.ReadIdentity();
            if (identity == null || !string.Equals(
                    identity.Manufacturer,
                    "Apple Inc.",
                    StringComparison.Ordinal))
            {
                throw new SecureStateConflictException(
                    "The experimental profile cannot re-prove exact Apple SMBIOS identity.");
            }

            ExperimentalProfileGenerationResult generated =
                Experimental48HzProfileGenerator.ResolveForRecovery(
                    profileId,
                    identity.ProductName,
                    target.HardwareId,
                    new EdidBaseBlock(target.BaseEdid),
                    sourceEdidSignature);
            if (!generated.Succeeded)
            {
                throw new SecureStateConflictException(
                    "The journaled experimental profile could not be regenerated.");
            }

            return generated.Profile;
        }

        private static bool IsExperimental(EdidJournal journal)
        {
            return journal != null && journal.Payload != null &&
                journal.Payload.Target != null &&
                Experimental48HzProfileGenerator.IsExperimentalProfileId(
                    journal.Payload.Target.ProfileId);
        }

        private static void ValidateInstallCommandForJournal(
            EdidJournal journal,
            DisplayProfileKind expectedKind,
            string acknowledgementToken)
        {
            if (journal == null ||
                journal.State == EdidJournalState.NotInstalled ||
                journal.State == EdidJournalState.Restored)
            {
                return;
            }

            RequirePayload(journal);
            bool experimental = IsExperimental(journal);
            if (experimental !=
                (expectedKind == DisplayProfileKind.Experimental))
            {
                throw new NotSupportedException(
                    "The fixed Admin command does not match the journaled "
                        + "profile kind.");
            }

            if (experimental)
            {
                RequireExperimentalAcknowledgement(
                    journal.Payload.Target.ProfileId,
                    acknowledgementToken);
            }
        }

        private static void RequireExperimentalAcknowledgement(
            string profileId,
            string acknowledgementToken)
        {
            if (!Experimental48HzProfileGenerator
                    .AcknowledgementTokenMatches(
                        profileId,
                        acknowledgementToken))
            {
                throw new NotSupportedException(
                    "The acknowledged experimental profile does not match "
                        + "the freshly proven candidate.");
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
                DisplayProfile profile)
            {
                Target = target;
                ExpectedOverride = expectedOverride;
                Snapshot = snapshot;
                Hardware = hardware;
                Profile = profile;
            }

            internal ResolvedMonitorTarget Target { get; private set; }

            internal byte[] ExpectedOverride { get; private set; }

            internal WindowsHardwareSnapshot Snapshot { get; private set; }

            internal HardwareSnapshot Hardware { get; private set; }

            internal DisplayProfile Profile { get; private set; }
        }

        private sealed class RestoreContext
        {
            internal RestoreContext(
                ResolvedMonitorTarget target,
                byte[] expectedOverride)
            {
                Target = target;
                ExpectedOverride = expectedOverride;
            }

            internal ResolvedMonitorTarget Target { get; private set; }

            internal byte[] ExpectedOverride { get; private set; }
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
