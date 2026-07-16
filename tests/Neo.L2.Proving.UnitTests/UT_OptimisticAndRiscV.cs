using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.L2.Batch;
using Neo.L2.Proving.Optimistic;
using Neo.L2.Proving.RiscVZk;
using Neo.L2.State;
using Neo.SmartContract;

namespace Neo.L2.Proving.UnitTests;

[TestClass]
public class UT_OptimisticAndRiscV
{
    private static PublicInputs SamplePublicInputs() => new()
    {
        ChainId = 1001,
        BatchNumber = 7,
        PreStateRoot = UInt256.Parse("0x" + new string('1', 64)),
        PostStateRoot = UInt256.Parse("0x" + new string('2', 64)),
        TxRoot = UInt256.Parse("0x" + new string('3', 64)),
        ReceiptRoot = UInt256.Parse("0x" + new string('4', 64)),
        WithdrawalRoot = UInt256.Parse("0x" + new string('5', 64)),
        L2ToL1MessageRoot = UInt256.Parse("0x" + new string('6', 64)),
        L2ToL2MessageRoot = UInt256.Parse("0x" + new string('7', 64)),
        L1MessageHash = UInt256.Parse("0x" + new string('8', 64)),
        DACommitment = UInt256.Parse("0x" + new string('9', 64)),
        BlockContextHash = UInt256.Parse("0x" + new string('a', 64)),
    };

    private static UInt160 SampleSequencer() => UInt160.Parse("0x" + new string('d', 40));

    private static UInt160 SequencerAccount(ECPoint pubKey) =>
        Contract.CreateSignatureRedeemScript(pubKey).ToScriptHash();

    [TestMethod]
    public async Task Optimistic_VerifierAcceptsValidSequencerSig()
    {
        var priv = new byte[32];
        for (var i = 0; i < 32; i++) priv[i] = (byte)(i + 1);
        var pub = ECCurve.Secp256r1.G * priv;

        var inputs = SamplePublicInputs();
        var canonical = BatchSerializer.EncodePublicInputs(inputs);
        var sig = Crypto.Sign(canonical, new Neo.Wallets.KeyPair(priv));

        var payload = new OptimisticProofPayload
        {
            BondContract = UInt160.Parse("0x" + new string('b', 40)),
            BondTxHash = UInt256.Parse("0x" + new string('c', 64)),
            SubmittedAt = 1_700_000_000_000,
            Sequencer = SequencerAccount(pub),
            SequencerSignature = sig,
        };

        var verifier = new OptimisticVerifier(pub);
        var result = await verifier.VerifyAsync(inputs, payload.Encode());
        Assert.IsTrue(result.Valid, result.FailureReason);
    }

    [TestMethod]
    public async Task OptimisticProver_Prove_RoundTripsThroughVerifier()
    {
        var priv = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        var key = new Neo.Wallets.KeyPair(priv);
        try
        {
            var bondContract = UInt160.Parse("0x" + new string('b', 40));
            var bondTx = UInt256.Parse("0x" + new string('c', 64));
            const ulong submittedAt = 1_700_000_000_000UL;
            var prover = new OptimisticProver(
                key,
                bondContract,
                bondTx,
                submittedAtUnixMs: static () => submittedAt);
            Assert.AreEqual(ProofType.Optimistic, prover.Kind);
            Assert.AreEqual(SequencerAccount(key.PublicKey), prover.SequencerAccount);

            var inputs = SamplePublicInputs();
            var result = await prover.ProveAsync(new ProofRequest
            {
                PublicInputs = inputs,
                Witness = Array.Empty<byte>(),
                Kind = ProofType.Optimistic,
            });

            Assert.AreEqual(ProofType.Optimistic, result.Kind);
            Assert.AreEqual(StateRootCalculator.HashPublicInputs(inputs), result.PublicInputHash);
            var payload = OptimisticProofPayload.Decode(result.Proof.Span);
            Assert.AreEqual(bondContract, payload.BondContract);
            Assert.AreEqual(bondTx, payload.BondTxHash);
            Assert.AreEqual(submittedAt, payload.SubmittedAt);
            Assert.AreEqual(prover.SequencerAccount, payload.Sequencer);
            Assert.AreEqual(64, payload.SequencerSignature.Length);

            var verified = await new OptimisticVerifier(key.PublicKey)
                .VerifyAsync(inputs, result.Proof);
            Assert.IsTrue(verified.Valid, verified.FailureReason);
        }
        finally
        {
            key.PrivateKey.AsSpan().Clear();
            priv.AsSpan().Clear();
        }
    }

