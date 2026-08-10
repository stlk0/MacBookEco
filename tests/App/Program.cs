using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using MacBookEco.App;
using MacBookEco.AppPolicy;
using MacBookEco.Core;
using MacBookEco.Platform.Windows;
using MacBookEco.Telemetry;

namespace MacBookEco.Tests.App
{
    internal static class Program
    {
        private const string ExperimentalProfileId =
            "exp48-v1-mbp161-7340-" +
            "0d248c3f0d1f6bea6b207f50f9e7cd669e518ac7dfe22c9184ad04801080b8da";
        private const string AlternateExperimentalProfileId =
            "exp48-v1-mbp161-7340-" +
            "1d248c3f0d1f6bea6b207f50f9e7cd669e518ac7dfe22c9184ad04801080b8da";

        private static int Main()
        {
            List<TestCase> tests = new List<TestCase>
            {
                Test("Global command gate rejects a concurrent mutation",
                    TestGlobalSingleFlightReturnsBusy),
                Test("Timeout remains indeterminate until late read-back",
                    TestTimeoutIsIndeterminateUntilLateReadBack),
                Test("Combined profile stops after display failure",
                    TestCombinedProfileIsSequentialAndStopsAfterDisplayFailure),
                Test("Combined profile runs display before CPU",
                    TestCombinedProfileRunsDisplayBeforeCpu),
                Test("Time-series buffer retains gaps in chronological order",
                    TestTimeSeriesBufferRetainsGapsAndChronologicalOrder),
                Test("Time-series buffer rejects republished samples",
                    TestTimeSeriesBufferRejectsRepublishedSample),
                Test("Time-series statistics ignore gaps outside the window",
                    TestTimeSeriesStatisticsIgnoreGapsOutsideWindow),
                Test("Time-series axis remains finite and honors bounds",
                    TestTimeSeriesAxisRangeIsFiniteAndHonorsBounds),
                Test("Metric-card status and accessibility stay consistent",
                    TestMetricCardPresentationMapsStatusAndAccessibility),
                Test("Display confirmation countdown honors its deadline",
                    TestDisplayConfirmationCountdownBoundary),
                Test("Experimental refresh binds the journal to the active panel",
                    TestExperimentalRefreshRequiresJournalTarget),
                Test("Install command preserves experimental risk acknowledgement",
                    TestInstallCommandPreservesRiskAcknowledgement),
                Test("Display support UI exposes only safe actions",
                    TestDisplaySupportUiPolicy),
                Test("Startup command selects background mode",
                    TestStartupCommandUsesBackgroundMode),
                Test("CPU hardware support policy fails closed",
                    TestCpuHardwareSupportPolicy),
                Test("Uninstall safety policy maps resource states",
                    TestUninstallSafetyPolicy),
                Test("Uninstall recovery restores display before power",
                    TestUninstallRecoveryOrder),
                Test("Uninstall recovery leaves safe state unchanged",
                    TestUninstallRecoverySkipsSafeState),
                Test("Uninstall recovery repairs exact-owned display state",
                    TestUninstallRecoveryRepairsOwnedConflict),
                Test("Uninstall recovery stops when repair is refused",
                    TestUninstallRecoveryStopsAfterRepairFailure),
                Test("Action service stops when its helper does not succeed",
                    TestActionServiceStopsAfterHelperFailure),
                Test("Action service verifies display-support read-back",
                    TestActionServiceVerifiesDisplaySupportReadBack),
                Test("Action service enforces experimental install acknowledgement",
                    TestActionServiceEnforcesExperimentalAcknowledgement),
                Test("Action state exposes reviewed IDs but hides generated IDs",
                    TestActionStateProjectsSafeProfileIdentity),
                Test("Action service maps and verifies every CPU preset",
                    TestActionServiceMapsAndVerifiesCpuPresets),
                Test("Unsupported CPU hardware never reaches the helper",
                    TestActionServiceRejectsUnsupportedCpuHardware),
                Test("Power restore requires the original scheme to be active",
                    TestActionServiceVerifiesPowerRestore),
                Test("Display refresh delegates the confirmation callback",
                    TestActionServiceDelegatesDisplayConfirmation),
                Test("Unverified startup recovery blocks every display mutation",
                    TestActionServiceBlocksDisplayAfterStartupRecovery),
                Test("Startup recovery ignores an empty session set",
                    TestStartupRecoveryHandlesEmptyAndMissingHelper),
                Test("Startup recovery cleans every verified session",
                    TestStartupRecoveryCleansVerifiedSessions),
                Test("Startup recovery retains partial failures",
                    TestStartupRecoveryRetainsPartialFailures),
                Test("Startup recovery enforces its total time budget",
                    TestStartupRecoveryEnforcesTotalBudget),
                Test("Startup recovery classifies every process boundary",
                    TestStartupRecoveryClassifiesProcessBoundaries),
                Test("Startup recovery fails closed on verifier errors",
                    TestStartupRecoveryFailsClosedOnVerifierError),
                Test("Display lease persists only the confirmed target",
                    TestDisplayLeaseConfirmsWithoutRollback),
                Test("Display lease rolls back once on disposal",
                    TestDisplayLeaseRollsBackOnDispose),
                Test("Display lease remains recoverable after native failures",
                    TestDisplayLeaseRetriesAfterNativeFailures),
                Test("Display rollback verifies the exact original mode",
                    TestDisplayRollbackVerifiesOriginalMode),
                Test("Display rollback forces persistence after commit attempt",
                    TestDisplayRollbackForcesOriginalPersistence),
                Test("Admin helper retains late child reconciliation",
                    TestAdminHelperWaitsForTerminalExit),
                Test("Admin helper exposes only fixed commands",
                    TestAdminHelperFixedArguments),
                Test("Admin helper maps every exit-code category",
                    TestAdminHelperExitCodeMappings),
                Test("Display persistence preserves watchdog ordering",
                    TestDisplayPersistencePreservesWatchdogOrdering),
                Test("Confirmed display transition verifies every outcome",
                    TestConfirmedDisplayTransitionOutcomes),
                Test("Public diagnostics omit machine-specific free-form data",
                    TestPublicDiagnosticsOmitPrivateData),
                Test("Profiles render before the first telemetry sample",
                    TestProfilesControllerRendersBeforeFirstTelemetrySample)
            };

            tests.AddRange(MacBookEco.Tests.Core.TestRunner.CreateCases());
            tests.AddRange(MacBookEco.Tests.Security.JournalCodecTests.CreateCases());
            tests.AddRange(MacBookEco.Tests.Smoke.Program.CreateCases());

            return TestSuite.Run("MacBookEco host-safe behavior tests", tests);
        }

        private static TestCase Test(string name, Action body)
        {
            return new TestCase(name, body);
        }

        private static void TestPublicDiagnosticsOmitPrivateData()
        {
            const string PrivateMarker =
                "MONITOR\\APPA044\\SERIAL-EXACT-EDID-PRIVATE";
            TelemetrySnapshot snapshot = new TelemetrySnapshot(
                new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc),
                new BatteryTelemetry(
                    TelemetryAvailability.Available,
                    false,
                    false,
                    75.0,
                    PrivateMarker,
                    PrivateMarker,
                    null,
                    9.5),
                new CpuTelemetry(
                    TelemetryAvailability.Error,
                    null,
                    null,
                    null,
                    null,
                    null,
                    PrivateMarker,
                    PrivateMarker),
                new DisplayTelemetry(
                    TelemetryAvailability.Available,
                    PrivateMarker,
                    3072,
                    1920,
                    60.0,
                    PrivateMarker,
                    "Internal",
                    "NORMALIZED-PANEL-SIGNATURE"),
                new GpuTelemetry(
                    TelemetryAvailability.Error,
                    PrivateMarker,
                    null,
                    null,
                    null,
                    null,
                    null,
                    PrivateMarker,
                    PrivateMarker),
                true);
            OptimizationStateSnapshot state = new OptimizationStateSnapshot(
                false,
                false,
                null,
                PrivateMarker,
                PrivateMarker,
                PrivateMarker,
                PrivateMarker);
            OptimizationActionResult action = OptimizationActionResult.Failed(
                OperationCode.UnhandledException,
                PrivateMarker,
                PrivateMarker);

            string report = DashboardDiagnosticsPage.BuildPublicDiagnostics(
                snapshot,
                state,
                action);

            Check.That(
                report.IndexOf(PrivateMarker, StringComparison.Ordinal) < 0,
                "public diagnostics exported a free-form private marker");
            Check.That(
                report.Contains(
                    "normalized signature NORMALIZED-PANEL-SIGNATURE"),
                "public diagnostics omitted the normalized panel signature");
            Check.That(
                report.Contains("Code: UnhandledException"),
                "public diagnostics omitted the stable operation code");
            Check.That(
                report.Contains("48 Hz mode exposed by Windows: False"),
                "public diagnostics omitted live 48 Hz readiness");
            Check.That(
                !report.Contains("Message:") && !report.Contains("Detail:"),
                "public diagnostics exposed free-form action fields");
        }

        private static void TestStartupCommandUsesBackgroundMode()
        {
            string expected = "\"" + Application.ExecutablePath
                + "\" --background";
            Check.That(
                string.Equals(
                    StartupRegistration.BuildCommand(),
                    expected,
                    StringComparison.Ordinal),
                "Windows startup must use the exact executable in background mode");
        }

        private static void TestCpuHardwareSupportPolicy()
        {
            Check.That(CpuHardwareSupportPolicy.IsSupported(
                    "apple inc.",
                    "macbookpro16,1"),
                "the reviewed SMBIOS identity must be supported");
            Check.That(CpuHardwareSupportPolicy.Classify(null, "MacBookPro16,1") ==
                    CpuHardwareSupportStatus.IdentityUnavailable,
                "missing SMBIOS data must fail closed as unavailable");
            Check.That(CpuHardwareSupportPolicy.Classify(
                    "Apple Inc.",
                    "MacBookPro16,4") == CpuHardwareSupportStatus.Unsupported,
                "a different Mac model must not inherit CPU-preset support");
        }

        private static void TestUninstallSafetyPolicy()
        {
            Check.That(UninstallSafetyPolicy.GetExitCode(
                    UninstallSafetyState.Safe,
                    UninstallSafetyState.Safe) == 0,
                "only two safe resource states may permit ordinary uninstall");
            Check.That(UninstallSafetyPolicy.GetExitCode(
                    UninstallSafetyState.RecoveryRequired,
                    UninstallSafetyState.Safe) == 1,
                "recoverable state must block ordinary uninstall");
            Check.That(UninstallSafetyPolicy.GetExitCode(
                    UninstallSafetyState.Unknown,
                    UninstallSafetyState.Safe) == 2,
                "unknown status must require an explicit uninstall decision");
        }

        private static void TestUninstallRecoveryOrder()
        {
            UninstallActions actions = new UninstallActions();
            actions.States.Enqueue(UninstallState("Installed", "Installed"));
            actions.States.Enqueue(UninstallState("Restored", "Installed"));
            actions.States.Enqueue(UninstallState("Restored", "Restored"));

            OptimizationActionResult result =
                new UninstallRecoveryCoordinator(actions).Recover(
                    delegate { return DisplayModeConfirmationDecision.Keep; });

            Check.That(result.Succeeded && result.RestartRequired,
                "verified display removal must allow uninstall and request restart");
            Check.That(
                string.Join(",", actions.CallOrder)
                    == "set-60,remove-display,restore-power",
                "uninstall recovery must restore display before power");
        }

        private static void TestUninstallRecoverySkipsSafeState()
        {
            UninstallActions actions = new UninstallActions();
            actions.States.Enqueue(UninstallState("Restored", "Restored"));
            actions.States.Enqueue(UninstallState("Restored", "Restored"));
            actions.States.Enqueue(UninstallState("Restored", "Restored"));

            OptimizationActionResult result =
                new UninstallRecoveryCoordinator(actions).Recover(null);

            Check.That(result.Succeeded && !result.RestartRequired,
                "already restored resources must permit uninstall without restart");
            Check.That(actions.CallOrder.Count == 0,
                "already restored resources must remain read-only");
        }

