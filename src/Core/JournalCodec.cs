using System;
using System.Collections.Generic;

namespace MacBookEco.Core
{
    /// <summary>
    /// Canonical in-memory codec for trusted journal bytes.
    ///
    /// Wire layout (all integers little-endian):
    ///   magic[6], format marker[1], kind[1], state[1], reserved[1], generation[8],
    ///   operationId[36 ASCII D GUID], createdUtcTicks[8], updatedUtcTicks[8],
    ///   fieldCount[1], then ordered fields of id[1], length[2], ASCII bytes.
    ///
    /// There is intentionally no permissive map or JSON parser.
    /// The parser accepts only the exact field allowlist for a kind and state.
    /// The parser accepts only this initial canonical format.
    /// </summary>
    public static class JournalCodec
    {
        public const int MaximumJournalBytes = 64 * 1024;

        private const int MagicLength = 6;
        private const int OperationIdLength = 36;
        private const int MaximumFieldCount = 8;
        private const int MaximumFieldBytes = 512;

        private const byte EdidProfileIdField = 1;
        private const byte EdidMonitorInstanceIdField = 2;
        private const byte EdidPanelHardwareIdField = 3;
        private const byte EdidManufacturerCodeField = 4;
        private const byte EdidNormalizedEdidHashField = 5;
        private const byte EdidOwnedOverrideHashField = 6;
        private const byte EdidOriginalOverrideAbsentField = 7;

        private const byte PowerOriginalSchemeIdField = 1;
        private const byte PowerOwnedSchemeIdField = 2;
        private const byte PowerPresetField = 3;
        private const byte PowerManagedSettingsHashField = 4;
        private const byte PowerInactiveReasonField = 5;

        private static readonly byte[] Magic =
        {
            (byte)'M', (byte)'B', (byte)'E', (byte)'J', (byte)'0', 0
        };

        public static byte[] Serialize(JournalEnvelope journal)
        {
            if (journal == null)
            {
                throw new ArgumentNullException(nameof(journal));
            }

            var writer = new JournalWriter();
            writer.WriteBytes(Magic);
            writer.WriteByte((byte)JournalEnvelope.FormatMarkerValue);
            writer.WriteByte((byte)journal.TransactionKind);
            writer.WriteByte(journal.StateCode);
            writer.WriteByte(0);
            writer.WriteUInt64(journal.Generation.Value);
            writer.WriteAscii(journal.OperationId.ToString());
            writer.WriteInt64(journal.CreatedUtc.Ticks);
            writer.WriteInt64(journal.UpdatedUtc.Ticks);

            List<JournalField> fields = BuildFields(journal);
            if (fields.Count > MaximumFieldCount)
            {
                throw new InvalidOperationException(
                    "The journal payload exceeds the configured field limit.");
            }

            writer.WriteByte((byte)fields.Count);
            for (var index = 0; index < fields.Count; index++)
            {
                JournalField field = fields[index];
                if (field.Value.Length == 0 || field.Value.Length > MaximumFieldBytes)
                {
                    throw new InvalidOperationException(
                        "A journal field exceeds the configured length limit.");
                }

                // ReadFields requires strictly ascending IDs. That held only
                // because the BuildFields methods happened to append in ID
                // order; asserting it here means a reordered Add is caught at
                // the write instead of producing bytes this codec refuses to
                // read back.
                if (index > 0 && field.Id <= fields[index - 1].Id)
                {
                    throw new InvalidOperationException(
                        "Journal fields must be written in strictly ascending ID order.");
                }

                writer.WriteByte(field.Id);
                writer.WriteUInt16((ushort)field.Value.Length);
                writer.WriteAscii(field.Value);
            }

            return writer.ToArray();
        }

