using System;
using System.IO;
using MacBookEco.Core;
using MacBookEco.Platform.Windows;

namespace MacBookEco.Tests.Security
{
    /// <summary>
    /// Hostile-parser cases for the exact JournalCodec in the App assembly.
    /// </summary>
    internal static class JournalCodecTests
    {
        private const int StateOffset = 8;
        private const int OperationIdOffset = 18;
        private const int FieldCountOffset = 70;

        internal static TestCase[] CreateCases()
        {
            return new[]
            {
                Test("SHA-256 requires canonical upper-case hex", Sha256IsCanonical),
                Test("Journal read rewinds a reused native file position", JournalReadRewinds),
                Test("Protected DACL accepts Windows replacement metadata", ProtectedDaclFlags),
                Test("EDID install intent round-trips exactly", EdidRoundTrip),
                Test("Power retained state round-trips exactly", PowerRoundTrip),
                Test("All typed power journal states round-trip canonically", PowerStateRoundTrips),
                Test("Power payload values fail closed", PowerCanonicalValues),
                Test("Power transition matrix preserves durable ownership", PowerTransitionMatrix),
                Test("Typed transitions preserve ownership identity", TypedTransitions),
                Test("Unknown and duplicate fields are rejected", UnknownAndDuplicateFieldsRejected),
                Test("Truncated, trailing and oversized bytes are rejected", FramingErrorsRejected),
                Test("Invalid enum, GUID and hash values are rejected", InvalidValuesRejected),
                Test("Impossible state and payload combinations are rejected", ImpossiblePayloadRejected)
            };
        }

        private static TestCase Test(string name, Action body)
        {
            return new TestCase("Security: " + name, body);
        }

        private static void Sha256IsCanonical()
        {
            Sha256Digest value = Sha256Digest.ParseCanonical(HashA);
            Check.Equal(HashA, value.ToString());

            Check.Throws<FormatException>(delegate
            {
                Sha256Digest.ParseCanonical(HashA.ToLowerInvariant());
            });
            Check.Throws<FormatException>(delegate
            {
                Sha256Digest.ParseCanonical(HashA + "00");
            });
        }

        private static void JournalReadRewinds()
        {
            using (MemoryStream stream = new MemoryStream(new byte[] { 1, 2, 3 }))
            {
                stream.Position = stream.Length;
                SecureStateStore.RewindJournalStream(stream);
                Check.Equal((long)0, stream.Position);
                Check.Equal(1, stream.ReadByte());
            }
        }

        private static void ProtectedDaclFlags()
        {
            const int daclPresent = 0x0004;
            const int daclDefaulted = 0x0008;
            const int autoInheritRequired = 0x0100;
            const int autoInherited = 0x0400;
            const int daclProtected = 0x1000;

            Check.True(SecureStateStore.HasAcceptedDaclControlFlags(
                daclPresent | daclProtected));
            Check.True(SecureStateStore.HasAcceptedDaclControlFlags(
                daclPresent | daclProtected | autoInherited));
            Check.False(SecureStateStore.HasAcceptedDaclControlFlags(
                daclPresent));
            Check.False(SecureStateStore.HasAcceptedDaclControlFlags(
                daclPresent | daclProtected | daclDefaulted));
            Check.False(SecureStateStore.HasAcceptedDaclControlFlags(
                daclPresent | daclProtected | autoInheritRequired));
        }

        private static void EdidRoundTrip()
        {
            EdidJournal expected = CreateEdidInstallPending();
            byte[] encoded = JournalCodec.Serialize(expected);
            Check.True(encoded.Length < JournalCodec.MaximumJournalBytes);

            EdidJournal actual = JournalCodec.Parse(encoded) as EdidJournal;
            Check.NotNull(actual);
            Check.Equal(EdidJournalState.InstallPending, actual.State);
            Check.Equal(expected.OperationId, actual.OperationId);
            Check.Equal(expected.Generation, actual.Generation);
            Check.Equal(expected.Payload.Target, actual.Payload.Target);
            Check.Equal(expected.Payload.OwnedOverrideHash, actual.Payload.OwnedOverrideHash);
        }