        private static void TestUninstallRecoveryRepairsOwnedConflict()
        {
            UninstallActions actions = new UninstallActions();
            actions.States.Enqueue(UninstallState("Conflict", "Restored"));
            actions.States.Enqueue(UninstallState("Restored", "Restored"));
            actions.States.Enqueue(UninstallState("Restored", "Restored"));

            OptimizationActionResult result =
                new UninstallRecoveryCoordinator(actions).Recover(
                    delegate { return DisplayModeConfirmationDecision.Keep; });

            Check.That(result.Succeeded && result.RestartRequired,
                "an exact-owned conflict may be repaired and then removed");
            Check.That(
                string.Join(",", actions.CallOrder)
                    == "repair-display,set-60,remove-display",
                "display repair must precede native mode and removal");
        }

        private static void TestUninstallRecoveryStopsAfterRepairFailure()
        {
            UninstallActions actions = new UninstallActions();
            actions.States.Enqueue(UninstallState("Conflict", "Installed"));
            actions.InstallResult = OptimizationActionResult.Failed(
                OperationCode.HelperFailed,
                "Foreign state was preserved.",
                string.Empty);

            OptimizationActionResult result =
                new UninstallRecoveryCoordinator(actions).Recover(null);

            Check.That(!result.Succeeded,
                "an unverified display conflict must keep uninstall blocked");
            Check.That(
                string.Join(",", actions.CallOrder) == "repair-display",
                "recovery must stop before later mutations after repair fails");
        }

        private static OptimizationStateSnapshot UninstallState(
            string display,
            string power)
        {
            return new OptimizationStateSnapshot(
                true,
                false,
                null,
                power,
                display,
                string.Empty,
                string.Empty);
        }

        private static void TestActionServiceStopsAfterHelperFailure()
        {
            Func<WindowsOptimizationActionService, OptimizationActionResult>[]
                operations =
                {
                    delegate(WindowsOptimizationActionService service)
                    {
                        return service.InstallDisplaySupport();
                    },
                    delegate(WindowsOptimizationActionService service)
                    {
                        return service.RemoveDisplaySupport();
                    },
                    delegate(WindowsOptimizationActionService service)
                    {
                        return service.ApplyCpuPreset(PowerPreset.Cool);
                    },
                    delegate(WindowsOptimizationActionService service)
                    {
                        return service.RestoreCpuPower();
                    }
                };
            AdminCommand[] expectedCommands =
            {
                AdminCommand.InstallDisplay,
                AdminCommand.RemoveDisplay,
                AdminCommand.ApplyPowerCool,
                AdminCommand.RestorePower
            };

            for (int index = 0; index < operations.Length; index++)
            {
                ActionServiceHarness harness = new ActionServiceHarness();
                harness.HelperResult = OptimizationActionResult.Indeterminate(
                    OperationCode.HelperIndeterminate,
                    "helper stopped at an indeterminate boundary",
                    "test");

                OptimizationActionResult result = operations[index](
                    harness.Service);

                Check.Equal(OperationOutcome.Indeterminate, result.Outcome);
                Check.Equal(OperationCode.HelperIndeterminate, result.Code);
                Check.Equal(1, harness.AdminCommands.Count);
                Check.Equal(expectedCommands[index], harness.AdminCommands[0]);
                Check.That(
                    harness.AdminAcknowledgementTokens[0] == null,
                    "A nonexperimental helper command carried a token.");
                Check.Equal(index == 0 ? 1 : 0, harness.DisplayStatusReads);
                Check.Equal(index == 0 ? 1 : 0, harness.DisplayCandidateReads);
                Check.Equal(0, harness.PowerStatusReads);
            }
        }

        private static void TestActionServiceVerifiesDisplaySupportReadBack()
        {
            ActionServiceHarness harness = new ActionServiceHarness();
            harness.DisplayStatus = new DisplayOverrideStatus
            {
                State = ManagedResourceState.Installed,
                ProfileId = "apple-internal-48"
            };

            OptimizationActionResult installed =
                harness.Service.InstallDisplaySupport();

            Check.Equal(OperationOutcome.Succeeded, installed.Outcome);
            Check.True(installed.RestartRequired);
            Check.Equal(2, harness.DisplayStatusReads);

            OptimizationStateSnapshot pendingRestart =
                harness.Service.ReadState();
            Check.True(!pendingRestart.Display48HzAvailable);
            harness.Display48HzAvailable = true;
            OptimizationStateSnapshot ready = harness.Service.ReadState();
            Check.True(ready.Display48HzAvailable);

            harness.DisplayStatus = new DisplayOverrideStatus
            {
                State = ManagedResourceState.RecoveryRequired
            };
            OptimizationActionResult mismatched =
                harness.Service.InstallDisplaySupport();
            Check.Equal(OperationOutcome.Failed, mismatched.Outcome);
            Check.Equal(OperationCode.StateVerificationFailed, mismatched.Code);

            harness.DisplayStatus = new DisplayOverrideStatus
            {
                State = ManagedResourceState.Restored
            };
            OptimizationActionResult restored =
                harness.Service.RemoveDisplaySupport();
            Check.Equal(OperationOutcome.Succeeded, restored.Outcome);
            Check.True(restored.RestartRequired);

            harness.DisplayStatusException = new InvalidOperationException(
                "read-back unavailable");
            OptimizationActionResult faulted =
                harness.Service.InstallDisplaySupport();
            Check.Equal(OperationOutcome.Failed, faulted.Outcome);
            Check.Equal(OperationCode.UnhandledException, faulted.Code);
        }

        private static void TestActionServiceEnforcesExperimentalAcknowledgement()
        {
            ActionServiceHarness declined = new ActionServiceHarness();
            declined.DisplayStatus = new DisplayOverrideStatus
            {
                State = ManagedResourceState.NotInstalled
            };
            declined.DisplayCandidate = CandidateStatus(true);

            OptimizationActionResult cancelled =
                declined.Service.InstallDisplaySupport();

            Check.Equal(OperationOutcome.Cancelled, cancelled.Outcome);
            Check.Equal(OperationCode.UserCancelled, cancelled.Code);
            Check.Equal(0, declined.AdminCommands.Count);
            Check.Equal(1, declined.DisplayCandidateReads);

            ActionServiceHarness accepted = new ActionServiceHarness();
            accepted.DisplayCandidate = CandidateStatus(true);
            accepted.DisplayStatuses.Enqueue(new DisplayOverrideStatus
            {
                State = ManagedResourceState.NotInstalled
            });
            accepted.DisplayStatuses.Enqueue(new DisplayOverrideStatus
            {
                State = ManagedResourceState.Installed,
                ProfileId = ExperimentalProfileId,
                ExperimentalProfile = true
            });

            OptimizationActionResult installed =
                accepted.Service.InstallDisplaySupport(
                    Experimental48HzProfileGenerator
                        .CreateAcknowledgementToken(ExperimentalProfileId));

            Check.Equal(OperationOutcome.Succeeded, installed.Outcome);
            Check.Equal(1, accepted.AdminCommands.Count);
            Check.Equal(
                AdminCommand.InstallExperimentalDisplay,
                accepted.AdminCommands[0]);
            Check.Equal(
                Experimental48HzProfileGenerator.CreateAcknowledgementToken(
                    ExperimentalProfileId),
                accepted.AdminAcknowledgementTokens[0]);
            Check.Equal(1, accepted.DisplayCandidateReads);

            ActionServiceHarness changedReadBack = new ActionServiceHarness();
            changedReadBack.DisplayCandidate = CandidateStatus(true);
            changedReadBack.DisplayStatuses.Enqueue(new DisplayOverrideStatus
            {
                State = ManagedResourceState.NotInstalled
            });
            changedReadBack.DisplayStatuses.Enqueue(new DisplayOverrideStatus
            {
                State = ManagedResourceState.Installed,
                ProfileId = AlternateExperimentalProfileId,
                ExperimentalProfile = true
            });
            OptimizationActionResult changedReadBackResult =
                changedReadBack.Service.InstallDisplaySupport(
                    Experimental48HzProfileGenerator
                        .CreateAcknowledgementToken(ExperimentalProfileId));
            Check.Equal(OperationOutcome.Failed, changedReadBackResult.Outcome);
            Check.Equal(
                OperationCode.StateVerificationFailed,
                changedReadBackResult.Code);
            Check.Equal(1, changedReadBack.AdminCommands.Count);

            ActionServiceHarness changedCandidate = new ActionServiceHarness();
            changedCandidate.DisplayStatus = new DisplayOverrideStatus
            {
                State = ManagedResourceState.NotInstalled
            };
            changedCandidate.DisplayCandidate = CandidateStatus(true);
            changedCandidate.DisplayCandidate.ExperimentalAcknowledgementToken =
                Experimental48HzProfileGenerator.CreateAcknowledgementToken(
                    AlternateExperimentalProfileId);
            OptimizationActionResult changedResult =
                changedCandidate.Service.InstallDisplaySupport(
                    Experimental48HzProfileGenerator
                        .CreateAcknowledgementToken(ExperimentalProfileId));
            Check.Equal(OperationOutcome.Cancelled, changedResult.Outcome);
            Check.Equal(0, changedCandidate.AdminCommands.Count);

            ActionServiceHarness changedToReviewed = new ActionServiceHarness();
            changedToReviewed.DisplayStatus = new DisplayOverrideStatus
            {
                State = ManagedResourceState.NotInstalled
            };
            changedToReviewed.DisplayCandidate = CandidateStatus(false);
            OptimizationActionResult changedToReviewedResult =
                changedToReviewed.Service.InstallDisplaySupport(
                    Experimental48HzProfileGenerator
                        .CreateAcknowledgementToken(ExperimentalProfileId));
            Check.Equal(
                OperationOutcome.Cancelled,
                changedToReviewedResult.Outcome);
            Check.Equal(0, changedToReviewed.AdminCommands.Count);

            ActionServiceHarness changedToOwned = new ActionServiceHarness();
            changedToOwned.DisplayStatus = new DisplayOverrideStatus
            {
                State = ManagedResourceState.Conflict,
                ProfileId = AlternateExperimentalProfileId,
                ExperimentalProfile = true
            };
            OptimizationActionResult changedToOwnedResult =
                changedToOwned.Service.InstallDisplaySupport(
                    Experimental48HzProfileGenerator
                        .CreateAcknowledgementToken(ExperimentalProfileId));
            Check.Equal(
                OperationOutcome.Cancelled,
                changedToOwnedResult.Outcome);
            Check.Equal(0, changedToOwned.AdminCommands.Count);

            ActionServiceHarness reviewed = new ActionServiceHarness();
            reviewed.DisplayCandidate = CandidateStatus(false);
            reviewed.DisplayStatuses.Enqueue(new DisplayOverrideStatus
            {
                State = ManagedResourceState.NotInstalled
            });
            reviewed.DisplayStatuses.Enqueue(new DisplayOverrideStatus
            {
                State = ManagedResourceState.Installed,
                ProfileId = "reviewed-static-profile"
            });

            OptimizationActionResult reviewedInstall =
                reviewed.Service.InstallDisplaySupport();

            Check.Equal(OperationOutcome.Succeeded, reviewedInstall.Outcome);
            Check.Equal(1, reviewed.AdminCommands.Count);
            Check.Equal(AdminCommand.InstallDisplay, reviewed.AdminCommands[0]);

            ActionServiceHarness repair = new ActionServiceHarness();
            repair.DisplayCandidate = CandidateStatus(true);
            repair.DisplayStatuses.Enqueue(new DisplayOverrideStatus
            {
                State = ManagedResourceState.Conflict,
                ProfileId = ExperimentalProfileId,
                ExperimentalProfile = true
            });
            repair.DisplayStatuses.Enqueue(new DisplayOverrideStatus
            {
                State = ManagedResourceState.Installed,
                ProfileId = ExperimentalProfileId,
                ExperimentalProfile = true
            });

            OptimizationActionResult repaired =
                repair.Service.InstallDisplaySupport();

            Check.Equal(OperationOutcome.Succeeded, repaired.Outcome);
            Check.Equal(1, repair.AdminCommands.Count);
            Check.Equal(
                AdminCommand.InstallExperimentalDisplay,
                repair.AdminCommands[0]);
            Check.Equal(
                Experimental48HzProfileGenerator.CreateAcknowledgementToken(
                    ExperimentalProfileId),
                repair.AdminAcknowledgementTokens[0]);
            Check.Equal(0, repair.DisplayCandidateReads);
        }