        public static JournalEnvelope Parse(byte[] serialized)
        {
            ValidateSerializedLength(serialized);

            try
            {
                var reader = new JournalReader(serialized);
                ReadMagic(reader);

                ReadFormatMarker(reader);

                JournalTransactionKind kind = ParseKind(reader.ReadByte("kind"));
                byte stateCode = reader.ReadByte("state");
                if (reader.ReadByte("reserved header byte") != 0)
                {
                    throw new JournalFormatException(
                        "The journal reserved header byte is non-zero.");
                }

                var generation = new JournalGeneration(
                    reader.ReadUInt64("generation"));
                JournalOperationId operationId = JournalOperationId.ParseCanonical(
                    reader.ReadAscii(OperationIdLength, "operation ID"));
                DateTime createdUtc = ReadUtcTimestamp(reader, "created timestamp");
                DateTime updatedUtc = ReadUtcTimestamp(reader, "updated timestamp");
                Dictionary<byte, string> fields = ReadFields(reader);
                if (!reader.IsAtEnd)
                {
                    throw new JournalFormatException(
                        "The journal contains trailing payload bytes.");
                }

                switch (kind)
                {
                    case JournalTransactionKind.Edid:
                        return ParseEdid(
                            stateCode,
                            operationId,
                            generation,
                            createdUtc,
                            updatedUtc,
                            fields);
                    case JournalTransactionKind.Power:
                        return ParsePower(
                            stateCode,
                            operationId,
                            generation,
                            createdUtc,
                            updatedUtc,
                            fields);
                    default:
                        throw new JournalFormatException("The journal kind is invalid.");
                }
            }
            catch (JournalFormatException)
            {
                throw;
            }
            catch (ArgumentException exception)
            {
                throw new JournalFormatException(
                    "The journal violates a semantic invariant.",
                    exception);
            }
            catch (FormatException exception)
            {
                throw new JournalFormatException(
                    "The journal contains a non-canonical value.",
                    exception);
            }
            catch (OverflowException exception)
            {
                throw new JournalFormatException(
                    "The journal contains an out-of-range numeric value.",
                    exception);
            }
        }

        private static List<JournalField> BuildFields(JournalEnvelope journal)
        {
            var fields = new List<JournalField>();
            EdidJournal edid = journal as EdidJournal;
            if (edid != null)
            {
                BuildEdidFields(edid, fields);
                return fields;
            }

            PowerJournal power = journal as PowerJournal;
            if (power != null)
            {
                BuildPowerFields(power, fields);
                return fields;
            }

            throw new ArgumentException(
                "Only concrete EDID and power journal types can be serialized.",
                nameof(journal));
        }

        private static void BuildEdidFields(
            EdidJournal journal,
            IList<JournalField> fields)
        {
            if (journal.State == EdidJournalState.NotInstalled)
            {
                return;
            }

            EdidJournalPayload payload = journal.Payload;
            if (payload == null || payload.Target == null ||
                payload.OwnedOverrideHash == null)
            {
                throw new InvalidOperationException(
                    "The EDID journal payload is incomplete.");
            }

            fields.Add(new JournalField(EdidProfileIdField, payload.Target.ProfileId));
            fields.Add(new JournalField(
                EdidMonitorInstanceIdField,
                payload.Target.MonitorInstanceId));
            fields.Add(new JournalField(
                EdidPanelHardwareIdField,
                payload.Target.PanelHardwareId));
            fields.Add(new JournalField(
                EdidManufacturerCodeField,
                payload.Target.ManufacturerCode));
            fields.Add(new JournalField(
                EdidNormalizedEdidHashField,
                payload.Target.NormalizedEdidHash.ToString()));
            fields.Add(new JournalField(
                EdidOwnedOverrideHashField,
                payload.OwnedOverrideHash.ToString()));
            fields.Add(new JournalField(EdidOriginalOverrideAbsentField, "1"));
        }

        private static void BuildPowerFields(
            PowerJournal journal,
            IList<JournalField> fields)
        {
            if (journal.State == PowerJournalState.NotManaged)
            {
                return;
            }

            PowerJournalPayload payload = journal.Payload;
            if (payload == null || payload.Target == null ||
                payload.Target.ManagedSettingsHash == null)
            {
                throw new InvalidOperationException(
                    "The power journal payload is incomplete.");
            }

            fields.Add(new JournalField(
                PowerOriginalSchemeIdField,
                CanonicalGuid(payload.Target.OriginalSchemeId)));
            fields.Add(new JournalField(
                PowerOwnedSchemeIdField,
                CanonicalGuid(payload.Target.OwnedSchemeId)));
            fields.Add(new JournalField(
                PowerPresetField,
                FormatPreset(payload.Target.Preset)));
            fields.Add(new JournalField(
                PowerManagedSettingsHashField,
                payload.Target.ManagedSettingsHash.ToString()));
            if (journal.State == PowerJournalState.InactiveRetained)
            {
                fields.Add(new JournalField(
                    PowerInactiveReasonField,
                    FormatInactiveReason(payload.InactiveReason)));
            }
        }