        private static void PowerRoundTrip()
        {
            PowerJournal expected = CreatePowerInactiveRetained();
            PowerJournal actual = JournalCodec.Parse(
                JournalCodec.Serialize(expected)) as PowerJournal;

            Check.NotNull(actual);
            Check.Equal(PowerJournalState.InactiveRetained, actual.State);
            Check.Equal(
                PowerInactiveReason.ExternalSelection,
                actual.Payload.InactiveReason);
            Check.Equal(
                expected.Payload.Target.OriginalSchemeId,
                actual.Payload.Target.OriginalSchemeId);
            Check.Equal(
                expected.Payload.Target.OwnedSchemeId,
                actual.Payload.Target.OwnedSchemeId);
            Check.Equal(
                expected.Payload.Target.ManagedSettingsHash,
                actual.Payload.Target.ManagedSettingsHash);
        }

        private static void PowerStateRoundTrips()
        {
            PowerJournalState[] states =
            {
                PowerJournalState.NotManaged,
                PowerJournalState.Creating,
                PowerJournalState.Applied,
                PowerJournalState.RestorePending,
                PowerJournalState.InactiveRetained,
                PowerJournalState.Conflict
            };

            for (int index = 0; index < states.Length; index++)
            {
                PowerJournalState state = states[index];
                PowerJournalPayload payload =
                    state == PowerJournalState.NotManaged
                        ? null
                        : new PowerJournalPayload(
                            CreatePowerTarget(),
                            state == PowerJournalState.InactiveRetained
                                ? PowerInactiveReason.OriginalAlreadyActive
                                : PowerInactiveReason.None);
                PowerJournal expected = new PowerJournal(
                    new JournalOperationId(Guid.Parse(
                        "99999999-9999-9999-9999-999999999999")),
                    new JournalGeneration((ulong)(index + 1)),
                    Utc(0),
                    Utc(1),
                    state,
                    payload);

                PowerJournal actual = JournalCodec.Parse(
                    JournalCodec.Serialize(expected)) as PowerJournal;
                Check.NotNull(actual);
                Check.Equal(state, actual.State);
                Check.Equal(expected.OperationId, actual.OperationId);
                Check.Equal(expected.Generation, actual.Generation);
                if (state == PowerJournalState.NotManaged)
                {
                    Check.True(actual.Payload == null);
                }
                else
                {
                    Check.Equal(expected.Payload.Target, actual.Payload.Target);
                    Check.Equal(
                        expected.Payload.InactiveReason,
                        actual.Payload.InactiveReason);
                }
            }
        }

        private static void PowerCanonicalValues()
        {
            PowerJournal creating = new PowerJournal(
                new JournalOperationId(Guid.Parse(
                    "cccccccc-cccc-cccc-cccc-cccccccccccc")),
                new JournalGeneration(7),
                Utc(0),
                Utc(1),
                PowerJournalState.Creating,
                new PowerJournalPayload(
                    CreatePowerTarget(),
                    PowerInactiveReason.None));
            byte[] canonical = JournalCodec.Serialize(creating);

            byte[] uppercaseGuid = (byte[])canonical.Clone();
            int originalGuidOffset = FindFieldValueOffset(uppercaseGuid, 1);
            uppercaseGuid[originalGuidOffset] = (byte)'A';
            Check.Throws<JournalFormatException>(delegate
            {
                JournalCodec.Parse(uppercaseGuid);
            });

            byte[] invalidPreset = (byte[])canonical.Clone();
            int presetOffset = FindFieldValueOffset(invalidPreset, 3);
            invalidPreset[presetOffset] = (byte)'X';
            Check.Throws<JournalFormatException>(delegate
            {
                JournalCodec.Parse(invalidPreset);
            });

            PowerJournal inactive = new PowerJournal(
                creating.OperationId,
                creating.Generation.Next(),
                creating.CreatedUtc,
                Utc(2),
                PowerJournalState.InactiveRetained,
                new PowerJournalPayload(
                    creating.Payload.Target,
                    PowerInactiveReason.ExternalSelection));
            byte[] extraInactiveField = JournalCodec.Serialize(inactive);
            extraInactiveField[StateOffset] = (byte)PowerJournalState.Applied;
            Check.Throws<JournalFormatException>(delegate
            {
                JournalCodec.Parse(extraInactiveField);
            });
        }