        private static DisplayProfileCandidateStatus CandidateStatus(
            bool experimental)
        {
            return new DisplayProfileCandidateStatus
            {
                Available = true,
                EligibleForInstall = true,
                Experimental = experimental,
                ReviewedProfileId = experimental
                    ? string.Empty
                    : ProfileCatalog.MacBookPro161Appa044ProfileId,
                ExperimentalAcknowledgementToken = experimental
                    ? Experimental48HzProfileGenerator
                        .CreateAcknowledgementToken(ExperimentalProfileId)
                    : null,
                SafeSummary = experimental
                    ? "Experimental candidate summary."
                    : "Reviewed candidate summary."
            };
        }

        private static void TestActionStateProjectsSafeProfileIdentity()
        {
            ActionServiceHarness reviewed = new ActionServiceHarness();
            reviewed.DisplayStatus = new DisplayOverrideStatus
            {
                State = ManagedResourceState.NotInstalled
            };
            reviewed.DisplayCandidate = CandidateStatus(false);

            OptimizationStateSnapshot reviewedState =
                reviewed.Service.ReadState();
            Check.Equal(
                ProfileCatalog.MacBookPro161Appa044ProfileId,
                reviewedState.DisplayProfileId);
            Check.False(reviewedState.DisplayProfileExperimental);

            ActionServiceHarness experimental = new ActionServiceHarness();
            experimental.DisplayStatus = new DisplayOverrideStatus
            {
                State = ManagedResourceState.NotInstalled
            };
            experimental.DisplayCandidate = CandidateStatus(true);

            OptimizationStateSnapshot experimentalState =
                experimental.Service.ReadState();
            Check.True(experimentalState.DisplayProfileExperimental);
            Check.Equal(string.Empty, experimentalState.DisplayProfileId);
            Check.Equal(
                experimental.DisplayCandidate
                    .ExperimentalAcknowledgementToken,
                experimentalState.DisplayCandidateAcknowledgementToken);
            string freshDiagnostics =
                DashboardDiagnosticsPage.BuildPublicDiagnostics(
                    TelemetrySnapshot.Empty(),
                    experimentalState,
                    null);
            Check.That(
                freshDiagnostics.IndexOf(
                    experimentalState.DisplayCandidateAcknowledgementToken,
                    StringComparison.Ordinal) < 0,
                "Public diagnostics exposed a fresh consent token.");

            ActionServiceHarness installedExperimental =
                new ActionServiceHarness();
            installedExperimental.DisplayStatus = new DisplayOverrideStatus
            {
                State = ManagedResourceState.Installed,
                ProfileId = ExperimentalProfileId,
                ExperimentalProfile = true
            };
            installedExperimental.DisplayCandidate = CandidateStatus(true);
            OptimizationStateSnapshot installedExperimentalState =
                installedExperimental.Service.ReadState();
            Check.Equal(
                "Experimental local 48 Hz candidate",
                installedExperimentalState.DisplayProfileId);
            string diagnostics =
                DashboardDiagnosticsPage.BuildPublicDiagnostics(
                    TelemetrySnapshot.Empty(),
                    installedExperimentalState,
                    null);
            Check.That(
                diagnostics.IndexOf(
                    ExperimentalProfileId,
                    StringComparison.Ordinal) < 0,
                "Public diagnostics exposed a generated profile ID.");
            Check.That(
                diagnostics.IndexOf(
                    Experimental48HzProfileGenerator
                        .CreateAcknowledgementToken(ExperimentalProfileId),
                    StringComparison.Ordinal) < 0,
                "Public diagnostics exposed an acknowledgement token.");

            ActionServiceHarness restoredExperimental =
                new ActionServiceHarness();
            restoredExperimental.DisplayStatus = new DisplayOverrideStatus
            {
                State = ManagedResourceState.Restored,
                ProfileId = ExperimentalProfileId,
                ExperimentalProfile = true
            };
            restoredExperimental.DisplayCandidate =
                new DisplayProfileCandidateStatus();
            OptimizationStateSnapshot restoredExperimentalState =
                restoredExperimental.Service.ReadState();
            Check.Equal(string.Empty, restoredExperimentalState.DisplayProfileId);
            Check.False(
                restoredExperimentalState.DisplayProfileExperimental);
            Check.That(
                restoredExperimentalState
                    .DisplayCandidateAcknowledgementToken == null,
                "A stale restored profile must expose no acknowledgement token.");

            ActionServiceHarness restoredReviewed = new ActionServiceHarness();
            restoredReviewed.DisplayStatus = new DisplayOverrideStatus
            {
                State = ManagedResourceState.Restored,
                ProfileId = ProfileCatalog.MacBookPro161Appa044ProfileId
            };
            restoredReviewed.DisplayCandidate =
                new DisplayProfileCandidateStatus();
            OptimizationStateSnapshot restoredReviewedState =
                restoredReviewed.Service.ReadState();
            Check.Equal(string.Empty, restoredReviewedState.DisplayProfileId);
            Check.False(restoredReviewedState.DisplayProfileExperimental);
        }

        private static void TestActionServiceMapsAndVerifiesCpuPresets()
        {
            PowerPreset[] presets =
            {
                PowerPreset.Normal,
                PowerPreset.Cool,
                PowerPreset.MaximumBattery
            };
            AdminCommand[] commands =
            {
                AdminCommand.ApplyPowerNormal,
                AdminCommand.ApplyPowerCool,
                AdminCommand.ApplyPowerBattery
            };

            for (int index = 0; index < presets.Length; index++)
            {
                ActionServiceHarness harness = new ActionServiceHarness();
                Guid owned = Guid.NewGuid();
                harness.PowerStatus = new PowerSchemeStatus
                {
                    State = ManagedResourceState.Installed,
                    Preset = presets[index],
                    ActiveScheme = owned,
                    OwnedScheme = owned
                };

                OptimizationActionResult result =
                    harness.Service.ApplyCpuPreset(presets[index]);

                Check.Equal(OperationOutcome.Succeeded, result.Outcome);
                Check.Equal(1, harness.AdminCommands.Count);
                Check.Equal(commands[index], harness.AdminCommands[0]);
                Check.Equal(1, harness.PowerStatusReads);
            }

            ActionServiceHarness mismatch = new ActionServiceHarness();
            mismatch.PowerStatus = new PowerSchemeStatus
            {
                State = ManagedResourceState.Installed,
                Preset = PowerPreset.Normal,
                ActiveScheme = Guid.NewGuid(),
                OwnedScheme = Guid.NewGuid()
            };
            OptimizationActionResult mismatchResult =
                mismatch.Service.ApplyCpuPreset(PowerPreset.Normal);
            Check.Equal(OperationOutcome.Failed, mismatchResult.Outcome);
            Check.Equal(
                OperationCode.StateVerificationFailed,
                mismatchResult.Code);
        }

        private static void TestActionServiceRejectsUnsupportedCpuHardware()
        {
            ActionServiceHarness harness = new ActionServiceHarness(
                CpuHardwareSupportStatus.Unsupported);

            OptimizationActionResult result =
                harness.Service.ApplyCpuPreset(PowerPreset.Normal);

            Check.Equal(OperationOutcome.Unsupported, result.Outcome);
            Check.Equal(OperationCode.UnsupportedCapability, result.Code);
            Check.Equal(0, harness.AdminCommands.Count);
            Check.Equal(0, harness.PowerStatusReads);
        }

        private static void TestActionServiceVerifiesPowerRestore()
        {
            Guid original = Guid.NewGuid();
            ActionServiceHarness harness = new ActionServiceHarness();
            harness.PowerStatus = new PowerSchemeStatus
            {
                State = ManagedResourceState.Restored,
                ActiveScheme = original,
                OriginalScheme = original
            };

            OptimizationActionResult restored = harness.Service.RestoreCpuPower();
            Check.Equal(OperationOutcome.Succeeded, restored.Outcome);

            harness.PowerStatus = new PowerSchemeStatus
            {
                State = ManagedResourceState.Restored,
                ActiveScheme = Guid.NewGuid(),
                OriginalScheme = original
            };
            OptimizationActionResult mismatch = harness.Service.RestoreCpuPower();
            Check.Equal(OperationOutcome.Failed, mismatch.Outcome);
            Check.Equal(OperationCode.StateVerificationFailed, mismatch.Code);
        }

        private static void TestActionServiceDelegatesDisplayConfirmation()
        {
            ActionServiceHarness harness = new ActionServiceHarness();
            Func<DisplayModeConfirmationRequest, DisplayModeConfirmationDecision>
                confirmation = delegate
                {
                    return DisplayModeConfirmationDecision.Keep;
                };

            OptimizationActionResult result =
                harness.Service.SetDisplayRefreshRate(48, confirmation);

            Check.Equal(OperationOutcome.Succeeded, result.Outcome);
            Check.Equal(48, harness.DisplayRefreshRate);
            Check.NotNull(harness.DisplayConfirmation);
            Check.Equal(
                DisplayModeConfirmationDecision.Keep,
                harness.DisplayConfirmation(
                    new DisplayModeConfirmationRequest(
                        48,
                        TimeSpan.FromSeconds(20))));
        }

        private static void TestActionServiceBlocksDisplayAfterStartupRecovery()
        {
            OptimizationActionResult startupRecovery =
                OptimizationActionResult.Indeterminate(
                    OperationCode.DisplayRollbackUnverified,
                    "stale session was not verified",
                    "private-test-detail");
            ActionServiceHarness blocked = new ActionServiceHarness(
                CpuHardwareSupportStatus.Supported,
                startupRecovery);

            OptimizationActionResult refresh =
                blocked.Service.SetDisplayRefreshRate(48, null);
            OptimizationActionResult install =
                blocked.Service.InstallDisplaySupport();
            OptimizationActionResult remove =
                blocked.Service.RemoveDisplaySupport();

            OptimizationActionResult[] results = { refresh, install, remove };
            for (int index = 0; index < results.Length; index++)
            {
                Check.Equal(OperationOutcome.Indeterminate, results[index].Outcome);
                Check.Equal(
                    OperationCode.DisplayRollbackUnverified,
                    results[index].Code);
                Check.Equal(
                    "startup-display-recovery=unverified",
                    results[index].DiagnosticDetail);
            }

            Check.Equal(0, blocked.DisplayRefreshRate);
            Check.Equal(0, blocked.AdminCommands.Count);
            Check.Equal(0, blocked.DisplayStatusReads);

            Guid owned = Guid.NewGuid();
            blocked.PowerStatus = new PowerSchemeStatus
            {
                State = ManagedResourceState.Installed,
                Preset = PowerPreset.Cool,
                ActiveScheme = owned,
                OwnedScheme = owned
            };
            OptimizationActionResult power =
                blocked.Service.ApplyCpuPreset(PowerPreset.Cool);
            Check.Equal(OperationOutcome.Succeeded, power.Outcome);
            Check.Equal(1, blocked.AdminCommands.Count);
            Check.Equal(AdminCommand.ApplyPowerCool, blocked.AdminCommands[0]);

            ActionServiceHarness recovered = new ActionServiceHarness(
                CpuHardwareSupportStatus.Supported,
                OptimizationActionResult.Successful(
                    "stale sessions recovered",
                    OperationCode.DisplayReverted,
                    false));
            OptimizationActionResult allowed =
                recovered.Service.SetDisplayRefreshRate(48, null);
            Check.Equal(OperationOutcome.Succeeded, allowed.Outcome);
            Check.Equal(48, recovered.DisplayRefreshRate);
        }