        private static EdidJournal ParseEdid(
            byte stateCode,
            JournalOperationId operationId,
            JournalGeneration generation,
            DateTime createdUtc,
            DateTime updatedUtc,
            IDictionary<byte, string> fields)
        {
            EdidJournalState state = ParseEdidState(stateCode);
            if (state == EdidJournalState.NotInstalled)
            {
                RequireExactFields(fields);
                return new EdidJournal(
                    operationId,
                    generation,
                    createdUtc,
                    updatedUtc,
                    state,
                    null);
            }

            RequireExactFields(
                fields,
                EdidProfileIdField,
                EdidMonitorInstanceIdField,
                EdidPanelHardwareIdField,
                EdidManufacturerCodeField,
                EdidNormalizedEdidHashField,
                EdidOwnedOverrideHashField,
                EdidOriginalOverrideAbsentField);
            if (!string.Equals(
                    Required(fields, EdidOriginalOverrideAbsentField),
                    "1",
                    StringComparison.Ordinal))
            {
                throw new JournalFormatException(
                    "The EDID journal must record an absent original override.");
            }

            var target = new EdidTargetIdentity(
                Required(fields, EdidProfileIdField),
                Required(fields, EdidMonitorInstanceIdField),
                Required(fields, EdidPanelHardwareIdField),
                Required(fields, EdidManufacturerCodeField),
                Sha256Digest.ParseCanonical(
                    Required(fields, EdidNormalizedEdidHashField)));
            var payload = new EdidJournalPayload(
                target,
                Sha256Digest.ParseCanonical(
                    Required(fields, EdidOwnedOverrideHashField)));
            return new EdidJournal(
                operationId,
                generation,
                createdUtc,
                updatedUtc,
                state,
                payload);
        }

        private static PowerJournal ParsePower(
            byte stateCode,
            JournalOperationId operationId,
            JournalGeneration generation,
            DateTime createdUtc,
            DateTime updatedUtc,
            IDictionary<byte, string> fields)
        {
            PowerJournalState state = ParsePowerState(stateCode);
            if (state == PowerJournalState.NotManaged)
            {
                RequireExactFields(fields);
                return new PowerJournal(
                    operationId,
                    generation,
                    createdUtc,
                    updatedUtc,
                    state,
                    null);
            }

            if (state == PowerJournalState.InactiveRetained)
            {
                RequireExactFields(
                    fields,
                    PowerOriginalSchemeIdField,
                    PowerOwnedSchemeIdField,
                    PowerPresetField,
                    PowerManagedSettingsHashField,
                    PowerInactiveReasonField);
            }
            else
            {
                RequireExactFields(
                    fields,
                    PowerOriginalSchemeIdField,
                    PowerOwnedSchemeIdField,
                    PowerPresetField,
                    PowerManagedSettingsHashField);
            }

            PowerInactiveReason inactiveReason =
                state == PowerJournalState.InactiveRetained
                    ? ParseInactiveReason(
                        Required(fields, PowerInactiveReasonField))
                    : PowerInactiveReason.None;
            var target = new PowerTargetIdentity(
                ParseCanonicalGuid(
                    Required(fields, PowerOriginalSchemeIdField),
                    "original scheme ID"),
                ParseCanonicalGuid(
                    Required(fields, PowerOwnedSchemeIdField),
                    "owned scheme ID"),
                ParsePreset(Required(fields, PowerPresetField)),
                Sha256Digest.ParseCanonical(
                    Required(fields, PowerManagedSettingsHashField)));
            var payload = new PowerJournalPayload(target, inactiveReason);
            return new PowerJournal(
                operationId,
                generation,
                createdUtc,
                updatedUtc,
                state,
                payload);
        }

        private static void ReadMagic(JournalReader reader)
        {
            for (var index = 0; index < MagicLength; index++)
            {
                if (reader.ReadByte("magic") != Magic[index])
                {
                    throw new JournalFormatException("The journal magic is invalid.");
                }
            }
        }

        private static byte ReadFormatMarker(JournalReader reader)
        {
            byte formatMarker = reader.ReadByte("format marker");
            if (formatMarker != JournalEnvelope.FormatMarkerValue)
            {
                throw new JournalFormatException(
                    "The journal format marker is not supported.");
            }

            return formatMarker;
        }

        private static void ValidateSerializedLength(byte[] serialized)
        {
            if (serialized == null)
            {
                throw new ArgumentNullException(nameof(serialized));
            }

            if (serialized.Length == 0 || serialized.Length > MaximumJournalBytes)
            {
                throw new JournalFormatException(
                    "The journal byte length is outside the allowed range.");
            }
        }

