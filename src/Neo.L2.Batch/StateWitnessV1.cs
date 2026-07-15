using System.Security.Cryptography;
using Neo.Cryptography;

namespace Neo.L2.Batch;

/// <summary>Protocol constants committed by the N4 genesis SP1 execution profile.</summary>
/// <remarks>See doc.md §8.1–§8.4.</remarks>
public sealed record StateWitnessProtocolConfigV1
{
    /// <summary>Canonical opcode and syscall fee multiplier.</summary>
    public uint ExecFeeFactor { get; init; } = 30;

    /// <summary>Canonical storage price in datoshi per byte.</summary>
    public uint StoragePrice { get; init; } = 100_000;

    /// <summary>Canonical Neo address version.</summary>
    public byte AddressVersion { get; init; } = 0x35;

    /// <summary>Canonical per-transaction gas ceiling.</summary>
    public long PerTransactionGasLimit { get; init; } = 2_000_000_000;
}

/// <summary>One sorted key/value leaf in a complete N4 pre-state snapshot.</summary>
/// <remarks>See doc.md §7.3 and §8.2.</remarks>
public sealed record StateWitnessEntryV1
{
    /// <summary>Canonical full storage key.</summary>
    public required ReadOnlyMemory<byte> Key { get; init; }

    /// <summary>Canonical storage value.</summary>
    public required ReadOnlyMemory<byte> Value { get; init; }

    /// <inheritdoc />
    public bool Equals(StateWitnessEntryV1? other) => other is not null
        && Key.Span.SequenceEqual(other.Key.Span)
        && Value.Span.SequenceEqual(other.Value.Span);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(Key.Span);
        hash.AddBytes(Value.Span);
        return hash.ToHashCode();
    }
}

/// <summary>Immutable deployed-contract descriptor authenticated by the state root.</summary>
/// <remarks>See doc.md §8.2.</remarks>
public sealed record ContractWitnessV1
{
    /// <summary>Neo contract identifier used to derive storage keys.</summary>
    public required int Id { get; init; }

    /// <summary>Neo script hash.</summary>
    public required UInt160 Hash { get; init; }

    /// <summary>Exact executable NeoVM script.</summary>
    public required ReadOnlyMemory<byte> Script { get; init; }

    /// <summary>Deterministic UTF-8 contract manifest JSON.</summary>
    public required ReadOnlyMemory<byte> Manifest { get; init; }

    /// <inheritdoc />
    public bool Equals(ContractWitnessV1? other) => other is not null
        && Id == other.Id
        && Hash.Equals(other.Hash)
        && Script.Span.SequenceEqual(other.Script.Span)
        && Manifest.Span.SequenceEqual(other.Manifest.Span);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Hash);
        hash.AddBytes(Script.Span);
        hash.AddBytes(Manifest.Span);
        return hash.ToHashCode();
    }
}

/// <summary>Complete authenticated pre-state consumed by the SP1 stateful NeoVM guest.</summary>
/// <remarks>See doc.md §8.1–§8.4.</remarks>
public sealed record StateWitnessV1
{
    /// <summary>Wire-format version.</summary>
    public const ushort Version = 1;

    /// <summary>Pinned protocol configuration.</summary>
    public required StateWitnessProtocolConfigV1 ProtocolConfig { get; init; }

    /// <summary>Complete state leaves in strict lexicographic key order.</summary>
    public required IReadOnlyList<StateWitnessEntryV1> Entries { get; init; }

    /// <summary>Contract descriptors in strict lexicographic script-hash order.</summary>
    public required IReadOnlyList<ContractWitnessV1> Contracts { get; init; }

    /// <inheritdoc />
    public bool Equals(StateWitnessV1? other) => other is not null
        && ProtocolConfig.Equals(other.ProtocolConfig)
        && Entries.SequenceEqual(other.Entries)
        && Contracts.SequenceEqual(other.Contracts);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ProtocolConfig);
        foreach (var entry in Entries) hash.Add(entry);
        foreach (var contract in Contracts) hash.Add(contract);
        return hash.ToHashCode();
    }
}

/// <summary>Canonical <c>NEO4STW1</c> serializer shared with <c>neo-execution-core</c>.</summary>
/// <remarks>
/// See doc.md §8.1–§8.4. All integers and lengths are little-endian. Contract descriptors
/// are bound into the state tree through an immutable domain-separated synthetic leaf.
/// </remarks>
public static class StateWitnessV1Serializer
{
    private static ReadOnlySpan<byte> Magic => "NEO4STW1"u8;
    private static ReadOnlySpan<byte> ContractBindingKeySuffix =>
        "neo-n4/contract-binding/v1/"u8;
    private static ReadOnlySpan<byte> ContractBindingHashDomain =>
        "neo-n4/contract-binding/v1\0"u8;

    /// <summary>Maximum complete witness size (128 MiB).</summary>
    public const int MaxEncodedBytes = 128 * 1024 * 1024;