        private static void PowerTransitionMatrix()
        {
            PowerJournalState[] states =
            {
                PowerJournalState.NotManaged,
                PowerJournalState.Creating,
                PowerJournalState.Applied,
                PowerJournalState.RestorePending,
                PowerJournalState.InactiveRetained,
                PowerJournalState.Conflict
            };
            bool[,] expected =
            {
                { false, true, false, false, false, false },
                { false, true, true, false, false, true },
                { false, true, true, true, true, true },
                { false, false, false, true, true, true },
                { false, true, true, true, true, true },
                { false, false, false, false, false, true }
            };

            for (int currentIndex = 0; currentIndex < states.Length; currentIndex++)
            {
                for (int nextIndex = 0; nextIndex < states.Length; nextIndex++)
                {
                    Check.That(
                        PowerJournal.CanTransition(
                            states[currentIndex],
                            states[nextIndex]) == expected[currentIndex, nextIndex],
                        string.Format(
                            "Unexpected power transition result for {0} -> {1}.",
                            states[currentIndex],
                            states[nextIndex]));

                    bool expectedNewOperation =
                        states[currentIndex] == PowerJournalState.NotManaged &&
                        states[nextIndex] == PowerJournalState.Creating;
                    Check.That(
                        PowerJournal.CanStartNewOperation(
                            states[currentIndex],
                            states[nextIndex]) == expectedNewOperation,
                        string.Format(
                            "Unexpected power new-operation result for {0} -> {1}.",
                            states[currentIndex],
                            states[nextIndex]));
                }
            }

            PowerJournal inactive = CreatePowerInactiveRetained();
            PowerTargetIdentity changedOriginal = inactive.Payload.Target.WithOriginalScheme(
                Guid.Parse("33333333-3333-3333-3333-333333333333"));
            PowerJournal reapplied = inactive.TransitionTo(
                PowerJournalState.Applied,
                new PowerJournalPayload(changedOriginal, PowerInactiveReason.None),
                inactive.Generation.Next(),
                Utc(2));
            Check.Equal(changedOriginal, reapplied.Payload.Target);
            Check.True(inactive.Payload.Target.HasSameOwnedResource(changedOriginal));
            Check.False(inactive.Payload.Target.Equals(changedOriginal));

            PowerJournal reactivationIntent = inactive.TransitionTo(
                PowerJournalState.Creating,
                new PowerJournalPayload(changedOriginal, PowerInactiveReason.None),
                inactive.Generation.Next(),
                Utc(2));
            Check.Equal(changedOriginal, reactivationIntent.Payload.Target);
            Check.Equal(PowerJournalState.Creating, reactivationIntent.State);
            Check.Equal(
                PowerInactiveReason.None,
                reactivationIntent.Payload.InactiveReason);

            PowerTargetIdentity changedOwned = new PowerTargetIdentity(
                changedOriginal.OriginalSchemeId,
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                changedOriginal.Preset,
                changedOriginal.ManagedSettingsHash);
            Check.Throws<ArgumentException>(delegate
            {
                inactive.TransitionTo(
                    PowerJournalState.Applied,
                    new PowerJournalPayload(changedOwned, PowerInactiveReason.None),
                    inactive.Generation.Next(),
                    Utc(2));
            });

            PowerJournal creating = new PowerJournal(
                new JournalOperationId(Guid.Parse(
                    "dddddddd-dddd-dddd-dddd-dddddddddddd")),
                new JournalGeneration(1),
                Utc(0),
                Utc(0),
                PowerJournalState.Creating,
                new PowerJournalPayload(CreatePowerTarget(), PowerInactiveReason.None));
            Check.Throws<ArgumentException>(delegate
            {
                creating.TransitionTo(
                    PowerJournalState.Applied,
                    new PowerJournalPayload(changedOriginal, PowerInactiveReason.None),
                    creating.Generation.Next(),
                    Utc(1));
            });
            Check.Throws<ArgumentException>(delegate
            {
                creating.TransitionTo(
                    PowerJournalState.Applied,
                    creating.Payload,
                    new JournalGeneration(3),
                    Utc(1));
            });
        }