    [TestMethod]
    public async Task OptimisticProver_RejectsWrongProofTypeAndZeroBond()
    {
        var priv = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        var key = new Neo.Wallets.KeyPair(priv);
        try
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                new OptimisticProver(key, UInt160.Zero, UInt256.Parse("0x" + new string('c', 64))));
            Assert.ThrowsExactly<ArgumentException>(() =>
                new OptimisticProver(
                    key,
                    UInt160.Parse("0x" + new string('b', 40)),
                    UInt256.Zero));

            var prover = new OptimisticProver(
                key,
                UInt160.Parse("0x" + new string('b', 40)),
                UInt256.Parse("0x" + new string('c', 64)));
            await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
                await prover.ProveAsync(new ProofRequest
                {
                    PublicInputs = SamplePublicInputs(),
                    Witness = Array.Empty<byte>(),
                    Kind = ProofType.Multisig,
                }));
        }
        finally
        {
            key.PrivateKey.AsSpan().Clear();
            priv.AsSpan().Clear();
        }
    }

    [TestMethod]
    public async Task Optimistic_VerifierRejectsBadSignature()
    {
        var priv = new byte[32]; priv[0] = 1;
        var realPub = ECCurve.Secp256r1.G * priv;
        var fakePriv = new byte[32]; fakePriv[0] = 2;

        var inputs = SamplePublicInputs();
        var canonical = BatchSerializer.EncodePublicInputs(inputs);
        var sig = Crypto.Sign(canonical, new Neo.Wallets.KeyPair(fakePriv)); // signed with the wrong key

        var payload = new OptimisticProofPayload
        {
            BondContract = UInt160.Zero,
            BondTxHash = UInt256.Zero,
            SubmittedAt = 0,
            Sequencer = SequencerAccount(realPub),
            SequencerSignature = sig,
        };

        var result = await new OptimisticVerifier(realPub).VerifyAsync(inputs, payload.Encode());
        Assert.IsFalse(result.Valid);
    }

    [TestMethod]
    public async Task Optimistic_VerifierRejectsSequencerAccountMismatch()
    {
        var priv = new byte[32]; priv[0] = 1;
        var pub = ECCurve.Secp256r1.G * priv;

        var inputs = SamplePublicInputs();
        var canonical = BatchSerializer.EncodePublicInputs(inputs);
        var sig = Crypto.Sign(canonical, new Neo.Wallets.KeyPair(priv));

        var payload = new OptimisticProofPayload
        {
            BondContract = UInt160.Parse("0x" + new string('b', 40)),
            BondTxHash = UInt256.Parse("0x" + new string('c', 64)),
            SubmittedAt = 1_700_000_000_000,
            Sequencer = UInt160.Parse("0x" + new string('e', 40)),
            SequencerSignature = sig,
        };

        var result = await new OptimisticVerifier(pub).VerifyAsync(inputs, payload.Encode());
        Assert.IsFalse(result.Valid);
        StringAssert.Contains(result.FailureReason ?? "", "sequencer account");
    }

    [TestMethod]
    public async Task RiscV_MockProverVerifierRoundTrip()
    {
        var vkId = UInt256.Parse("0x" + new string('f', 64));
        var prover = new MockRiscVProver(vkId);
        var verifier = new MockRiscVVerifier(vkId);

        var inputs = SamplePublicInputs();
        var result = await prover.ProveAsync(new ProofRequest
        {
            PublicInputs = inputs,
            Witness = ReadOnlyMemory<byte>.Empty,
            Kind = ProofType.Zk,
        });

        Assert.AreEqual(ProofType.Zk, result.Kind);
        var verify = await verifier.VerifyAsync(inputs, result.Proof);
        Assert.IsTrue(verify.Valid, verify.FailureReason);
    }

    [TestMethod]
    public async Task RiscV_MockVerifierRejectsWrongVk()
    {
        var goodVk = UInt256.Parse("0x" + new string('a', 64));
        var badVk = UInt256.Parse("0x" + new string('b', 64));
        var prover = new MockRiscVProver(goodVk);
        var verifier = new MockRiscVVerifier(badVk);

        var inputs = SamplePublicInputs();
        var result = await prover.ProveAsync(new ProofRequest { PublicInputs = inputs, Witness = ReadOnlyMemory<byte>.Empty, Kind = ProofType.Zk });
        var verify = await verifier.VerifyAsync(inputs, result.Proof);
        Assert.IsFalse(verify.Valid);
    }

    [TestMethod]
    public async Task Registry_DispatchesByKind()
    {
        var vkId = UInt256.Parse("0x" + new string('f', 64));
        var registry = new VerifierRegistry();
        registry.Register(new MockRiscVVerifier(vkId));
        Assert.AreEqual(1, registry.Count);
        Assert.IsTrue(registry.IsRegistered(ProofType.Zk));

        var prover = new MockRiscVProver(vkId);
        var inputs = SamplePublicInputs();
        var proof = await prover.ProveAsync(new ProofRequest { PublicInputs = inputs, Witness = ReadOnlyMemory<byte>.Empty, Kind = ProofType.Zk });

        var commitment = new L2BatchCommitment
        {
            ChainId = inputs.ChainId,
            BatchNumber = inputs.BatchNumber,
            FirstBlock = 100,
            LastBlock = 200,
            PreStateRoot = inputs.PreStateRoot,
            PostStateRoot = inputs.PostStateRoot,
            TxRoot = inputs.TxRoot,
            ReceiptRoot = inputs.ReceiptRoot,
            WithdrawalRoot = inputs.WithdrawalRoot,
            L2ToL1MessageRoot = inputs.L2ToL1MessageRoot,
            L2ToL2MessageRoot = inputs.L2ToL2MessageRoot,
            DACommitment = inputs.DACommitment,
            PublicInputHash = proof.PublicInputHash,
            ProofType = ProofType.Zk,
            Proof = proof.Proof,
        };

        var verify = await registry.VerifyAsync(commitment, inputs);
        Assert.IsTrue(verify.Valid, verify.FailureReason);
    }

    [TestMethod]
    public async Task Registry_FailsWhenCommitmentDisagreesWithInputs()
    {
        var vkId = UInt256.Parse("0x" + new string('f', 64));
        var registry = new VerifierRegistry();
        registry.Register(new MockRiscVVerifier(vkId));

        var prover = new MockRiscVProver(vkId);
        var inputs = SamplePublicInputs();
        var proof = await prover.ProveAsync(new ProofRequest { PublicInputs = inputs, Witness = ReadOnlyMemory<byte>.Empty, Kind = ProofType.Zk });

        var commitment = new L2BatchCommitment
        {
            ChainId = inputs.ChainId,
            BatchNumber = 999,           // mismatch
            FirstBlock = 100,
            LastBlock = 200,
            PreStateRoot = inputs.PreStateRoot,
            PostStateRoot = inputs.PostStateRoot,
            TxRoot = inputs.TxRoot,
            ReceiptRoot = inputs.ReceiptRoot,
            WithdrawalRoot = inputs.WithdrawalRoot,
            L2ToL1MessageRoot = inputs.L2ToL1MessageRoot,
            L2ToL2MessageRoot = inputs.L2ToL2MessageRoot,
            DACommitment = inputs.DACommitment,
            PublicInputHash = proof.PublicInputHash,
            ProofType = ProofType.Zk,
            Proof = proof.Proof,
        };

        var verify = await registry.VerifyAsync(commitment, inputs);
        Assert.IsFalse(verify.Valid);
    }

    [TestMethod]
    public async Task Registry_FailsWhenPublicInputHashIsForged()
    {
        // Regression: previously VerifierRegistry only compared 10 commitment fields
        // against publicInputs but never re-derived publicInputs's hash. A malicious
        // submission could set commitment.PublicInputHash to an arbitrary value (the
        // attacker plans to claim it later in a forged replay) while supplying a real
        // publicInputs that the verifier accepts. Now the registry catches the
        // forgery with "commitment.PublicInputHash != hash(publicInputs)".
        var vkId = UInt256.Parse("0x" + new string('f', 64));
        var registry = new VerifierRegistry();
        registry.Register(new MockRiscVVerifier(vkId));

        var prover = new MockRiscVProver(vkId);
        var inputs = SamplePublicInputs();
        var proof = await prover.ProveAsync(new ProofRequest { PublicInputs = inputs, Witness = ReadOnlyMemory<byte>.Empty, Kind = ProofType.Zk });

        var forged = new L2BatchCommitment
        {
            ChainId = inputs.ChainId,
            BatchNumber = inputs.BatchNumber,
            FirstBlock = 100,
            LastBlock = 200,
            PreStateRoot = inputs.PreStateRoot,
            PostStateRoot = inputs.PostStateRoot,
            TxRoot = inputs.TxRoot,
            ReceiptRoot = inputs.ReceiptRoot,
            WithdrawalRoot = inputs.WithdrawalRoot,
            L2ToL1MessageRoot = inputs.L2ToL1MessageRoot,
            L2ToL2MessageRoot = inputs.L2ToL2MessageRoot,
            DACommitment = inputs.DACommitment,
            PublicInputHash = UInt256.Parse("0x" + new string('e', 64)),  // forged
            ProofType = ProofType.Zk,
            Proof = proof.Proof,
        };

        var verify = await registry.VerifyAsync(forged, inputs);
        Assert.IsFalse(verify.Valid);
        StringAssert.Contains(verify.FailureReason ?? "", "PublicInputHash");
    }

    [TestMethod]
    public void OptimisticProofPayload_ByteLayout_MatchesDocumentedOffsets()
    {
        // Pins the layout claimed in OptimisticProofPayload's XML docs.
        var bond = UInt160.Parse("0x" + new string('a', 40));
        var bondTx = UInt256.Parse("0x" + new string('b', 64));
        var sig = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };

        var payload = new OptimisticProofPayload
        {
            BondContract = bond,
            BondTxHash = bondTx,
            SubmittedAt = 0x1122334455667788,
            Sequencer = SampleSequencer(),
            SequencerSignature = sig,
        };
        var bytes = payload.Encode();

        Assert.AreEqual(85 + sig.Length, bytes.Length);
        Assert.AreEqual(OptimisticProofPayload.Version, bytes[0]);
        CollectionAssert.AreEqual(bond.GetSpan().ToArray(), bytes[1..21]);
        CollectionAssert.AreEqual(bondTx.GetSpan().ToArray(), bytes[21..53]);
        Assert.AreEqual(0x1122334455667788UL, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(53, 8)));
        CollectionAssert.AreEqual(SampleSequencer().GetSpan().ToArray(), bytes[61..81]);
        Assert.AreEqual(sig.Length, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(81, 4)));
        CollectionAssert.AreEqual(sig, bytes[85..]);
    }

    [TestMethod]
    public void OptimisticProofPayload_ValueSemanticsAndHashCodeBindEveryField()
    {
        var payload = new OptimisticProofPayload
        {
            BondContract = UInt160.Parse("0x" + new string('b', 40)),
            BondTxHash = UInt256.Parse("0x" + new string('c', 64)),
            SubmittedAt = 1_700_000_000,
            Sequencer = SampleSequencer(),
            SequencerSignature = new byte[] { 1, 2, 3 },
        };
        var equal = OptimisticProofPayload.Decode(payload.Encode());

        Assert.AreEqual(payload, equal);
        Assert.AreEqual(payload.GetHashCode(), equal.GetHashCode());
        Assert.AreNotEqual(payload, null);
        Assert.AreNotEqual(payload, payload with { BondContract = UInt160.Zero });
        Assert.AreNotEqual(payload, payload with { BondTxHash = UInt256.Zero });
        Assert.AreNotEqual(payload, payload with { SubmittedAt = payload.SubmittedAt + 1 });
        Assert.AreNotEqual(payload, payload with { Sequencer = UInt160.Parse("0x" + new string('e', 40)) });
        Assert.AreNotEqual(payload, payload with { SequencerSignature = new byte[] { 3, 2, 1 } });
    }

    [TestMethod]
    public async Task OptimisticVerifier_RejectsMalformedAndWrongLengthProofs()
    {
        var privateKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var publicKey = ECCurve.Secp256r1.G * privateKey;
        var verifier = new OptimisticVerifier(publicKey);

        Assert.AreEqual(ProofType.Optimistic, verifier.Kind);
        var malformed = await verifier.VerifyAsync(SamplePublicInputs(), new byte[] { 1 });
        Assert.IsFalse(malformed.Valid);

        var wrongLength = new OptimisticProofPayload
        {
            BondContract = UInt160.Zero,
            BondTxHash = UInt256.Zero,
            SubmittedAt = 0,
            Sequencer = SequencerAccount(publicKey),
            SequencerSignature = new byte[] { 1 },
        };
        var wrongLengthResult = await verifier.VerifyAsync(
            SamplePublicInputs(), wrongLength.Encode());
        Assert.IsFalse(wrongLengthResult.Valid);
    }

    [TestMethod]
    public void OptimisticProofPayload_Decode_RejectsOversizedSigLen()
    {
        var inner = new OptimisticProofPayload
        {
            BondContract = UInt160.Zero,
            BondTxHash = UInt256.Zero,
            SubmittedAt = 0,
            Sequencer = SampleSequencer(),
            SequencerSignature = new byte[64],
        };
        var bytes = inner.Encode();
        var oversized = OptimisticProofPayload.MaxSignatureBytes + 1;
        var bigBuf = new byte[85 + oversized];
        Array.Copy(bytes, bigBuf, 85);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bigBuf.AsSpan(81, 4), oversized);

        Assert.ThrowsExactly<InvalidDataException>(() => OptimisticProofPayload.Decode(bigBuf));
    }

    [TestMethod]
    public void RiscVProofPayload_Decode_RejectsUnknownProofSystem()
    {
        // Regression: previously bytes[1] was cast (ProofSystem) without bounds-checking.
        // A corrupted or replayed-from-future payload with a discriminant > 4 would slip
        // through as an undefined enum value, and a downstream verifier dispatcher's
        // `==` comparison would silently treat it as "not the expected one".
        var inner = new RiscVProofPayload
        {
            ProofSystem = ProofSystem.Sp1,
            VerificationKeyId = UInt256.Zero,
            ProofBytes = new byte[8],
        };
        var bytes = inner.Encode();
        bytes[1] = 99; // overwrite ProofSystem byte with an out-of-range value
        var ex = Assert.ThrowsExactly<InvalidDataException>(() => RiscVProofPayload.Decode(bytes));
        StringAssert.Contains(ex.Message, "Unknown ProofSystem");
    }

    [TestMethod]
    public void RiscVProofPayload_Decode_AcceptsAllValidProofSystems()
    {
        // Boundary partner: every defined enum byte (0..4) round-trips.
        foreach (ProofSystem ps in Enum.GetValues<ProofSystem>())
        {
            var inner = new RiscVProofPayload
            {
                ProofSystem = ps,
                VerificationKeyId = UInt256.Zero,
                ProofBytes = new byte[8],
            };
            var bytes = inner.Encode();
            var decoded = RiscVProofPayload.Decode(bytes);
            Assert.AreEqual(ps, decoded.ProofSystem);
        }
    }

    [TestMethod]
    public void RiscVProofPayload_Decode_RejectsOversizedProofLen()
    {
        var inner = new RiscVProofPayload
        {
            ProofSystem = ProofSystem.Sp1,
            VerificationKeyId = UInt256.Zero,
            ProofBytes = new byte[8],
        };
        var bytes = inner.Encode();
        var oversized = RiscVProofPayload.MaxProofBytes + 1;
        var bigBuf = new byte[38 + oversized];
        Array.Copy(bytes, bigBuf, 38);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bigBuf.AsSpan(34, 4), oversized);

        Assert.ThrowsExactly<InvalidDataException>(() => RiscVProofPayload.Decode(bigBuf));
    }

    [TestMethod]
    public void OptimisticProofPayload_AcceptsExactlyMaxSignatureBytes()
    {
        // Boundary case: exactly MaxSignatureBytes must succeed. Pairs with
        // RejectsOversizedSigLen above to pin the limit on both sides.
        var sig = new byte[OptimisticProofPayload.MaxSignatureBytes];
        var inner = new OptimisticProofPayload
        {
            BondContract = UInt160.Zero,
            BondTxHash = UInt256.Zero,
            SubmittedAt = 0,
            Sequencer = SampleSequencer(),
            SequencerSignature = sig,
        };
        var bytes = inner.Encode();
        var decoded = OptimisticProofPayload.Decode(bytes);
        Assert.AreEqual(OptimisticProofPayload.MaxSignatureBytes, decoded.SequencerSignature.Length);
    }

    [TestMethod]
    public void RiscVProofPayload_AcceptsExactlyMaxProofBytes()
    {
        var proof = new byte[RiscVProofPayload.MaxProofBytes];
        var inner = new RiscVProofPayload
        {
            ProofSystem = ProofSystem.Sp1,
            VerificationKeyId = UInt256.Zero,
            ProofBytes = proof,
        };
        var bytes = inner.Encode();
        var decoded = RiscVProofPayload.Decode(bytes);
        Assert.AreEqual(RiscVProofPayload.MaxProofBytes, decoded.ProofBytes.Length);
    }

    [TestMethod]
    public void OptimisticProofPayload_Encode_RejectsNullBondContract()
    {
        // BondContract is `required` UInt160 (reference type) — the keyword forces "must
        // be set," not "non-null." Without OptimisticProofPayload.cs:51's
        // ArgumentNullException.ThrowIfNull, Encode would NRE inside BondContract.GetSpan().
        var payload = new OptimisticProofPayload
        {
            BondContract = null!,
            BondTxHash = UInt256.Zero,
            SubmittedAt = 0,
            Sequencer = SampleSequencer(),
            SequencerSignature = new byte[64],
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => payload.Encode());
    }

    [TestMethod]
    public void OptimisticProofPayload_Encode_RejectsNullBondTxHash()
    {
        var payload = new OptimisticProofPayload
        {
            BondContract = UInt160.Zero,
            BondTxHash = null!,
            SubmittedAt = 0,
            Sequencer = SampleSequencer(),
            SequencerSignature = new byte[64],
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => payload.Encode());
    }

    [TestMethod]
    public void RiscVProofPayload_Encode_RejectsNullVerificationKeyId()
    {
        // Pinning RiscVProofPayload.cs:45's ArgumentNullException.ThrowIfNull. Without
        // the guard a null VerificationKeyId NREs inside its GetSpan call. (The Sp1*
        // and Mock* ctor guards added in iter 198 surface this earlier in production —
        // before constructing the payload — but a direct caller of Encode bypasses
        // those, so the per-field guard at the payload level still earns its keep.)
        var payload = new RiscVProofPayload
        {
            ProofSystem = ProofSystem.Sp1,
            VerificationKeyId = null!,
            ProofBytes = new byte[1],
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => payload.Encode());
    }

    [TestMethod]
    public void OptimisticProofPayload_Encode_RejectsOversizedSig()
    {
        // Regression for iter 159: Encode/Decode symmetry. Without this Encode-side
        // check, a producer could create bytes the Decode would refuse — the failure
        // would surface only at the next consumer (e.g., a verifier downstream),
        // hiding the producer-side bug.
        var oversized = new byte[OptimisticProofPayload.MaxSignatureBytes + 1];
        var payload = new OptimisticProofPayload
        {
            BondContract = UInt160.Zero,
            BondTxHash = UInt256.Zero,
            SubmittedAt = 0,
            Sequencer = SampleSequencer(),
            SequencerSignature = oversized,
        };
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => payload.Encode());
        StringAssert.Contains(ex.Message, "MaxSignatureBytes");
    }

    [TestMethod]
    public void RiscVProofPayload_Encode_RejectsOversizedProof()
    {
        // Regression for iter 159: same Encode/Decode symmetry pattern as Optimistic.
        var oversized = new byte[RiscVProofPayload.MaxProofBytes + 1];
        var payload = new RiscVProofPayload
        {
            ProofSystem = ProofSystem.Sp1,
            VerificationKeyId = UInt256.Zero,
            ProofBytes = oversized,
        };
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => payload.Encode());
        StringAssert.Contains(ex.Message, "MaxProofBytes");
    }

    [TestMethod]
    public void RiscVProofPayload_ByteLayout_MatchesDocumentedOffsets()
    {
        // Pins the layout claimed in RiscVProofPayload's XML docs.
        var vk = UInt256.Parse("0x" + new string('c', 64));
        var proofBytes = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 };

        var payload = new RiscVProofPayload
        {
            ProofSystem = ProofSystem.Sp1,
            ProofBytes = proofBytes,
            VerificationKeyId = vk,
        };
        var bytes = payload.Encode();

        Assert.AreEqual(38 + proofBytes.Length, bytes.Length);
        Assert.AreEqual(RiscVProofPayload.Version, bytes[0]);
        Assert.AreEqual((byte)ProofSystem.Sp1, bytes[1]);
        CollectionAssert.AreEqual(vk.GetSpan().ToArray(), bytes[2..34]);
        Assert.AreEqual(proofBytes.Length, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(34, 4)));
        CollectionAssert.AreEqual(proofBytes, bytes[38..]);
    }

    [TestMethod]
    public void RiscVProofPayload_Equality_UsesProofByteContent()
    {
        var verificationKeyId = UInt256.Parse("0x" + new string('c', 64));
        var left = new RiscVProofPayload
        {
            ProofSystem = ProofSystem.Sp1,
            ProofBytes = new byte[] { 0x11, 0x22, 0x33 },
            VerificationKeyId = verificationKeyId,
        };
        var equal = new RiscVProofPayload
        {
            ProofSystem = ProofSystem.Sp1,
            ProofBytes = new byte[] { 0x11, 0x22, 0x33 },
            VerificationKeyId = new UInt256(verificationKeyId.GetSpan()),
        };
        var differentProof = equal with { ProofBytes = new byte[] { 0x11, 0x22, 0x34 } };
        var differentSystem = equal with { ProofSystem = ProofSystem.RiscZero };
        var differentKey = equal with { VerificationKeyId = UInt256.Zero };

        Assert.AreEqual(left, equal);
        Assert.AreEqual(left.GetHashCode(), equal.GetHashCode());
        Assert.AreNotEqual(left, differentProof);
        Assert.AreNotEqual(left, differentSystem);
        Assert.AreNotEqual(left, differentKey);
        Assert.IsFalse(left.Equals(null));
    }

    [TestMethod]
    public void MockRiscVProver_Constructor_RejectsNullVerificationKeyId()
    {
        // Regression for iter 198: null UInt256 verificationKeyId would slip past the
        // ctor and surface much later in RiscVProofPayload.Encode's iter-159 null-guard
        // ("VerificationKeyId is null"), which names the payload field but not the
        // producer. Surface at the source.
        Assert.ThrowsExactly<ArgumentNullException>(() => new MockRiscVProver(null!));
    }

    [TestMethod]
    public void MockRiscVVerifier_Constructor_RejectsNullExpectedVkId()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new MockRiscVVerifier(null!));
    }

    [TestMethod]
    public void VerifierRegistry_Register_RejectsNullVerifier()
    {
        // Pin VerifierRegistry.cs:21. Without it Register would silently store null
        // and the next VerifyAsync would NRE on the dispatch.
        var registry = new VerifierRegistry();
        Assert.ThrowsExactly<ArgumentNullException>(() => registry.Register(null!));
    }

    [TestMethod]
    public async Task VerifierRegistry_VerifyAsync_RejectsNullCommitment()
    {
        // Pin VerifierRegistry.cs:37.
        var registry = new VerifierRegistry();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await registry.VerifyAsync(null!, SamplePublicInputs()));
    }

    [TestMethod]
    public async Task VerifierRegistry_VerifyAsync_RejectsNullPublicInputs()
    {
        // Pin VerifierRegistry.cs:38.
        var registry = new VerifierRegistry();
        var sample = new L2BatchCommitment
        {
            ChainId = 1001,
            BatchNumber = 1,
            FirstBlock = 0,
            LastBlock = 0,
            PreStateRoot = UInt256.Zero,
            PostStateRoot = UInt256.Zero,
            TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = UInt256.Zero,
            PublicInputHash = UInt256.Zero,
            ProofType = ProofType.None,
            Proof = ReadOnlyMemory<byte>.Empty,
        };
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await registry.VerifyAsync(sample, null!));
    }

    [TestMethod]
    public void OptimisticVerifier_Constructor_RejectsNullSequencerKey()
    {
        // Pin OptimisticVerifier.cs:26.
        Assert.ThrowsExactly<ArgumentNullException>(() => new OptimisticVerifier(null!));
    }

    [TestMethod]
    public async Task OptimisticVerifier_VerifyAsync_RejectsNullPublicInputs()
    {
        // Pin OptimisticVerifier.cs:37.
        var priv = new byte[32]; priv[0] = 1;
        var realPub = ECCurve.Secp256r1.G * priv;
        var verifier = new OptimisticVerifier(realPub);
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await verifier.VerifyAsync(null!, ReadOnlyMemory<byte>.Empty));
    }

    [TestMethod]
    public async Task MockRiscVProver_ProveAsync_RejectsNullRequest()
    {
        // Pin RiscVProver.cs:52.
        var prover = new MockRiscVProver(UInt256.Parse("0x" + new string('a', 64)));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await prover.ProveAsync(null!));
    }

    [TestMethod]
    public async Task MockRiscVVerifier_VerifyAsync_RejectsNullPublicInputs()
    {
        // Pin RiscVProver.cs:103.
        var verifier = new MockRiscVVerifier(UInt256.Parse("0x" + new string('a', 64)));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await verifier.VerifyAsync(null!, ReadOnlyMemory<byte>.Empty));
    }

    [TestMethod]
    public void ProofSystem_HasExpectedDiscriminants()
    {
        // RiscVProofPayload encodes ProofSystem as a 1-byte field at the documented
        // offset; the values double as L1 verifier-registry dispatch keys. Pinning
        // them surfaces a future renumber as a visible diff rather than silent verifier
        // mismatch.
        Assert.AreEqual(0, (byte)ProofSystem.Unknown);
        Assert.AreEqual(1, (byte)ProofSystem.Sp1);
        Assert.AreEqual(2, (byte)ProofSystem.RiscZero);
        Assert.AreEqual(3, (byte)ProofSystem.Halo2);
        Assert.AreEqual(4, (byte)ProofSystem.Axiom);
    }

    [TestMethod]
    public void PublicInputs_HashEqualsHash256OfCanonicalEncoding()
    {
        // StateRootCalculator.HashPublicInputs and BatchSerializer.EncodePublicInputs
        // independently hand-roll the SAME 332-byte canonical layout
        // (ChainId‖BatchNumber‖10×UInt256, all LE). StateRootCalculator.cs:87 documents
        // this as the "BatchSerializer-equivalent layout" invariant, but nothing pinned
        // it. The two encoders feed DIFFERENT trust paths:
        //   - EncodePublicInputs → the exact bytes Multisig validators ECDSA-sign
        //     (AttestationVerifier) and Optimistic sequencers sign (OptimisticVerifier).
        //   - HashPublicInputs    → commitment.PublicInputHash, re-derived and checked by
        //     VerifierRegistry and by NeoHub on L1.
        // If either layout drifts (a reordered field, a missing domain-separator), the
        // signed bytes would no longer correspond to the committed hash and the drift
        // would surface only as opaque downstream verification failures. Pin the
        // equivalence so a layout change is a visible, intentional diff in one place.
        var inputs = SamplePublicInputs();
        var expected = new UInt256(Crypto.Hash256(BatchSerializer.EncodePublicInputs(inputs)));
        Assert.AreEqual(expected, StateRootCalculator.HashPublicInputs(inputs),
            "HashPublicInputs drifted from the canonical EncodePublicInputs layout");
    }

    [TestMethod]
    public void PublicInputs_HashBindsChainId_PreventsCrossL2Replay()
    {
        // Domain-separation regression: ChainId is the first field of both canonical
        // encodings, so a proof/attestation valid on one L2 must not validate on another
        // that shares a validator set. Flipping only ChainId must change the hash.
        var a = SamplePublicInputs();
        var b = a with { ChainId = a.ChainId + 1u };
        Assert.AreNotEqual(
            StateRootCalculator.HashPublicInputs(a),
            StateRootCalculator.HashPublicInputs(b),
            "public-input hash must bind ChainId for cross-L2 domain separation");
    }

    [TestMethod]
    public void PublicInputs_Hash_MatchesOutOfBandGoldenVector()
    {
        // Out-of-band golden vector (computed independently of this C# code) that pins the exact
        // bytes + double-SHA256 of HashPublicInputs. This is the cross-implementation anchor for
        // the ON-CHAIN parity: NeoHub.SettlementManager.ComputePublicInputHash reconstructs this
        // same hash from the commitment header offsets (chainId@0, batchNumber@4, preState@28,
        // postState@60, tx@92, receipt@124, withdrawal@156, l2ToL1@188, l2ToL2@220, daCommitment@252)
        // plus the supplied l1MessageHash/blockContextHash. If this golden constant ever breaks, the
        // on-chain offsets/field-order MUST be re-verified or settlement will silently mis-bind.
        // Canonical input: chainId=1, batchNumber=1, roots = uniform bytes 0x11,0x22,...,0xAA in
        // HashPublicInputs field order (pre,post,tx,receipt,withdrawal,l2ToL1,l2ToL2,l1MessageHash,
        // daCommitment,blockContextHash).
        static UInt256 Root(char hexDigit) => UInt256.Parse("0x" + new string(hexDigit, 64));
        var inputs = new PublicInputs
        {
            ChainId = 1,
            BatchNumber = 1,
            PreStateRoot = Root('1'),
            PostStateRoot = Root('2'),
            TxRoot = Root('3'),
            ReceiptRoot = Root('4'),
            WithdrawalRoot = Root('5'),
            L2ToL1MessageRoot = Root('6'),
            L2ToL2MessageRoot = Root('7'),
            L1MessageHash = Root('8'),
            DACommitment = Root('9'),
            BlockContextHash = Root('a'),
        };
        var golden = Convert.FromHexString("13760a1b4cb7b3e75421c564671f8fabb7014fe71806fd94beaff927a4cc03f4");
        CollectionAssert.AreEqual(golden, StateRootCalculator.HashPublicInputs(inputs).GetSpan().ToArray(),
            "HashPublicInputs diverged from the pinned golden vector — re-verify on-chain ComputePublicInputHash parity");
    }
}