    /// <summary>Maximum number of state entries.</summary>
    public const uint MaxEntries = 65_536;

    /// <summary>Maximum state-key size.</summary>
    public const int MaxKeyBytes = 4_096;

    /// <summary>Maximum state-value size.</summary>
    public const int MaxValueBytes = 1024 * 1024;

    /// <summary>Maximum number of contract descriptors.</summary>
    public const uint MaxContracts = 4_096;

    /// <summary>Maximum contract script size.</summary>
    public const int MaxContractScriptBytes = 1024 * 1024;

    /// <summary>Maximum contract manifest size.</summary>
    public const int MaxContractManifestBytes = ushort.MaxValue;

    /// <summary>Encode a fully validated canonical witness.</summary>
    public static byte[] Encode(StateWitnessV1 witness)
    {
        ArgumentNullException.ThrowIfNull(witness);
        Validate(witness);
        var writer = new CanonicalWireWriter(EstimateSize(witness));
        writer.WriteBytes(Magic);
        writer.WriteUInt16(StateWitnessV1.Version);
        writer.WriteUInt16(0);
        writer.WriteUInt32(witness.ProtocolConfig.ExecFeeFactor);
        writer.WriteUInt32(witness.ProtocolConfig.StoragePrice);
        writer.WriteByte(witness.ProtocolConfig.AddressVersion);
        writer.WriteBytes(stackalloc byte[3]);
        writer.WriteInt64(witness.ProtocolConfig.PerTransactionGasLimit);
        writer.WriteUInt32(checked((uint)witness.Entries.Count));
        foreach (var entry in witness.Entries)
        {
            writer.WriteLengthPrefixedBytes(entry.Key.Span);
            writer.WriteLengthPrefixedBytes(entry.Value.Span);
        }
        writer.WriteUInt32(checked((uint)witness.Contracts.Count));
        foreach (var contract in witness.Contracts)
        {
            writer.WriteInt32(contract.Id);
            writer.WriteUInt160(contract.Hash);
            writer.WriteLengthPrefixedBytes(contract.Script.Span);
            writer.WriteLengthPrefixedBytes(contract.Manifest.Span);
        }
        var encoded = writer.ToArray();
        if (encoded.Length > MaxEncodedBytes)
            throw new ArgumentException(
                $"State witness exceeds {MaxEncodedBytes} bytes", nameof(witness));
        return encoded;
    }