        private static void TestStartupRecoveryHandlesEmptyAndMissingHelper()
        {
            StartupRecoveryHarness empty = new StartupRecoveryHarness();
            empty.ExecutableAvailable = false;

            OptimizationActionResult noSessions = empty.Recover();

            Check.That(noSessions == null,
                "no durable sessions must produce no startup notification");
            Check.False(empty.VerifiedHandle.Opened);
            Check.Equal(0, empty.AttemptedTokens.Count);

            StartupRecoveryHarness missing = new StartupRecoveryHarness();
            missing.Tokens.Add("session-a");
            missing.ExecutableAvailable = false;

            OptimizationActionResult missingResult = missing.Recover();

            Check.Equal(OperationOutcome.Indeterminate, missingResult.Outcome);
            Check.Equal(
                OperationCode.DisplayRollbackUnverified,
                missingResult.Code);
            Check.That(
                missingResult.DiagnosticDetail.Contains(
                    "startup-watchdog-sessions=1"),
                "missing-helper diagnostics must retain the pending count");
            Check.False(missing.VerifiedHandle.Opened);
            Check.Equal(0, missing.AttemptedTokens.Count);
        }

        private static void TestStartupRecoveryCleansVerifiedSessions()
        {
            StartupRecoveryHarness harness = new StartupRecoveryHarness();
            harness.Tokens.Add("session-a");
            harness.Tokens.Add("session-b");
            harness.SessionFailures.Enqueue(null);
            harness.SessionFailures.Enqueue(null);

            OptimizationActionResult result = harness.Recover();

            Check.Equal(OperationOutcome.Succeeded, result.Outcome);
            Check.Equal(OperationCode.DisplayReverted, result.Code);
            Check.Equal(2, harness.AttemptedTokens.Count);
            Check.Equal("session-a", harness.CleanedTokens[0]);
            Check.Equal("session-b", harness.CleanedTokens[1]);
            Check.Equal(15000, harness.WaitMilliseconds[0]);
            Check.Equal(15000, harness.WaitMilliseconds[1]);
            Check.True(harness.VerifiedHandle.Disposed);
        }

        private static void TestStartupRecoveryRetainsPartialFailures()
        {
            StartupRecoveryHarness harness = new StartupRecoveryHarness();
            harness.Tokens.Add("session-a");
            harness.Tokens.Add("session-b");
            harness.Tokens.Add("session-c");
            harness.SessionFailures.Enqueue(null);
            harness.SessionFailures.Enqueue("timeout");
            harness.SessionFailures.Enqueue("exit-7");

            OptimizationActionResult result = harness.Recover();

            Check.Equal(OperationOutcome.Indeterminate, result.Outcome);
            Check.Equal(1, harness.CleanedTokens.Count);
            Check.Equal("session-a", harness.CleanedTokens[0]);
            Check.That(
                result.DiagnosticDetail.Contains("recovered=1"),
                "partial recovery must report the verified cleanup count");
            Check.That(
                result.DiagnosticDetail.Contains("failures=timeout,exit-7"),
                "partial recovery must retain ordered failure reasons");
            Check.True(harness.VerifiedHandle.Disposed);
        }

        private static void TestStartupRecoveryEnforcesTotalBudget()
        {
            StartupRecoveryHarness harness = new StartupRecoveryHarness();
            harness.Tokens.Add("session-a");
            harness.Tokens.Add("session-b");
            harness.ElapsedMilliseconds.Enqueue(20000);
            harness.ElapsedMilliseconds.Enqueue(30000);
            harness.SessionFailures.Enqueue(null);

            OptimizationActionResult result = harness.Recover();

            Check.Equal(OperationOutcome.Indeterminate, result.Outcome);
            Check.Equal(1, harness.AttemptedTokens.Count);
            Check.Equal(10000, harness.WaitMilliseconds[0]);
            Check.Equal(1, harness.CleanedTokens.Count);
            Check.That(
                result.DiagnosticDetail.Contains("failures=budget-exhausted"),
                "expired total budget must retain the unprocessed session");
        }

        private static void TestStartupRecoveryFailsClosedOnVerifierError()
        {
            StartupRecoveryHarness harness = new StartupRecoveryHarness();
            harness.Tokens.Add("session-a");
            harness.OpenException = new InvalidDataException(
                "watchdog bytes differ");

            OptimizationActionResult result = harness.Recover();

            Check.Equal(OperationOutcome.Indeterminate, result.Outcome);
            Check.Equal(
                OperationCode.DisplayRollbackUnverified,
                result.Code);
            Check.That(
                result.DiagnosticDetail.Contains("InvalidDataException"),
                "verifier failure type must remain available for diagnostics");
            Check.Equal(0, harness.AttemptedTokens.Count);
            Check.Equal(0, harness.CleanedTokens.Count);
        }

        private static void TestStartupRecoveryClassifiesProcessBoundaries()
        {
            Check.Equal(
                "start-failed",
                DisplayWatchdogStartupRecovery.ClassifyProcessResult(
                    false,
                    false,
                    0));
            Check.Equal(
                "timeout",
                DisplayWatchdogStartupRecovery.ClassifyProcessResult(
                    true,
                    false,
                    0));
            Check.That(
                DisplayWatchdogStartupRecovery.ClassifyProcessResult(
                    true,
                    true,
                    DisplayWatchdogExitCodes.Completed) == null,
                "a clean watchdog exit must permit session cleanup");
            Check.That(
                DisplayWatchdogStartupRecovery.ClassifyProcessResult(
                    true,
                    true,
                    DisplayWatchdogExitCodes.RollbackPerformed) == null,
                "a verified rollback must permit session cleanup");
            Check.Equal(
                "exit-7",
                DisplayWatchdogStartupRecovery.ClassifyProcessResult(
                    true,
                    true,
                    7));
        }

        private static void TestDisplayLeaseConfirmsWithoutRollback()
        {
            DisplayModeKey original = CreateDisplayModeKey(60);
            DisplayModeKey target = CreateDisplayModeKey(48);
            List<DisplayModeCall> calls = new List<DisplayModeCall>();
            DisplayModeLease lease = CreateDisplayModeLease(
                original,
                target,
                calls,
                delegate { return SuccessfulDisplayModeOperation(); },
                delegate { return SuccessfulDisplayModeOperation(); });

            lease.ConfirmAndPersist();
            lease.Dispose();

            Check.True(lease.IsCompleted);
            Check.Equal(1, calls.Count);
            Check.Equal("persist", calls[0].Operation);
            Check.Equal(@"\\.\DISPLAY9", calls[0].DeviceName);
            Check.True(calls[0].Mode.Equals(target));
        }

        private static void TestDisplayLeaseRollsBackOnDispose()
        {
            DisplayModeKey original = CreateDisplayModeKey(60);
            DisplayModeKey target = CreateDisplayModeKey(48);
            List<DisplayModeCall> calls = new List<DisplayModeCall>();
            DisplayModeLease lease = CreateDisplayModeLease(
                original,
                target,
                calls,
                delegate { return SuccessfulDisplayModeOperation(); },
                delegate { return SuccessfulDisplayModeOperation(); });

            lease.Dispose();
            lease.Dispose();

            Check.True(lease.IsCompleted);
            Check.Equal(1, calls.Count);
            Check.Equal("apply", calls[0].Operation);
            Check.True(calls[0].Mode.Equals(original));
        }

        private static void TestDisplayLeaseRetriesAfterNativeFailures()
        {
            DisplayModeKey original = CreateDisplayModeKey(60);
            DisplayModeKey target = CreateDisplayModeKey(48);
            int rollbackAttempts = 0;
            DisplayModeLease lease = new DisplayModeLease(
                delegate
                {
                    rollbackAttempts++;
                    return rollbackAttempts == 1
                        ? FailedDisplayModeOperation()
                        : SuccessfulDisplayModeOperation();
                },
                delegate
                {
                    return FailedDisplayModeOperation();
                },
                original,
                target,
                delegate { return @"\\.\DISPLAY9"; },
                TimeSpan.FromMinutes(1));

            Check.Throws<DisplayModeException>(lease.ConfirmAndPersist);
            Check.False(lease.IsCompleted);
            Check.Throws<DisplayModeException>(lease.Rollback);
            Check.False(lease.IsCompleted);

            lease.Rollback();

            Check.True(lease.IsCompleted);
            Check.Equal(2, rollbackAttempts);
            lease.Dispose();
        }

        private static void TestDisplayRollbackVerifiesOriginalMode()
        {
            DisplayModeKey original = CreateDisplayModeKey(60);
            DisplayModeKey target = CreateDisplayModeKey(48);
            MonitorIdentity identity = CreateMonitorIdentity();
            int rollbackCalls = 0;
            int readCalls = 0;
            int persistCalls = 0;
            DisplayModeLease lease = new DisplayModeLease(
                delegate
                {
                    rollbackCalls++;
                    return SuccessfulDisplayModeOperation();
                },
                delegate { return SuccessfulDisplayModeOperation(); },
                original,
                target,
                delegate { return @"\\.\DISPLAY9"; },
                TimeSpan.FromMinutes(1));

            bool restored = DisplayRefreshRateUseCase.RestoreAndVerifyOriginal(
                lease,
                identity,
                original,
                false,
                delegate
                {
                    readCalls++;
                    return CreateWindowsDisplayMode(60);
                },
                delegate
                {
                    persistCalls++;
                    return true;
                });

            Check.True(restored);
            Check.Equal(1, rollbackCalls);
            Check.Equal(2, readCalls);
            Check.Equal(0, persistCalls);
            lease.Dispose();
        }

        private static void TestDisplayRollbackForcesOriginalPersistence()
        {
            DisplayModeKey original = CreateDisplayModeKey(60);
            DisplayModeKey target = CreateDisplayModeKey(48);
            MonitorIdentity identity = CreateMonitorIdentity();
            int readCalls = 0;
            int persistCalls = 0;
            DisplayModeLease completedLease = new DisplayModeLease(
                delegate { return SuccessfulDisplayModeOperation(); },
                delegate { return SuccessfulDisplayModeOperation(); },
                original,
                target,
                delegate { return @"\\.\DISPLAY9"; },
                TimeSpan.FromMinutes(1));
            completedLease.ConfirmAndPersist();

            bool restored = DisplayRefreshRateUseCase.RestoreAndVerifyOriginal(
                completedLease,
                identity,
                original,
                true,
                delegate
                {
                    readCalls++;
                    return CreateWindowsDisplayMode(
                        readCalls == 1 ? 48 : 60);
                },
                delegate(MonitorIdentity actualIdentity, DisplayModeKey mode)
                {
                    Check.True(actualIdentity.Equals(identity));
                    Check.True(mode.Equals(original));
                    persistCalls++;
                    return true;
                });

            Check.True(restored);
            Check.Equal(2, readCalls);
            Check.Equal(1, persistCalls);

            bool unverified = DisplayRefreshRateUseCase.RestoreAndVerifyOriginal(
                completedLease,
                identity,
                original,
                true,
                delegate { return CreateWindowsDisplayMode(48); },
                delegate { return true; });
            Check.False(unverified);
            completedLease.Dispose();
        }

