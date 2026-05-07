using System;
using System.Text.Json;
using Neo.L2;

namespace Neo.Stack.Cli.Commands;

/// <summary>
/// <c>validate &lt;path&gt;</c> — sanity-check a <c>chain.config.json</c> before running
/// the devnet, register-chain, or shipping a sample. Each enum byte is parsed (catches
/// typos in <c>securityLevel</c>, <c>daMode</c>, <c>sequencerModel</c>, <c>exitModel</c>);
/// each bool field is parsed; each numeric field is type-checked.
/// </summary>
/// <remarks>
/// <para>
/// Exits 0 with a "✅ valid" line when the config parses cleanly, 2 when it doesn't —
/// the message names the field that failed, mirroring <see cref="L2ChainConfigJsonReader"/>'s
/// error contract so the same operator-friendly diagnostics show up wherever a config
/// gets read.
/// </para>
/// <para>
/// Doesn't require the four operator-supplied L1 contract hashes (those come from
/// the deploy bundle later) — this subcommand validates only the JSON-encoded
/// dimensions a template / sample author controls.
/// </para>
/// </remarks>
internal static class ValidateChainConfigCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: neo-stack validate <path-to-chain.config.json>");
            return 1;
        }
        var path = args[0];
        if (!System.IO.File.Exists(path))
        {
            Console.Error.WriteLine($"file not found: {path}");
            return 2;
        }

        string json;
        try
        {
            json = System.IO.File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"failed to read {path}: {ex.Message}");
            return 2;
        }

        // The chain.config.json shape is:
        //   { chainId, template, vm, chainMode, daMode, proofType, securityLevel,
        //     sequencerModel, exitModel, gatewayEnabled, permissionlessExit,
        //     milestonePerBlockMs, validators }
        // We validate the §16.2 + chainMode + proofType + chainId fields. The
        // metadata fields (template, vm, milestonePerBlockMs, validators) are
        // operator-property — their format isn't pinned by the framework.
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // chainId — required + must validate as a non-zero L2 chain id.
            var chainId = RequireUInt(root, "chainId");
            ChainIdValidator.ValidateL2(chainId, "chainId");

            // §16.2 enum dimensions — required + must parse to known values.
            var sec = RequireEnum<SecurityLevel>(root, "securityLevel");
            var da = RequireEnum<DAMode>(root, "daMode");
            var seq = RequireEnum<SequencerModel>(root, "sequencerModel");
            var exit = RequireEnum<ExitModel>(root, "exitModel");

            // §6 ChainMode (drives consensus + settlement + DA semantics). Required
            // so an unparseable value surfaces here, not at L1-registration time.
            var chainMode = RequireEnum<ChainMode>(root, "chainMode");

            // ProofType (off-chain metadata; the verifier registry uses ProofType
            // to dispatch). Required so an unparseable value surfaces here, not
            // mid-prove.
            var proof = RequireEnum<ProofType>(root, "proofType");

            // Bool flags — required.
            var gateway = RequireBool(root, "gatewayEnabled");
            var permExit = RequireBool(root, "permissionlessExit");

            // Cross-field consistency: a chain claiming a particular SecurityLevel
            // SHOULD use a matching proofType. Mismatches aren't fatal — operators
            // who know what they're doing can ship anyway — but the warning catches
            // a typo (e.g. zk-rollup template + accidentally-changed proofType).
            // One warning per supported SecurityLevel dimension so a Validium / Sidechain
            // operator gets the same diagnostic an Optimistic / Validity operator would.
            if (sec == SecurityLevel.Validity && proof != ProofType.Zk)
            {
                Console.WriteLine($"⚠ securityLevel=Validity typically pairs with proofType=Zk; got {proof}");
            }
            if (sec == SecurityLevel.Validium && proof != ProofType.Zk)
            {
                Console.WriteLine($"⚠ securityLevel=Validium typically pairs with proofType=Zk; got {proof}");
            }
            if (sec == SecurityLevel.Optimistic && proof != ProofType.Optimistic && proof != ProofType.Multisig)
            {
                Console.WriteLine($"⚠ securityLevel=Optimistic typically pairs with proofType=Optimistic or Multisig; got {proof}");
            }
            if (sec == SecurityLevel.Sidechain && proof != ProofType.None && proof != ProofType.Multisig)
            {
                Console.WriteLine($"⚠ securityLevel=Sidechain typically pairs with proofType=None or Multisig; got {proof}");
            }

            // ChainMode vs DAMode: L2ValidiumMode means "transaction data lives
            // off L1" by definition (per doc.md §6 + §12). DAMode=L1 contradicts
            // that. This is a spec-level contradiction worth surfacing — operator
            // probably meant to choose either L2RollupMode (with L1 DA) or change
            // daMode to NeoFS / External / DAC.
            if (chainMode == ChainMode.L2ValidiumMode && da == DAMode.L1)
            {
                Console.WriteLine($"⚠ chainMode=L2ValidiumMode contradicts daMode=L1; validium chains by definition have off-chain DA (NeoFS / External / DAC)");
            }

            // ExitModel.OperatorAssisted vs permissionlessExit=true: per the
            // ExitModel doc, OperatorAssisted means "user exit requires the
            // operator to co-sign or pre-stage exit batches." That's the opposite
            // of permissionless. A chain claiming both is internally
            // contradictory — pin so the operator doesn't ship a chain config
            // that promises permissionlessness it can't deliver.
            if (exit == ExitModel.OperatorAssisted && permExit)
            {
                Console.WriteLine($"⚠ exitModel=OperatorAssisted contradicts permissionlessExit=true; OperatorAssisted means user exit requires operator co-sign — flip permissionlessExit to false or change exitModel");
            }

            Console.WriteLine($"✅ valid: chainId={chainId} chainMode={chainMode} securityLevel={sec} daMode={da} " +
                $"sequencer={seq} exit={exit} proofType={proof} gateway={gateway} permExit={permExit}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ {ex.Message}");
            return 2;
        }
    }

    private static uint RequireUInt(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var prop))
            throw new ArgumentException($"missing '{field}'");
        if (!prop.TryGetUInt32(out var value))
            throw new ArgumentException($"'{field}' is not a non-negative integer");
        return value;
    }

    private static T RequireEnum<T>(JsonElement root, string field) where T : struct, Enum
    {
        if (!root.TryGetProperty(field, out var prop))
            throw new ArgumentException($"missing '{field}'");
        var name = prop.GetString();
        if (!Enum.TryParse<T>(name, ignoreCase: false, out var value))
        {
            var names = string.Join(", ", Enum.GetNames<T>());
            throw new ArgumentException(
                $"'{field}'='{name}' is not a valid {typeof(T).Name} (expected one of: {names})");
        }
        return value;
    }

    private static bool RequireBool(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var prop))
            throw new ArgumentException($"missing '{field}'");
        return prop.GetBoolean();
    }
}