    /// <summary>Decode and fully validate canonical witness bytes.</summary>
    public static StateWitnessV1 Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxEncodedBytes)
            throw new InvalidDataException(
                $"State witness exceeds {MaxEncodedBytes} bytes: {bytes.Length}");
        var reader = new StrictWireReader(bytes);
        reader.RequireMagic(Magic, "state witness");
        if (reader.ReadUInt16("version") != StateWitnessV1.Version)
            throw new InvalidDataException("Unsupported state witness version");
        if (reader.ReadUInt16("flags") != 0)
            throw new InvalidDataException("State witness flags must be zero");
        var config = new StateWitnessProtocolConfigV1
        {
            ExecFeeFactor = reader.ReadUInt32("exec fee factor"),
            StoragePrice = reader.ReadUInt32("storage price"),
            AddressVersion = reader.ReadByte("address version"),
            PerTransactionGasLimit = ReadReservedAndGas(ref reader),
        };
        var entries = new StateWitnessEntryV1[
            reader.ReadBoundedCount(MaxEntries, "state entry count")];
        for (var index = 0; index < entries.Length; index++)
        {
            entries[index] = new StateWitnessEntryV1
            {
                Key = reader.ReadLengthPrefixedBytes(MaxKeyBytes, $"state entry {index} key"),
                Value = reader.ReadLengthPrefixedBytes(
                    MaxValueBytes, $"state entry {index} value"),
            };
        }
        var contracts = new ContractWitnessV1[
            reader.ReadBoundedCount(MaxContracts, "contract count")];
        for (var index = 0; index < contracts.Length; index++)
        {
            contracts[index] = new ContractWitnessV1
            {
                Id = reader.ReadInt32($"contract {index} id"),
                Hash = reader.ReadUInt160($"contract {index} hash"),
                Script = reader.ReadLengthPrefixedBytes(
                    MaxContractScriptBytes, $"contract {index} script"),
                Manifest = reader.ReadLengthPrefixedBytes(
                    MaxContractManifestBytes, $"contract {index} manifest"),
            };
        }
        reader.EnsureEnd("state witness");
        var witness = new StateWitnessV1
        {
            ProtocolConfig = config,
            Entries = entries,
            Contracts = contracts,
        };
        try
        {
            Validate(witness);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("State witness is not canonical", exception);
        }
        return witness;
    }

    /// <summary>Build the synthetic state key authenticating one contract descriptor.</summary>
    public static byte[] ContractBindingKey(UInt160 contractHash)
    {
        ArgumentNullException.ThrowIfNull(contractHash);
        var key = new byte[1 + ContractBindingKeySuffix.Length + UInt160.Length];
        key[0] = 0xff;
        ContractBindingKeySuffix.CopyTo(key.AsSpan(1));
        contractHash.GetSpan().CopyTo(key.AsSpan(1 + ContractBindingKeySuffix.Length));
        return key;
    }

    /// <summary>Compute the synthetic state value authenticating one contract descriptor.</summary>
    public static UInt256 ContractBindingHash(ContractWitnessV1 contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(contract.Hash);
        var writer = new CanonicalWireWriter(checked(
            ContractBindingHashDomain.Length + 4 + UInt160.Length
            + 4 + contract.Script.Length + 4 + contract.Manifest.Length));
        writer.WriteBytes(ContractBindingHashDomain);
        writer.WriteInt32(contract.Id);
        writer.WriteUInt160(contract.Hash);
        writer.WriteLengthPrefixedBytes(contract.Script.Span);
        writer.WriteLengthPrefixedBytes(contract.Manifest.Span);
        return new UInt256(Crypto.Hash256(writer.ToArray()));
    }

    private static long ReadReservedAndGas(ref StrictWireReader reader)
    {
        if (!reader.ReadBytes(3, "reserved bytes").SequenceEqual(stackalloc byte[3]))
            throw new InvalidDataException("State witness reserved bytes must be zero");
        return reader.ReadInt64("per-transaction gas limit");
    }

    private static int EstimateSize(StateWitnessV1 witness)
    {
        var size = 8 + 2 + 2 + 4 + 4 + 1 + 3 + 8 + 4 + 4;
        foreach (var entry in witness.Entries)
            size = checked(size + 4 + entry.Key.Length + 4 + entry.Value.Length);
        foreach (var contract in witness.Contracts)
            size = checked(size + 4 + UInt160.Length
                + 4 + contract.Script.Length + 4 + contract.Manifest.Length);
        return size;
    }

    private static void Validate(StateWitnessV1 witness)
    {
        ArgumentNullException.ThrowIfNull(witness.ProtocolConfig);
        ArgumentNullException.ThrowIfNull(witness.Entries);
        ArgumentNullException.ThrowIfNull(witness.Contracts);
        var config = witness.ProtocolConfig;
        if (config.ExecFeeFactor != 30
            || config.StoragePrice != 100_000
            || config.AddressVersion != 0x35
            || config.PerTransactionGasLimit != 2_000_000_000)
            throw new ArgumentException(
                "State witness protocol configuration differs from N4 genesis V1",
                nameof(witness));
        if (witness.Entries.Count is 0 || (uint)witness.Entries.Count > MaxEntries)
            throw new ArgumentException("State witness entry count is invalid", nameof(witness));
        if (witness.Contracts.Count is 0 || (uint)witness.Contracts.Count > MaxContracts)
            throw new ArgumentException("State witness contract count is invalid", nameof(witness));

        ReadOnlySpan<byte> previousKey = default;
        foreach (var entry in witness.Entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.Key.IsEmpty || entry.Key.Length > MaxKeyBytes
                || entry.Value.Length > MaxValueBytes
                || (!previousKey.IsEmpty && previousKey.SequenceCompareTo(entry.Key.Span) >= 0))
                throw new ArgumentException(
                    "State witness entries must be bounded and strictly key-sorted",
                    nameof(witness));
            previousKey = entry.Key.Span;
        }

        ReadOnlySpan<byte> previousHash = default;
        var ids = new HashSet<int>();
        foreach (var contract in witness.Contracts)
        {
            ArgumentNullException.ThrowIfNull(contract);
            ArgumentNullException.ThrowIfNull(contract.Hash);
            if (!ids.Add(contract.Id)
                || contract.Script.IsEmpty
                || contract.Script.Length > MaxContractScriptBytes
                || contract.Manifest.IsEmpty
                || contract.Manifest.Length > MaxContractManifestBytes
                || (!previousHash.IsEmpty
                    && previousHash.SequenceCompareTo(contract.Hash.GetSpan()) >= 0))
                throw new ArgumentException(
                    "Contract witnesses must be unique, bounded, and strictly hash-sorted",
                    nameof(witness));
            previousHash = contract.Hash.GetSpan();
            var key = ContractBindingKey(contract.Hash);
            var index = BinarySearch(witness.Entries, key);
            if (index < 0
                || !witness.Entries[index].Value.Span.SequenceEqual(
                    ContractBindingHash(contract).GetSpan()))
                throw new ArgumentException(
                    "Contract witness is not authenticated by its state binding leaf",
                    nameof(witness));
        }
    }

    private static int BinarySearch(IReadOnlyList<StateWitnessEntryV1> entries, byte[] key)
    {
        var low = 0;
        var high = entries.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = entries[middle].Key.Span.SequenceCompareTo(key);
            if (comparison == 0) return middle;
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        return -1;
    }
}