        private static void TestAdminHelperWaitsForTerminalExit()
        {
            int boundedMilliseconds = 0;
            int unboundedCalls = 0;
            ElevatedAdminHelper.WaitForTerminalExit(
                delegate(int milliseconds)
                {
                    boundedMilliseconds = milliseconds;
                    return true;
                },
                delegate { unboundedCalls++; });

            Check.Equal(120000, boundedMilliseconds);
            Check.Equal(0, unboundedCalls);

            List<string> order = new List<string>();
            ElevatedAdminHelper.WaitForTerminalExit(
                delegate
                {
                    order.Add("bounded");
                    return false;
                },
                delegate { order.Add("unbounded"); });

            Check.Equal(2, order.Count);
            Check.Equal("bounded", order[0]);
            Check.Equal("unbounded", order[1]);
        }

        private static void TestAdminHelperFixedArguments()
        {
            AdminCommand[] commands =
            {
                AdminCommand.InstallDisplay,
                AdminCommand.RemoveDisplay,
                AdminCommand.ApplyPowerNormal,
                AdminCommand.ApplyPowerCool,
                AdminCommand.ApplyPowerBattery,
                AdminCommand.RestorePower
            };
            string[] arguments =
            {
                "install-display",
                "remove-display",
                "apply-power normal",
                "apply-power cool",
                "apply-power battery",
                "restore-power"
            };

            for (int index = 0; index < commands.Length; index++)
            {
                Check.Equal(
                    arguments[index],
                    ElevatedAdminHelper.FixedArguments(commands[index]));
            }

            string token =
                Experimental48HzProfileGenerator.CreateAcknowledgementToken(
                    ExperimentalProfileId);
            Check.Equal(
                "install-experimental-display " + token,
                ElevatedAdminHelper.FixedArguments(
                    AdminCommand.InstallExperimentalDisplay,
                    token));

            Check.Throws<InvalidOperationException>(delegate
            {
                ElevatedAdminHelper.FixedArguments(
                    AdminCommand.InstallExperimentalDisplay);
            });
            Check.Throws<InvalidOperationException>(delegate
            {
                ElevatedAdminHelper.FixedArguments(
                    AdminCommand.InstallExperimentalDisplay,
                    "a" + token.Substring(1));
            });
            Check.Throws<InvalidOperationException>(delegate
            {
                ElevatedAdminHelper.FixedArguments(
                    AdminCommand.InstallExperimentalDisplay,
                    token + " & remove-display");
            });
            Check.Throws<InvalidOperationException>(delegate
            {
                ElevatedAdminHelper.FixedArguments(
                    AdminCommand.InstallDisplay,
                    token);
            });

            Check.Throws<InvalidOperationException>(
                delegate
                {
                    ElevatedAdminHelper.FixedArguments((AdminCommand)255);
                });
        }

        private static void TestAdminHelperExitCodeMappings()
        {
            int[] exitCodes =
            {
                AdminHelperExitCodes.Usage,
                AdminHelperExitCodes.Unsupported,
                AdminHelperExitCodes.Failed,
                AdminHelperExitCodes.Indeterminate,
                255
            };
            OperationOutcome[] outcomes =
            {
                OperationOutcome.Failed,
                OperationOutcome.Unsupported,
                OperationOutcome.Failed,
                OperationOutcome.Indeterminate,
                OperationOutcome.Failed
            };
            OperationCode[] codes =
            {
                OperationCode.HelperRejected,
                OperationCode.HelperUnsupported,
                OperationCode.HelperFailed,
                OperationCode.HelperIndeterminate,
                OperationCode.HelperFailed
            };

            for (int index = 0; index < exitCodes.Length; index++)
            {
                OptimizationActionResult result =
                    ElevatedAdminHelper.HelperFailure(exitCodes[index]);
                Check.Equal(outcomes[index], result.Outcome);
                Check.Equal(codes[index], result.Code);
                if (result.Outcome != OperationOutcome.Unsupported)
                {
                    Check.That(
                        result.DiagnosticDetail.Contains(
                            "helper-exit=" + exitCodes[index]),
                        "helper failure diagnostics must retain the exit code");
                }
            }
        }

        private static void TestDisplayPersistencePreservesWatchdogOrdering()
        {
            List<string> order = new List<string>();
            bool attempted = false;
            RecordingDisposable guard = new RecordingDisposable(order);

            DisplayRefreshRateUseCase.PersistWithWatchdog(
                delegate
                {
                    order.Add("acquire");
                    return guard;
                },
                delegate { order.Add("persist"); },
                delegate { order.Add("commit"); },
                ref attempted);

            Check.True(attempted);
            Check.Equal(4, order.Count);
            Check.Equal("acquire", order[0]);
            Check.Equal("persist", order[1]);
            Check.Equal("commit", order[2]);
            Check.Equal("dispose", order[3]);

            order.Clear();
            attempted = false;
            Check.Throws<InvalidOperationException>(
                delegate
                {
                    DisplayRefreshRateUseCase.PersistWithWatchdog(
                        delegate
                        {
                            order.Add("acquire");
                            return new RecordingDisposable(order);
                        },
                        delegate
                        {
                            order.Add("persist");
                            throw new InvalidOperationException("persist failed");
                        },
                        delegate { order.Add("commit"); },
                        ref attempted);
                });
            Check.True(attempted);
            Check.Equal(3, order.Count);
            Check.Equal("dispose", order[2]);

            order.Clear();
            attempted = false;
            Check.Throws<InvalidOperationException>(
                delegate
                {
                    DisplayRefreshRateUseCase.PersistWithWatchdog(
                        delegate
                        {
                            order.Add("acquire");
                            return new RecordingDisposable(order);
                        },
                        delegate { order.Add("persist"); },
                        delegate
                        {
                            order.Add("commit");
                            throw new InvalidOperationException("commit failed");
                        },
                        ref attempted);
                });
            Check.True(attempted);
            Check.Equal(4, order.Count);
            Check.Equal("commit", order[2]);
            Check.Equal("dispose", order[3]);

            order.Clear();
            attempted = false;
            Check.Throws<InvalidOperationException>(
                delegate
                {
                    DisplayRefreshRateUseCase.PersistWithWatchdog(
                        delegate
                        {
                            order.Add("acquire");
                            throw new InvalidOperationException(
                                "watchdog already requested rollback");
                        },
                        delegate { order.Add("persist"); },
                        delegate { order.Add("commit"); },
                        ref attempted);
                });
            Check.False(attempted);
            Check.Equal(1, order.Count);

            order.Clear();
            attempted = false;
            Check.Throws<InvalidOperationException>(
                delegate
                {
                    DisplayRefreshRateUseCase.PersistWithWatchdog(
                        delegate
                        {
                            order.Add("acquire");
                            return null;
                        },
                        delegate { order.Add("persist"); },
                        delegate { order.Add("commit"); },
                        ref attempted);
                });
            Check.False(attempted);
            Check.Equal(1, order.Count);
        }

        private static void TestConfirmedDisplayTransitionOutcomes()
        {
            DisplayModeKey original = CreateDisplayModeKey(60);
            int restoreCalls = 0;
            int readCalls = 0;

            OptimizationActionResult restored =
                DisplayRefreshRateUseCase.CompleteConfirmedTransition(
                    48,
                    original,
                    delegate { return false; },
                    delegate
                    {
                        readCalls++;
                        return CreateWindowsDisplayMode(48);
                    },
                    delegate
                    {
                        restoreCalls++;
                        return true;
                    });
            Check.Equal(OperationOutcome.Failed, restored.Outcome);
            Check.Equal(OperationCode.DisplayReverted, restored.Code);
            Check.Equal(0, readCalls);
            Check.Equal(1, restoreCalls);

            OptimizationActionResult unverified =
                DisplayRefreshRateUseCase.CompleteConfirmedTransition(
                    48,
                    original,
                    delegate { return false; },
                    delegate { return CreateWindowsDisplayMode(48); },
                    delegate { return false; });
            Check.Equal(OperationOutcome.Indeterminate, unverified.Outcome);
            Check.Equal(
                OperationCode.DisplayRollbackUnverified,
                unverified.Code);

            restoreCalls = 0;
            OptimizationActionResult mismatch =
                DisplayRefreshRateUseCase.CompleteConfirmedTransition(
                    48,
                    original,
                    delegate { return true; },
                    delegate { return CreateWindowsDisplayMode(60); },
                    delegate
                    {
                        restoreCalls++;
                        return true;
                    });
            Check.Equal(OperationOutcome.Failed, mismatch.Outcome);
            Check.Equal(OperationCode.StateVerificationFailed, mismatch.Code);
            Check.Equal(1, restoreCalls);

            OptimizationActionResult mismatchUnverified =
                DisplayRefreshRateUseCase.CompleteConfirmedTransition(
                    48,
                    original,
                    delegate { return true; },
                    delegate { return CreateWindowsDisplayMode(60); },
                    delegate { return false; });
            Check.Equal(
                OperationOutcome.Indeterminate,
                mismatchUnverified.Outcome);
            Check.Equal(
                OperationCode.DisplayRollbackUnverified,
                mismatchUnverified.Code);

            restoreCalls = 0;
            OptimizationActionResult configurationMismatch =
                DisplayRefreshRateUseCase.CompleteConfirmedTransition(
                    48,
                    original,
                    delegate { return true; },
                    delegate { return CreateWindowsDisplayMode(48, 24); },
                    delegate
                    {
                        restoreCalls++;
                        return true;
                    });
            Check.Equal(
                OperationCode.StateVerificationFailed,
                configurationMismatch.Code);
            Check.Equal(1, restoreCalls);

            restoreCalls = 0;
            OptimizationActionResult succeeded =
                DisplayRefreshRateUseCase.CompleteConfirmedTransition(
                    48,
                    original,
                    delegate { return true; },
                    delegate { return CreateWindowsDisplayMode(48); },
                    delegate
                    {
                        restoreCalls++;
                        return true;
                    });
            Check.Equal(OperationOutcome.Succeeded, succeeded.Outcome);
            Check.Equal(0, restoreCalls);
        }

        private static DisplayModeLease CreateDisplayModeLease(
            DisplayModeKey original,
            DisplayModeKey target,
            IList<DisplayModeCall> calls,
            Func<DisplayModeOperationResult> applyResult,
            Func<DisplayModeOperationResult> persistResult)
        {
            return new DisplayModeLease(
                delegate(string deviceName, DisplayModeKey mode)
                {
                    calls.Add(new DisplayModeCall("apply", deviceName, mode));
                    return applyResult();
                },
                delegate(string deviceName, DisplayModeKey mode)
                {
                    calls.Add(new DisplayModeCall("persist", deviceName, mode));
                    return persistResult();
                },
                original,
                target,
                delegate { return @"\\.\DISPLAY9"; },
                TimeSpan.FromMinutes(1));
        }

        private static DisplayModeOperationResult SuccessfulDisplayModeOperation()
        {
            return DisplayModeOperationResult.FromNative(0);
        }

        private static DisplayModeOperationResult FailedDisplayModeOperation()
        {
            return DisplayModeOperationResult.FromNative(-1);
        }

        private static DisplayModeKey CreateDisplayModeKey(int refreshRate)
        {
            return new DisplayModeKey(
                3072,
                1920,
                32,
                refreshRate,
                0,
                0,
                0,
                (uint)refreshRate,
                1);
        }

        private static WindowsDisplayMode CreateWindowsDisplayMode(
            int refreshRate)
        {
            return CreateWindowsDisplayMode(refreshRate, 32);
        }

        private static WindowsDisplayMode CreateWindowsDisplayMode(
            int refreshRate,
            int bitsPerPixel)
        {
            DEVMODE mode = DEVMODE.Create();
            mode.PelsWidth = 3072;
            mode.PelsHeight = 1920;
            mode.BitsPerPel = bitsPerPixel;
            mode.DisplayFrequency = refreshRate;
            return WindowsDisplayMode.FromNative(
                mode,
                (uint)refreshRate,
                1);
        }