        private static JournalTransactionKind ParseKind(byte value)
        {
            if (value == (byte)JournalTransactionKind.Edid)
            {
                return JournalTransactionKind.Edid;
            }

            if (value == (byte)JournalTransactionKind.Power)
            {
                return JournalTransactionKind.Power;
            }

            throw new JournalFormatException("The journal kind is invalid.");
        }

        private static EdidJournalState ParseEdidState(byte value)
        {
            EdidJournalState state = (EdidJournalState)value;
            if (!EdidJournal.IsKnownState(state))
            {
                throw new JournalFormatException("The EDID journal state is invalid.");
            }

            return state;
        }

        private static PowerJournalState ParsePowerState(byte value)
        {
            PowerJournalState state = (PowerJournalState)value;
            if (!PowerJournal.IsKnownState(state))
            {
                throw new JournalFormatException("The power journal state is invalid.");
            }

            return state;
        }

        private static DateTime ReadUtcTimestamp(JournalReader reader, string field)
        {
            long ticks = reader.ReadInt64(field);
            try
            {
                return new DateTime(ticks, DateTimeKind.Utc);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new JournalFormatException(
                    "The journal " + field + " is invalid.",
                    exception);
            }
        }

        private static Dictionary<byte, string> ReadFields(JournalReader reader)
        {
            byte count = reader.ReadByte("field count");
            if (count > MaximumFieldCount)
            {
                throw new JournalFormatException(
                    "The journal contains too many payload fields.");
            }

            var fields = new Dictionary<byte, string>();
            byte previousId = 0;
            for (var index = 0; index < count; index++)
            {
                byte id = reader.ReadByte("field ID");
                if (id == 0 || id <= previousId || fields.ContainsKey(id))
                {
                    throw new JournalFormatException(
                        "The journal payload contains duplicate or non-canonical fields.");
                }

                ushort length = reader.ReadUInt16("field length");
                if (length == 0 || length > MaximumFieldBytes)
                {
                    throw new JournalFormatException(
                        "The journal field length is outside the allowed range.");
                }

                fields.Add(id, reader.ReadAscii(length, "field value"));
                previousId = id;
            }

            return fields;
        }

        private static void RequireExactFields(
            IDictionary<byte, string> fields,
            params byte[] expected)
        {
            if (fields.Count != expected.Length)
            {
                throw new JournalFormatException(
                    "The journal payload does not have the required field count.");
            }

            for (var index = 0; index < expected.Length; index++)
            {
                if (!fields.ContainsKey(expected[index]))
                {
                    throw new JournalFormatException(
                        "The journal payload contains an unknown or missing field.");
                }
            }
        }

        private static string Required(IDictionary<byte, string> fields, byte id)
        {
            string value;
            if (!fields.TryGetValue(id, out value))
            {
                throw new JournalFormatException("A required journal field is missing.");
            }

            return value;
        }

        private static Guid ParseCanonicalGuid(string value, string field)
        {
            Guid result;
            if (value == null ||
                value.Length != OperationIdLength ||
                !Guid.TryParseExact(value, "D", out result) ||
                result == Guid.Empty ||
                !string.Equals(
                    result.ToString("D"),
                    value,
                    StringComparison.Ordinal))
            {
                throw new JournalFormatException(
                    "The journal " + field + " is not a canonical non-empty GUID.");
            }

            return result;
        }

        private static string CanonicalGuid(Guid value)
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException("A journal GUID cannot be empty.", nameof(value));
            }