        private static void TypedTransitions()
        {
            DateTime now = Utc(0);
            EdidJournal notInstalled = new EdidJournal(
                new JournalOperationId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                new JournalGeneration(1),
                now,
                now,
                EdidJournalState.NotInstalled,
                null);
            EdidJournalPayload payload = CreateEdidPayload(HashB);
            EdidJournal pending = notInstalled.TransitionTo(
                EdidJournalState.InstallPending,
                payload,
                new JournalGeneration(2),
                Utc(1));
            Check.Equal(EdidJournalState.InstallPending, pending.State);
            Check.Equal(payload, pending.Payload);

            Check.Throws<ArgumentException>(delegate
            {
                pending.TransitionTo(
                    EdidJournalState.Installed,
                    CreateEdidPayload(HashC),
                    new JournalGeneration(3),
                    Utc(2));
            });

            EdidJournal restorePending = pending.TransitionTo(
                EdidJournalState.RestorePending,
                new JournalGeneration(3),
                Utc(2));
            Check.Equal(EdidJournalState.RestorePending, restorePending.State);
            Check.Equal(payload, restorePending.Payload);

            PowerJournal power = CreatePowerInactiveRetained();
            PowerTargetIdentity changedOriginal = power.Payload.Target.WithOriginalScheme(
                Guid.Parse("33333333-3333-3333-3333-333333333333"));
            PowerJournal applied = power.TransitionTo(
                PowerJournalState.Applied,
                new PowerJournalPayload(changedOriginal, PowerInactiveReason.None),
                new JournalGeneration(3),
                Utc(2));
            Check.Equal(PowerJournalState.Applied, applied.State);
            Check.Equal(changedOriginal, applied.Payload.Target);
        }

        private static void UnknownAndDuplicateFieldsRejected()
        {
            byte[] unknown = JournalCodec.Serialize(CreateEdidInstallPending());
            int firstFieldOffset = FieldCountOffset + 1;
            unknown[firstFieldOffset] = 99;
            Check.Throws<JournalFormatException>(delegate
            {
                JournalCodec.Parse(unknown);
            });

            byte[] duplicate = JournalCodec.Serialize(CreateEdidInstallPending());
            int secondFieldOffset = NextFieldOffset(duplicate, firstFieldOffset);
            duplicate[secondFieldOffset] = duplicate[firstFieldOffset];
            Check.Throws<JournalFormatException>(delegate
            {
                JournalCodec.Parse(duplicate);
            });
        }

        private static void FramingErrorsRejected()
        {
            byte[] original = JournalCodec.Serialize(CreateEdidInstallPending());

            byte[] unknownVersion = (byte[])original.Clone();
            unknownVersion[6] = 3;
            Check.Throws<JournalFormatException>(delegate
            {
                JournalCodec.Parse(unknownVersion);
            });

            byte[] truncated = new byte[original.Length - 1];
            Array.Copy(original, truncated, truncated.Length);
            Check.Throws<JournalFormatException>(delegate
            {
                JournalCodec.Parse(truncated);
            });

            byte[] trailing = new byte[original.Length + 1];
            Array.Copy(original, trailing, original.Length);
            trailing[trailing.Length - 1] = 1;
            Check.Throws<JournalFormatException>(delegate
            {
                JournalCodec.Parse(trailing);
            });

            Check.Throws<JournalFormatException>(delegate
            {
                JournalCodec.Parse(new byte[JournalCodec.MaximumJournalBytes + 1]);
            });
        }