        private static MonitorIdentity CreateMonitorIdentity()
        {
            return new MonitorIdentity(
                @"DISPLAY\APP1234\INSTANCE",
                "APPA044",
                "APP",
                Sha256Digest.Compute(new byte[] { 1, 2, 3, 4 }));
        }

        // The dashboard attaches its profiles controller from the constructor,
        // which runs before the telemetry service has produced anything. A
        // controller that assumes it already has a display sample takes the
        // whole tray process down on launch, and nothing catches it: the
        // ApplicationContext is constructed as an argument to Application.Run.
        private static void TestProfilesControllerRendersBeforeFirstTelemetrySample()
        {
            object customProfileItem = new object();
            DashboardProfilesController controller =
                new DashboardProfilesController(customProfileItem);
            DashboardProfilesPage page = new DashboardProfilesPage(
                customProfileItem,
                controller.OnRecommendedProfileChanged,
                controller.OnCpuPresetChanged,
                delegate { },
                delegate { },
                delegate { },
                delegate { },
                delegate { },
                delegate { },
                delegate { });

            controller.Attach(page);

            Check.That(
                !controller.IsSelectedRefreshRate(48.0),
                "an absent display sample must not report a refresh rate");
            Check.That(
                page.DisplayState.Text.Length > 0,
                "the display support state must render before telemetry arrives");

            controller.UpdateOptimizationState(null);
            controller.UpdateDisplay(null);

            Check.That(
                page.CpuState.Text.Length > 0,
                "the CPU plan state must render before telemetry arrives");
        }

        private static void TestDisplayConfirmationCountdownBoundary()
        {
            TimeSpan timeout = TimeSpan.FromSeconds(20);
            Check.That(
                DisplayModeConfirmationDialog.RemainingWholeSeconds(
                    timeout,
                    TimeSpan.Zero) == 20,
                "display confirmation countdown must start at the full timeout");
            Check.That(
                DisplayModeConfirmationDialog.RemainingWholeSeconds(
                    timeout,
                    TimeSpan.FromMilliseconds(19500)) == 1,
                "display confirmation countdown must round a partial second up");
            Check.That(
                DisplayModeConfirmationDialog.RemainingWholeSeconds(
                    timeout,
                    timeout) == 0,
                "display confirmation countdown must close at the deadline");
        }

        private static void TestExperimentalRefreshRequiresJournalTarget()
        {
            Sha256Digest originalHash =
                Sha256Digest.Compute(new byte[] { 1, 2, 3 });
            Sha256Digest ownedHash =
                Sha256Digest.Compute(new byte[] { 4, 5, 6 });
            Sha256Digest foreignHash =
                Sha256Digest.Compute(new byte[] { 7, 8, 9 });
            MonitorIdentity journaled = new MonitorIdentity(
                @"DISPLAY\APPA044\4&ABC&0&UID0",
                "APPA044",
                "APP",
                originalHash);
            MonitorIdentity originalCurrent = new MonitorIdentity(
                @"DISPLAY\APPA044\4&ABC&0&UID0",
                "APPA044",
                "APP",
                originalHash);
            MonitorIdentity ownedCurrent = new MonitorIdentity(
                @"DISPLAY\APPA044\4&ABC&0&UID0",
                "APPA044",
                "APP",
                ownedHash);

            Check.True(DisplayRefreshRateValidator.MatchesJournalTarget(
                originalCurrent,
                journaled,
                ownedHash));
            Check.True(DisplayRefreshRateValidator.MatchesJournalTarget(
                ownedCurrent,
                journaled,
                ownedHash));
            Check.False(DisplayRefreshRateValidator.MatchesJournalTarget(
                new MonitorIdentity(
                    @"DISPLAY\APPA044\4&OTHER&0&UID0",
                    "APPA044",
                    "APP",
                    originalHash),
                journaled,
                ownedHash));
            Check.False(DisplayRefreshRateValidator.MatchesJournalTarget(
                new MonitorIdentity(
                    @"DISPLAY\APPA045\4&ABC&0&UID0",
                    "APPA045",
                    "APP",
                    originalHash),
                journaled,
                ownedHash));
            Check.False(DisplayRefreshRateValidator.MatchesJournalTarget(
                new MonitorIdentity(
                    @"DISPLAY\APPA044\4&ABC&0&UID0",
                    "APPA044",
                    "APP",
                    foreignHash),
                journaled,
                ownedHash));
            Check.False(DisplayRefreshRateValidator.MatchesJournalTarget(
                originalCurrent,
                journaled,
                null));
        }

        private static void TestInstallCommandPreservesRiskAcknowledgement()
        {
            string acknowledgementToken =
                Experimental48HzProfileGenerator.CreateAcknowledgementToken(
                    ExperimentalProfileId);
            OptimizationCommand declined =
                OptimizationCommand.InstallDisplaySupport();
            OptimizationCommand accepted =
                OptimizationCommand.InstallDisplaySupport(
                    acknowledgementToken);

            Check.Equal(
                OptimizationCommandKind.InstallDisplaySupport,
                declined.Kind);
            Check.That(
                declined.ExperimentalAcknowledgementToken == null,
                "An ordinary install command must carry no consent token.");
            Check.Equal(
                acknowledgementToken,
                accepted.ExperimentalAcknowledgementToken);

            FakeActions actions = new FakeActions();
            using (OptimizationCommandRunner runner =
                new OptimizationCommandRunner(
                    actions,
                    null,
                    TimeSpan.FromSeconds(1)))
            {
                ManualResetEvent completed = new ManualResetEvent(false);
                runner.Completed += delegate
                {
                    completed.Set();
                };

                runner.Execute(declined);
                Check.That(completed.WaitOne(1000),
                    "the declined install command did not complete");
                Check.Equal(
                    1,
                    actions.InstallAcknowledgementTokens.Count);
                Check.That(
                    actions.InstallAcknowledgementTokens[0] == null,
                    "The command runner changed a missing token.");

                completed.Reset();
                runner.Execute(accepted);
                Check.That(completed.WaitOne(1000),
                    "the acknowledged install command did not complete");
                Check.Equal(
                    2,
                    actions.InstallAcknowledgementTokens.Count);
                Check.Equal(
                    acknowledgementToken,
                    actions.InstallAcknowledgementTokens[1]);
            }
        }

        private static void TestDisplaySupportUiPolicy()
        {
            OptimizationStateSnapshot installed = new OptimizationStateSnapshot(
                true,
                false,
                null,
                "NotInstalled",
                "Installed",
                "profile",
                "read-back",
                true,
                false,
                false,
                "Reviewed profile details must remain hidden here.");
            DisplaySupportUiState installedUi = DisplaySupportUiPolicy.Evaluate(
                installed,
                false,
                true);
            Check.That(installedUi.CanSelect48Hz
                && installedUi.CanSelect60Hz
                && installedUi.CanInstall
                && installedUi.ShowRemove
                && installedUi.CanRemove
                && installedUi.SupportText.IndexOf(
                    "Reviewed profile details",
                    StringComparison.Ordinal) < 0
                && installedUi.InstallText == "Repair 48 Hz support",
                "owned display support must expose 48 Hz, repair, and remove");

            OptimizationStateSnapshot pendingRestart =
                new OptimizationStateSnapshot(
                    true,
                    false,
                    null,
                    "NotInstalled",
                    "Installed",
                    "profile",
                    "read-back");
            DisplaySupportUiState pendingUi = DisplaySupportUiPolicy.Evaluate(
                pendingRestart,
                false,
                true);
            Check.That(!pendingUi.CanSelect48Hz
                && pendingUi.CanSelect60Hz
                && !pendingUi.CanInstall
                && pendingUi.SupportText.IndexOf(
                    "Restart Windows",
                    StringComparison.OrdinalIgnoreCase) >= 0,
                "an installed override must wait for live driver readiness");

            OptimizationStateSnapshot notInstalled = new OptimizationStateSnapshot(
                true,
                false,
                null,
                "NotInstalled",
                "NotInstalled",
                string.Empty,
                "read-back");
            DisplaySupportUiState missingUi = DisplaySupportUiPolicy.Evaluate(
                notInstalled,
                false,
                true);
            Check.That(!missingUi.CanSelect48Hz
                && missingUi.CanSelect60Hz
                && !missingUi.CanInstall
                && !missingUi.ShowRemove
                && !missingUi.CanRemove
                && missingUi.InstallText == "Install 48 Hz support",
                "a fresh install must stay disabled without a current candidate");

            OptimizationStateSnapshot restoredWithoutCandidate =
                new OptimizationStateSnapshot(
                    true,
                    false,
                    null,
                    "NotInstalled",
                    "Restored",
                    string.Empty,
                    "read-back");
            DisplaySupportUiState restoredWithoutCandidateUi =
                DisplaySupportUiPolicy.Evaluate(
                    restoredWithoutCandidate,
                    false,
                    true);
            Check.That(!restoredWithoutCandidateUi.CanInstall
                && restoredWithoutCandidateUi.CanSelect60Hz,
                "a restored state must also require a current candidate");

            OptimizationStateSnapshot reviewedCandidate =
                new OptimizationStateSnapshot(
                    true,
                    false,
                    null,
                    "NotInstalled",
                    "NotInstalled",
                    "reviewed-static-profile",
                    "read-back",
                    false,
                    true,
                    false,
                    "Reviewed profile for this exact hardware.");
            DisplaySupportUiState reviewedCandidateUi =
                DisplaySupportUiPolicy.Evaluate(
                    reviewedCandidate,
                    false,
                    true);
            Check.That(reviewedCandidateUi.CanInstall
                && reviewedCandidateUi.InstallText == "Install 48 Hz support",
                "an eligible reviewed candidate must enable the normal install");

            const string DynamicProfileId =
                "exp48-v1-mbp164-7360-"
                    + "0123456789abcdef0123456789abcdef"
                    + "0123456789abcdef0123456789abcdef";
            const string RawEdidMarker = "00 FF FF FF FF FF FF 00";
            const string DeviceInstanceMarker =
                @"DISPLAY\APPLE123\PRIVATE-INSTANCE";
            const string SafeCandidateSummary =
                "Model MacBookPro16,4; panel APPA0AA; controlling GPU "
                    + @"PCI\VEN_1002&DEV_7360; native 3072x1920 @ 60 Hz, "
                    + "466.81 MHz, totals 3200x2016; calculated 48 Hz: "
                    + "373.40 MHz, totals 3200x2431.";
            OptimizationStateSnapshot experimentalCandidate =
                new OptimizationStateSnapshot(
                    true,
                    false,
                    null,
                    "NotInstalled",
                    "NotInstalled",
                    DynamicProfileId,
                    RawEdidMarker + " " + DeviceInstanceMarker,
                    false,
                    true,
                    true,
                    SafeCandidateSummary);
            DisplaySupportUiState experimentalCandidateUi =
                DisplaySupportUiPolicy.Evaluate(
                    experimentalCandidate,
                    false,
                    true);
            string experimentalPresentation =
                experimentalCandidateUi.InstallText + "\n"
                    + experimentalCandidateUi.SupportText;
            Check.That(experimentalCandidateUi.CanInstall
                && experimentalCandidateUi.InstallText.IndexOf(
                    "experimental",
                    StringComparison.OrdinalIgnoreCase) >= 0
                && experimentalCandidateUi.SupportText.IndexOf(
                    "not hardware verified",
                    StringComparison.OrdinalIgnoreCase) >= 0
                && experimentalCandidateUi.SupportText.IndexOf(
                    SafeCandidateSummary,
                    StringComparison.Ordinal) >= 0,
                "an eligible generated candidate must be explicit and safe");
            Check.That(experimentalPresentation.IndexOf(
                    DynamicProfileId,
                    StringComparison.OrdinalIgnoreCase) < 0
                && experimentalPresentation.IndexOf(
                    RawEdidMarker,
                    StringComparison.OrdinalIgnoreCase) < 0
                && experimentalPresentation.IndexOf(
                    DeviceInstanceMarker,
                    StringComparison.OrdinalIgnoreCase) < 0,
                "candidate presentation must omit hashes, raw EDID, and device IDs");

            OptimizationStateSnapshot ineligibleExperimental =
                new OptimizationStateSnapshot(
                    true,
                    false,
                    null,
                    "NotInstalled",
                    "NotInstalled",
                    string.Empty,
                    "read-back",
                    false,
                    false,
                    true,
                    SafeCandidateSummary);
            DisplaySupportUiState ineligibleExperimentalUi =
                DisplaySupportUiPolicy.Evaluate(
                    ineligibleExperimental,
                    false,
                    true);
            Check.That(
                !ineligibleExperimentalUi.CanInstall &&
                ineligibleExperimentalUi.SupportText.IndexOf(
                    SafeCandidateSummary,
                    StringComparison.Ordinal) < 0,
                "an ineligible generated profile must not expose calculated details");

            DisplaySupportUiState externalUi = DisplaySupportUiPolicy.Evaluate(
                notInstalled,
                true,
                true);
            Check.That(!externalUi.CanSelect48Hz
                && !externalUi.CanInstall
                && !externalUi.ShowRemove
                && externalUi.SupportText.IndexOf("external", StringComparison.OrdinalIgnoreCase) >= 0,
                "an external active 48 Hz mode must not expose MacBookEco mutations");

            OptimizationStateSnapshot conflict = new OptimizationStateSnapshot(
                true,
                false,
                null,
                "NotInstalled",
                "Conflict",
                string.Empty,
                "read-back");
            DisplaySupportUiState repairUi = DisplaySupportUiPolicy.Evaluate(
                conflict,
                false,
                true);
            Check.That(!repairUi.CanSelect48Hz
                && repairUi.CanSelect60Hz
                && repairUi.CanInstall
                && !repairUi.ShowRemove
                && repairUi.InstallText == "Repair 48 Hz support",
                "a display conflict must expose only exact-owned repair and native recovery");

            DisplaySupportUiState busyUi = DisplaySupportUiPolicy.Evaluate(
                installed,
                false,
                false);
            Check.That(!busyUi.CanSelect48Hz
                && !busyUi.CanSelect60Hz
                && !busyUi.CanInstall
                && !busyUi.CanRemove,
                "the busy overlay must disable every display mutation");
        }