            return value.ToString("D");
        }

        private static PowerPresetId ParsePreset(string value)
        {
            if (string.Equals(value, "Normal", StringComparison.Ordinal))
            {
                return PowerPresetId.Normal;
            }

            if (string.Equals(value, "Cool", StringComparison.Ordinal))
            {
                return PowerPresetId.Cool;
            }

            if (string.Equals(value, "MaximumBattery", StringComparison.Ordinal))
            {
                return PowerPresetId.MaximumBattery;
            }

            throw new JournalFormatException("The power preset is invalid.");
        }

        private static string FormatPreset(PowerPresetId preset)
        {
            if (preset == PowerPresetId.Normal)
            {
                return "Normal";
            }

            if (preset == PowerPresetId.Cool)
            {
                return "Cool";
            }

            if (preset == PowerPresetId.MaximumBattery)
            {
                return "MaximumBattery";
            }

            throw new ArgumentOutOfRangeException(nameof(preset));
        }

        private static PowerInactiveReason ParseInactiveReason(string value)
        {
            if (string.Equals(value, "OriginalAlreadyActive", StringComparison.Ordinal))
            {
                return PowerInactiveReason.OriginalAlreadyActive;
            }

            if (string.Equals(value, "ExternalSelection", StringComparison.Ordinal))
            {
                return PowerInactiveReason.ExternalSelection;
            }

            throw new JournalFormatException("The power inactive reason is invalid.");
        }

        private static string FormatInactiveReason(PowerInactiveReason value)
        {
            if (value == PowerInactiveReason.OriginalAlreadyActive)
            {
                return "OriginalAlreadyActive";
            }

            if (value == PowerInactiveReason.ExternalSelection)
            {
                return "ExternalSelection";
            }

            throw new ArgumentOutOfRangeException(nameof(value));
        }

        private sealed class JournalField
        {
            public JournalField(byte id, string value)
            {
                if (id == 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(id));
                }

                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                Id = id;
                Value = value;
            }

            public byte Id { get; private set; }

            public string Value { get; private set; }
        }

        private sealed class JournalWriter
        {
            private readonly List<byte> _bytes = new List<byte>();

            public void WriteByte(byte value)
            {
                _bytes.Add(value);
                EnsureWithinLimit();
            }

            public void WriteBytes(byte[] value)
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                for (var index = 0; index < value.Length; index++)
                {
                    _bytes.Add(value[index]);
                }

                EnsureWithinLimit();
            }

            public void WriteUInt16(ushort value)
            {
                WriteByte((byte)value);
                WriteByte((byte)(value >> 8));
            }

            public void WriteUInt64(ulong value)
            {
                for (var index = 0; index < 8; index++)
                {
                    WriteByte((byte)(value >> (index * 8)));
                }
            }

            public void WriteInt64(long value)
            {
                WriteUInt64(unchecked((ulong)value));
            }

            public void WriteAscii(string value)
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                for (var index = 0; index < value.Length; index++)
                {
                    char character = value[index];
                    if (character < 0x20 || character > 0x7E)
                    {
                        throw new ArgumentException(
                            "Journal fields must be printable ASCII.",
                            nameof(value));
                    }

                    WriteByte((byte)character);
                }
            }

            public byte[] ToArray()
            {
                EnsureWithinLimit();
                return _bytes.ToArray();
            }

            private void EnsureWithinLimit()
            {
                if (_bytes.Count > MaximumJournalBytes)
                {
                    throw new InvalidOperationException(
                        "The serialized journal exceeds 64 KiB.");
                }
            }
        }

        private sealed class JournalReader
        {
            private readonly byte[] _bytes;
            private int _offset;

            public JournalReader(byte[] bytes)
            {
                _bytes = bytes;
            }

            public bool IsAtEnd => _offset == _bytes.Length;

            public byte ReadByte(string field)
            {
                EnsureRemaining(1, field);
                return _bytes[_offset++];
            }

            public ushort ReadUInt16(string field)
            {
                EnsureRemaining(2, field);
                ushort result = (ushort)(
                    _bytes[_offset] |
                    (_bytes[_offset + 1] << 8));
                _offset += 2;
                return result;
            }

            public ulong ReadUInt64(string field)
            {
                EnsureRemaining(8, field);
                ulong result = 0;
                for (var index = 0; index < 8; index++)
                {
                    result |= ((ulong)_bytes[_offset + index]) << (index * 8);
                }

                _offset += 8;
                return result;
            }

            public long ReadInt64(string field)
            {
                return unchecked((long)ReadUInt64(field));
            }

            public string ReadAscii(int length, string field)
            {
                if (length <= 0)
                {
                    throw new JournalFormatException(
                        "The journal " + field + " has an invalid length.");
                }

                EnsureRemaining(length, field);
                var characters = new char[length];
                for (var index = 0; index < length; index++)
                {
                    byte value = _bytes[_offset + index];
                    if (value < 0x20 || value > 0x7E)
                    {
                        throw new JournalFormatException(
                            "The journal " + field + " is not printable ASCII.");
                    }

                    characters[index] = (char)value;
                }

                _offset += length;
                return new string(characters);
            }

            private void EnsureRemaining(int count, string field)
            {
                if (count < 0 || count > _bytes.Length - _offset)
                {
                    throw new JournalFormatException(
                        "The journal is truncated while reading " + field + ".");
                }
            }
        }
    }
}