        private static void InvalidValuesRejected()
        {
            byte[] invalidState = JournalCodec.Serialize(CreateEdidInstallPending());
            invalidState[StateOffset] = 0x7F;
            Check.Throws<JournalFormatException>(delegate
            {
                JournalCodec.Parse(invalidState);
            });

            byte[] invalidGuid = JournalCodec.Serialize(CreateEdidInstallPending());
            invalidGuid[OperationIdOffset] = (byte)'A';
            Check.Throws<JournalFormatException>(delegate
            {
                JournalCodec.Parse(invalidGuid);
            });

            byte[] invalidHash = JournalCodec.Serialize(CreateEdidInstallPending());
            int normalizedHashOffset = FindFieldValueOffset(invalidHash, 5);
            invalidHash[normalizedHashOffset] = (byte)'a';
            Check.Throws<JournalFormatException>(delegate
            {
                JournalCodec.Parse(invalidHash);
            });

            byte[] impossiblePayload = JournalCodec.Serialize(CreateEdidInstallPending());
            impossiblePayload[StateOffset] = (byte)EdidJournalState.NotInstalled;
            Check.Throws<JournalFormatException>(delegate
            {
                JournalCodec.Parse(impossiblePayload);
            });
        }

        private static void ImpossiblePayloadRejected()
        {
            Check.Throws<ArgumentException>(delegate
            {
                new EdidJournal(
                    new JournalOperationId(Guid.Parse("44444444-4444-4444-4444-444444444444")),
                    new JournalGeneration(1),
                    Utc(0),
                    Utc(0),
                    EdidJournalState.NotInstalled,
                    CreateEdidPayload(HashA));
            });

            Check.Throws<ArgumentException>(delegate
            {
                new PowerJournal(
                    new JournalOperationId(Guid.Parse("55555555-5555-5555-5555-555555555555")),
                    new JournalGeneration(1),
                    Utc(0),
                    Utc(0),
                    PowerJournalState.Applied,
                    new PowerJournalPayload(
                        CreatePowerInactiveRetained().Payload.Target,
                        PowerInactiveReason.ExternalSelection));
            });
        }

        private static EdidJournal CreateEdidInstallPending()
        {
            return new EdidJournal(
                new JournalOperationId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
                new JournalGeneration(17),
                Utc(0),
                Utc(1),
                EdidJournalState.InstallPending,
                CreateEdidPayload(HashB));
        }

        private static EdidJournalPayload CreateEdidPayload(string ownedHash)
        {
            return new EdidJournalPayload(
                new EdidTargetIdentity(
                    "macbookpro16-1-appa044-48hz",
                    "DISPLAY\\APPA044\\5&ABCDEF&0&UID1",
                    "APPA044",
                    "APP",
                    Sha256Digest.ParseCanonical(HashA)),
                Sha256Digest.ParseCanonical(ownedHash));
        }

        private static PowerJournal CreatePowerInactiveRetained()
        {
            return new PowerJournal(
                new JournalOperationId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")),
                new JournalGeneration(2),
                Utc(0),
                Utc(1),
                PowerJournalState.InactiveRetained,
                new PowerJournalPayload(
                    CreatePowerTarget(),
                    PowerInactiveReason.ExternalSelection));
        }

        private static PowerTargetIdentity CreatePowerTarget()
        {
            return new PowerTargetIdentity(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                PowerPresetId.Cool,
                Sha256Digest.ParseCanonical(HashC));
        }

        private static DateTime Utc(int seconds)
        {
            return new DateTime(2026, 7, 29, 12, 0, seconds, DateTimeKind.Utc);
        }

        private static int NextFieldOffset(byte[] bytes, int fieldOffset)
        {
            int length = bytes[fieldOffset + 1] | (bytes[fieldOffset + 2] << 8);
            return fieldOffset + 3 + length;
        }

        private static int FindFieldValueOffset(byte[] bytes, byte expectedId)
        {
            int offset = FieldCountOffset + 1;
            int count = bytes[FieldCountOffset];
            for (int index = 0; index < count; index++)
            {
                byte id = bytes[offset];
                int length = bytes[offset + 1] | (bytes[offset + 2] << 8);
                if (id == expectedId)
                {
                    Check.True(length > 0);
                    return offset + 3;
                }

                offset += 3 + length;
            }

            throw new Exception("Expected test field was not found.");
        }

        private const string HashA =
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        private const string HashB =
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        private const string HashC =
            "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
    }
}