        private static void TestGlobalSingleFlightReturnsBusy()
        {
            FakeActions actions = new FakeActions();
            actions.BlockCpu = true;
            using (OptimizationCommandRunner runner =
                new OptimizationCommandRunner(
                    actions,
                    null,
                    TimeSpan.FromSeconds(2)))
            {
                List<OptimizationActionResult> results =
                    new List<OptimizationActionResult>();
                ManualResetEvent completed = new ManualResetEvent(false);
                runner.Completed += delegate(object sender,
                    OptimizationCommandCompletedEventArgs eventArgs)
                {
                    lock (results)
                    {
                        results.Add(eventArgs.Result);
                    }

                    if (eventArgs.Result.Outcome == OperationOutcome.Succeeded)
                    {
                        completed.Set();
                    }
                };

                runner.Execute(OptimizationCommand.ApplyCpuPreset(PowerPreset.Cool));
                Check.That(actions.CpuEntered.WaitOne(1000),
                    "first command did not start");
                runner.Execute(OptimizationCommand.ApplyCpuPreset(PowerPreset.Normal));
                actions.ReleaseCpu.Set();
                Check.That(completed.WaitOne(1500),
                    "first command did not complete");

                bool busySeen = false;
                lock (results)
                {
                    foreach (OptimizationActionResult result in results)
                    {
                        busySeen |= result.Outcome == OperationOutcome.Busy;
                    }
                }

                Check.That(busySeen,
                    "second dashboard/tray entry must observe the global busy gate");
                Check.That(actions.CpuCalls == 1,
                    "single-flight gate must not invoke a conflicting CPU mutation");
            }
        }

        private static void TestTimeoutIsIndeterminateUntilLateReadBack()
        {
            FakeActions actions = new FakeActions();
            actions.BlockCpu = true;
            using (OptimizationCommandRunner runner =
                new OptimizationCommandRunner(
                    actions,
                    null,
                    TimeSpan.FromMilliseconds(80)))
            {
                List<OptimizationCommandCompletedEventArgs> completions =
                    new List<OptimizationCommandCompletedEventArgs>();
                ManualResetEvent indeterminate = new ManualResetEvent(false);
                ManualResetEvent late = new ManualResetEvent(false);
                runner.Completed += delegate(object sender,
                    OptimizationCommandCompletedEventArgs eventArgs)
                {
                    lock (completions)
                    {
                        completions.Add(eventArgs);
                        if (completions.Count >= 2 &&
                            eventArgs.Result.Outcome !=
                                OperationOutcome.Indeterminate)
                        {
                            late.Set();
                        }
                    }

                    if (eventArgs.Result.Outcome == OperationOutcome.Indeterminate)
                    {
                        indeterminate.Set();
                    }

                };

                runner.Execute(OptimizationCommand.ApplyCpuPreset(PowerPreset.Cool));
                Check.That(actions.CpuEntered.WaitOne(1000), "hung action did not start");
                Check.That(indeterminate.WaitOne(1500),
                    "timeout must report an indeterminate result");
                Check.That(runner.IsBusy,
                    "timed-out command must retain the conflict gate");
                Check.That(actions.CpuCalls == 1,
                    "timeout must not kill or restart the helper action");

                actions.ReleaseCpu.Set();
                Check.That(late.WaitOne(1500),
                    "late completed action did not publish reconciliation");
                Check.That(!runner.IsBusy,
                    "verified late completion must release the gate");
            }
        }

        private static void TestCombinedProfileIsSequentialAndStopsAfterDisplayFailure()
        {
            FakeActions actions = new FakeActions();
            actions.DisplayResult = OptimizationActionResult.Cancelled(
                OperationCode.DisplayReverted,
                "display restored");
            using (OptimizationCommandRunner runner =
                new OptimizationCommandRunner(
                    actions,
                    null,
                    TimeSpan.FromSeconds(1)))
            {
                ManualResetEvent completed = new ManualResetEvent(false);
                OptimizationActionResult finalResult = null;
                runner.Completed += delegate(object sender,
                    OptimizationCommandCompletedEventArgs eventArgs)
                {
                    finalResult = eventArgs.Result;
                    completed.Set();
                };
                runner.Execute(OptimizationCommand.ApplyCombinedProfile(
                    48,
                    PowerPreset.Cool,
                    true,
                    "Test profile"));
                Check.That(completed.WaitOne(1000),
                    "combined profile did not complete");
                Check.That(finalResult.Outcome == OperationOutcome.Cancelled,
                    "combined result must preserve terminal display outcome");
                Check.That(finalResult.Code
                    == OperationCode.CombinedProfileDisplayIncomplete,
                    "combined result must identify the incomplete display step");
                Check.That(actions.DisplayCalls == 1 && actions.CpuCalls == 0,
                    "CPU must not start after a failed display step");
            }
        }

        private static void TestCombinedProfileRunsDisplayBeforeCpu()
        {
            FakeActions actions = new FakeActions();
            using (OptimizationCommandRunner runner =
                new OptimizationCommandRunner(
                    actions,
                    null,
                    TimeSpan.FromSeconds(1)))
            {
                ManualResetEvent completed = new ManualResetEvent(false);
                OptimizationActionResult finalResult = null;
                runner.Completed += delegate(object sender,
                    OptimizationCommandCompletedEventArgs eventArgs)
                {
                    finalResult = eventArgs.Result;
                    completed.Set();
                };
                runner.Execute(OptimizationCommand.ApplyCombinedProfile(
                    48,
                    PowerPreset.Cool,
                    true,
                    "Test profile"));
                Check.That(completed.WaitOne(1000),
                    "successful combined profile did not complete");
                Check.That(finalResult.Outcome == OperationOutcome.Succeeded,
                    "combined profile must report the CPU terminal result");
                Check.That(actions.CallOrder.Count == 2
                    && actions.CallOrder[0] == "display"
                    && actions.CallOrder[1] == "cpu",
                    "combined profile must run display before CPU without atomic rollback");
            }
        }

        private static void TestTimeSeriesBufferRetainsGapsAndChronologicalOrder()
        {
            TimeSeriesBuffer buffer = new TimeSeriesBuffer(3);
            DateTime first = new DateTime(2026, 7, 30, 10, 0, 0, DateTimeKind.Utc);
            buffer.Add(first, 10.0);
            buffer.Add(first.AddSeconds(1), double.NaN);
            buffer.Add(first.AddSeconds(2), 30.0);
            buffer.Add(first.AddSeconds(3), 40.0);

            Check.That(buffer.Count == 3, "ring buffer must overwrite only its oldest sample");
            TimeSeriesSample gap = buffer.GetChronologicalSample(0);
            Check.That(!gap.IsValid && gap.TimestampUtc == first.AddSeconds(1),
                "invalid sample must remain an explicit chronological graph gap");
            Check.That(buffer.GetChronologicalSample(1).Value == 30.0,
                "ring buffer must retain the next valid sample after overwrite");

            TimeSeriesSample latest;
            Check.That(buffer.TryGetLatest(out latest) && latest.IsValid && latest.Value == 40.0,
                "latest sample must be the newest valid sample after ring wrap");
        }

        // Showing the dashboard republishes the latest telemetry snapshot, so
        // the same sample arrives at the graphs again with the same timestamp.
        // Plotting it twice skewed every visible average, minimum and maximum.
        private static void TestTimeSeriesBufferRejectsRepublishedSample()
        {
            TimeSeriesBuffer buffer = new TimeSeriesBuffer(4);
            DateTime start = new DateTime(2026, 7, 30, 10, 0, 0, DateTimeKind.Utc);
            Check.That(buffer.Add(start, 10.0), "the first sample must be accepted");
            Check.That(!buffer.Add(start, 10.0),
                "republishing the same snapshot must not add a second point");
            Check.That(!buffer.Add(start.AddSeconds(-1), 99.0),
                "an older sample must not be appended out of order");
            Check.That(buffer.Count == 1, "only distinct newer samples may be stored");

            Check.That(buffer.Add(start.AddSeconds(1), 20.0),
                "a newer sample must still be accepted");
            TimeSeriesStatistics statistics = TimeSeriesStatisticsCalculator.Calculate(
                buffer,
                start,
                start.AddSeconds(1));
            Check.That(statistics.Count == 2 && Math.Abs(statistics.Average - 15.0) < 0.00001,
                "statistics must see each sample exactly once");
        }

        private static void TestTimeSeriesStatisticsIgnoreGapsOutsideWindow()
        {
            TimeSeriesBuffer buffer = new TimeSeriesBuffer(5);
            DateTime start = new DateTime(2026, 7, 30, 10, 0, 0, DateTimeKind.Utc);
            buffer.Add(start, 5.0);
            buffer.Add(start.AddSeconds(1), null);
            buffer.Add(start.AddSeconds(2), 15.0);
            buffer.Add(start.AddSeconds(3), 100.0);

            TimeSeriesStatistics statistics = TimeSeriesStatisticsCalculator.Calculate(
                buffer,
                start,
                start.AddSeconds(2));
            Check.That(statistics.Count == 2, "statistics must exclude gaps and later samples");
            Check.That(statistics.Minimum == 5.0 && statistics.Maximum == 15.0,
                "statistics must retain the visible minimum and maximum");
            Check.That(Math.Abs(statistics.Average - 10.0) < 0.00001,
                "statistics must calculate the visible average");
        }

        private static void TestTimeSeriesAxisRangeIsFiniteAndHonorsBounds()
        {
            TimeSeriesStatistics empty = new TimeSeriesStatistics();
            double minimum;
            double maximum;
            TimeSeriesAxisRange.Calculate(
                empty,
                5.0,
                null,
                out minimum,
                out maximum);
            Check.That(minimum == 5.0 && maximum == 6.0,
                "empty axis must remain visible above a fixed minimum");

            TimeSeriesStatistics constant = new TimeSeriesStatistics();
            constant.Count = 2;
            constant.Minimum = 10.0;
            constant.Maximum = 10.0;
            TimeSeriesAxisRange.Calculate(
                constant,
                null,
                null,
                out minimum,
                out maximum);
            Check.That(minimum < 10.0 && maximum > 10.0 && maximum > minimum,
                "constant samples must receive a finite padded axis range");

            TimeSeriesAxisRange.Calculate(
                constant,
                3.0,
                3.0,
                out minimum,
                out maximum);
            Check.That(maximum > minimum && minimum == 3.0,
                "equal fixed bounds must widen safely instead of producing a zero span");
        }

        private static void TestMetricCardPresentationMapsStatusAndAccessibility()
        {
            MetricCardVisualState warning =
                MetricCardPresentation.CreateVisualState(
                    "CPU",
                    "20%",
                    "45 C",
                    string.Empty,
                    DashboardTheme.AccentColor,
                    MetricCardStatus.Warning,
                    DashboardTheme.PrimaryTextColor,
                    true);
            Check.That(warning.StatusText == "Attention"
                && warning.StatusColor == DashboardTheme.WarningColor,
                "warning cards must keep their reviewed status label and color");

            Check.That(MetricCardPresentation.GetAccessibleName(string.Empty) == "Metric",
                "empty card title must retain an accessible fallback name");
            Check.That(MetricCardPresentation.GetAccessibleDescription(
                "20%",
                "45 C",
                string.Empty,
                MetricCardStatus.Available,
                false).EndsWith("Unavailable"),
                "disabled card accessibility must report unavailability");

            Check.Throws<ArgumentOutOfRangeException>(
                delegate
                {
                    MetricCardPresentation.ValidateStatus((MetricCardStatus)99);
                },
                "unknown card status must fail closed");
        }

        private sealed class RecordingDisposable : IDisposable
        {
            private readonly IList<string> _order;

            public RecordingDisposable(IList<string> order)
            {
                _order = order;
            }

            public void Dispose()
            {
                _order.Add("dispose");
            }
        }

        private sealed class DisplayModeCall
        {
            public DisplayModeCall(
                string operation,
                string deviceName,
                DisplayModeKey mode)
            {
                Operation = operation;
                DeviceName = deviceName;
                Mode = mode;
            }

            public string Operation { get; private set; }
            public string DeviceName { get; private set; }
            public DisplayModeKey Mode { get; private set; }
        }

        private sealed class StartupRecoveryHarness
        {
            public readonly List<string> Tokens = new List<string>();
            public readonly Queue<string> SessionFailures =
                new Queue<string>();
            public readonly Queue<long> ElapsedMilliseconds =
                new Queue<long>();
            public readonly List<string> AttemptedTokens =
                new List<string>();
            public readonly List<string> CleanedTokens =
                new List<string>();
            public readonly List<int> WaitMilliseconds = new List<int>();
            public readonly DisposableProbe VerifiedHandle =
                new DisposableProbe();
            public bool ExecutableAvailable = true;
            public Exception OpenException;

            public OptimizationActionResult Recover()
            {
                return DisplayWatchdogStartupRecovery.Recover(
                    Tokens.AsReadOnly(),
                    ExecutableAvailable,
                    OpenVerifiedWatchdog,
                    RecoverSession,
                    CleanedTokens.Add,
                    ReadElapsedMilliseconds);
            }

            private IDisposable OpenVerifiedWatchdog()
            {
                if (OpenException != null)
                {
                    throw OpenException;
                }

                VerifiedHandle.Opened = true;
                return VerifiedHandle;
            }

            private string RecoverSession(string token, int waitMilliseconds)
            {
                AttemptedTokens.Add(token);
                WaitMilliseconds.Add(waitMilliseconds);
                return SessionFailures.Count == 0
                    ? null
                    : SessionFailures.Dequeue();
            }

            private long ReadElapsedMilliseconds()
            {
                return ElapsedMilliseconds.Count == 0
                    ? 0
                    : ElapsedMilliseconds.Dequeue();
            }
        }

        private sealed class DisposableProbe : IDisposable
        {
            public bool Opened;
            public bool Disposed;

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private sealed class ActionServiceHarness
        {
            public readonly List<AdminCommand> AdminCommands =
                new List<AdminCommand>();
            public readonly List<string> AdminAcknowledgementTokens =
                new List<string>();
            public readonly Queue<DisplayOverrideStatus> DisplayStatuses =
                new Queue<DisplayOverrideStatus>();
            public readonly WindowsOptimizationActionService Service;
            public OptimizationActionResult HelperResult =
                OptimizationActionResult.Successful(
                    "helper",
                    OperationCode.None,
                    false);
            public OptimizationActionResult DisplayResult =
                OptimizationActionResult.Successful(
                    "display",
                    OperationCode.None,
                    false);
            public DisplayOverrideStatus DisplayStatus =
                new DisplayOverrideStatus();
            public DisplayProfileCandidateStatus DisplayCandidate =
                new DisplayProfileCandidateStatus
                {
                    Available = true,
                    EligibleForInstall = true,
                    Experimental = false,
                    ReviewedProfileId =
                        ProfileCatalog.MacBookPro161Appa044ProfileId,
                    SafeSummary = "Reviewed candidate summary."
                };
            public PowerSchemeStatus PowerStatus = new PowerSchemeStatus();
            public Exception DisplayStatusException;
            public int DisplayStatusReads;
            public int DisplayCandidateReads;
            public int PowerStatusReads;
            public bool Display48HzAvailable;
            public int DisplayRefreshRate;
            public Func<
                DisplayModeConfirmationRequest,
                DisplayModeConfirmationDecision> DisplayConfirmation;

            public ActionServiceHarness(
                CpuHardwareSupportStatus cpuHardwareSupport =
                    CpuHardwareSupportStatus.Supported,
                OptimizationActionResult startupRecovery = null)
            {
                Service = new WindowsOptimizationActionService(
                    SetDisplayRefreshRate,
                    ReadDisplayStatus,
                    ReadPowerStatus,
                    RunAdminCommand,
                    cpuHardwareSupport,
                    startupRecovery,
                    Is48HzModeAvailable,
                    ReadDisplayCandidate);
            }

            private OptimizationActionResult SetDisplayRefreshRate(
                int refreshRateHz,
                Func<
                    DisplayModeConfirmationRequest,
                    DisplayModeConfirmationDecision> confirmation)
            {
                DisplayRefreshRate = refreshRateHz;
                DisplayConfirmation = confirmation;
                return DisplayResult;
            }

            private DisplayOverrideStatus ReadDisplayStatus()
            {
                DisplayStatusReads++;
                if (DisplayStatusException != null)
                {
                    throw DisplayStatusException;
                }

                return DisplayStatuses.Count == 0
                    ? DisplayStatus
                    : DisplayStatuses.Dequeue();
            }

            private DisplayProfileCandidateStatus ReadDisplayCandidate(
                string profileId)
            {
                DisplayCandidateReads++;
                return DisplayCandidate;
            }

            private PowerSchemeStatus ReadPowerStatus()
            {
                PowerStatusReads++;
                return PowerStatus;
            }

            private bool Is48HzModeAvailable()
            {
                return Display48HzAvailable;
            }

            private OptimizationActionResult RunAdminCommand(
                AdminCommand command,
                string acknowledgementToken)
            {
                AdminCommands.Add(command);
                AdminAcknowledgementTokens.Add(acknowledgementToken);
                return HelperResult;
            }
        }

        private sealed class UninstallActions : IOptimizationActionService
        {
            public readonly Queue<OptimizationStateSnapshot> States =
                new Queue<OptimizationStateSnapshot>();
            public readonly List<string> CallOrder = new List<string>();
            public OptimizationActionResult InstallResult =
                OptimizationActionResult.Successful(
                    "repaired",
                    OperationCode.None,
                    true);

            public OptimizationActionResult SetDisplayRefreshRate(
                int refreshRateHz,
                Func<
                    DisplayModeConfirmationRequest,
                    DisplayModeConfirmationDecision> confirmation)
            {
                CallOrder.Add("set-" + refreshRateHz);
                return OptimizationActionResult.Successful(
                    "native mode",
                    OperationCode.None,
                    false);
            }

            public OptimizationActionResult InstallDisplaySupport(
                string experimentalAcknowledgementToken = null)
            {
                CallOrder.Add("repair-display");
                return InstallResult;
            }

            public OptimizationActionResult RemoveDisplaySupport()
            {
                CallOrder.Add("remove-display");
                return OptimizationActionResult.Successful(
                    "removed",
                    OperationCode.None,
                    true);
            }

            public OptimizationActionResult ApplyCpuPreset(PowerPreset preset)
            {
                throw new InvalidOperationException(
                    "Uninstall recovery must not apply a CPU preset.");
            }

            public OptimizationActionResult RestoreCpuPower()
            {
                CallOrder.Add("restore-power");
                return OptimizationActionResult.Successful(
                    "restored",
                    OperationCode.None,
                    false);
            }

            public OptimizationStateSnapshot ReadState()
            {
                return States.Count == 0
                    ? OptimizationStateSnapshot.Unavailable(
                        "No test state remains.")
                    : States.Dequeue();
            }
        }

        private sealed class FakeActions : IOptimizationActionService
        {
            public readonly ManualResetEvent CpuEntered = new ManualResetEvent(false);
            public readonly ManualResetEvent ReleaseCpu = new ManualResetEvent(false);
            public readonly List<string> InstallAcknowledgementTokens =
                new List<string>();
            public bool BlockCpu;
            public int CpuCalls;
            public int DisplayCalls;
            public readonly List<string> CallOrder = new List<string>();
            public OptimizationActionResult DisplayResult =
                OptimizationActionResult.Successful("display", OperationCode.None, false);

            public OptimizationActionResult SetDisplayRefreshRate(
                int refreshRateHz,
                Func<DisplayModeConfirmationRequest, DisplayModeConfirmationDecision>
                    confirmation)
            {
                DisplayCalls++;
                lock (CallOrder)
                {
                    CallOrder.Add("display");
                }
                return DisplayResult;
            }

            public OptimizationActionResult InstallDisplaySupport(
                string experimentalAcknowledgementToken = null)
            {
                InstallAcknowledgementTokens.Add(
                    experimentalAcknowledgementToken);
                return OptimizationActionResult.Successful("install", OperationCode.None, false);
            }

            public OptimizationActionResult RemoveDisplaySupport()
            {
                return OptimizationActionResult.Successful("remove", OperationCode.None, false);
            }

            public OptimizationActionResult ApplyCpuPreset(PowerPreset preset)
            {
                CpuCalls++;
                lock (CallOrder)
                {
                    CallOrder.Add("cpu");
                }
                CpuEntered.Set();
                if (BlockCpu)
                {
                    ReleaseCpu.WaitOne(3000);
                }

                return OptimizationActionResult.Successful("cpu", OperationCode.None, false);
            }

            public OptimizationActionResult RestoreCpuPower()
            {
                return OptimizationActionResult.Successful("restore", OperationCode.None, false);
            }

            public OptimizationStateSnapshot ReadState()
            {
                return new OptimizationStateSnapshot(
                    true,
                    false,
                    null,
                    "Restored",
                    "NotInstalled",
                    string.Empty,
                    "read-back");
            }
        }
    }
}
