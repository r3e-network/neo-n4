using System;
using System.IO;
using Neo.L2;

namespace Neo.Stack.Cli.Commands;

/// <summary>
/// <c>new-l2</c> — composite that goes from "I want a custom L2" to a buildable +
/// testable + devnet-previewable starter directory in one command. Strings together
/// <see cref="CreateChainCommand"/>, <see cref="ValidateChainConfigCommand"/>,
/// <see cref="InitL2Command"/>, and <see cref="ScaffoldExecutorCommand"/> with their
/// flags routed appropriately.
/// </summary>
/// <remarks>
/// <para>
/// Usage:
/// </para>
/// <code>
/// neo-stack new-l2 --name MyChain --chain-id 1234 [--template rollup] [--output ./my-l2]
/// </code>
/// <para>
/// What gets created (default <c>--output ./chain-&lt;chainId&gt;</c>):
/// </para>
/// <code>
/// chain-1234/
/// ├── chain.config.json              # from create-chain (template-driven §16.2 dimensions)
/// ├── data/  logs/  Plugins/         # from init-l2 (node working dirs)
/// ├── MyChainExecutor/               # from scaffold-executor (custom ITransactionExecutor)
/// │   ├── MyChainExecutor.csproj
/// │   ├── MyChainExecutor.cs
/// │   ├── IMyChainState.cs
/// │   ├── MyChainTxBuilder.cs
/// │   ├── MyChainKeyedStateStoreAdapter.cs
/// │   └── README.md
/// └── MyChainExecutor.UnitTests/     # from scaffold-executor --with-tests
///     ├── MyChainExecutor.UnitTests.csproj
///     ├── Usings.cs
///     └── UT_MyChainExecutor.cs
/// </code>
/// <para>
/// Each step is invoked through its own <c>Run</c> method, so flag validation +
/// refuse-to-overwrite + atomic-abort behavior is inherited. If any step fails, the
/// composite aborts with that step's exit code — partial progress stays on disk so
/// the operator can inspect what got written before the failure.
/// </para>
/// </remarks>
internal static class NewL2Command
{
    /// <summary>Run the composite.</summary>
    public static int Run(string[] args)
    {
        // Validate the args we own before delegating, so error messages name new-l2
        // instead of pointing at one of the subcommands.
        var name = ArgUtil.Get(args, "--name", "");
        if (string.IsNullOrEmpty(name))
        {
            Console.Error.WriteLine("--name is required (the chain's project + opcode name; emitted as <Name>Executor).");
            return 1;
        }
        var rawChainId = ArgUtil.Get(args, "--chain-id", "1001");
        if (!uint.TryParse(rawChainId, out var parsedChainId))
        {
            Console.Error.WriteLine($"--chain-id must be a non-negative integer, got '{rawChainId}'");
            return 1;
        }
        // Reject 0 at the composite level too — same defense the underlying commands
        // do, but here we surface "new-l2 rejected this" instead of trickling down.
        var chainId = Neo.L2.ChainIdValidator.ValidateL2(parsedChainId, "--chain-id");

        var template = ArgUtil.Get(args, "--template", "rollup");
        var vm = ArgUtil.Get(args, "--vm", L2ExecutionVms.NeoVm2RiscV);
        var da = ArgUtil.Get(args, "--da", "neofs");

        var pathFlag = ArgUtil.Get(args, "--path", "");
        var output = pathFlag.Length > 0
            ? pathFlag
            : ArgUtil.Get(args, "--output", $"./chain-{chainId}");

        // Step 1: create-chain → write chain.config.json at <output>/.
        Console.WriteLine("┌─ new-l2 step 1/4: create-chain");
        var createArgs = new[]
        {
            "--chain-id", chainId.ToString(),
            "--template", template,
            "--vm", vm,
            "--output", output,
        };
        var createRc = CreateChainCommand.Run(createArgs);
        if (createRc != 0)
        {
            Console.Error.WriteLine($"new-l2 aborted at create-chain (exit {createRc}).");
            return createRc;
        }

        // Step 2: validate → confirm the just-emitted chain.config.json parses.
        // Defense-in-depth: catches a template / serializer / validator drift
        // immediately, not later when the operator's first `neo-stack validate` run
        // surfaces it. With well-known templates this should always pass — failure
        // here means a genuine bug in one of the three components.
        Console.WriteLine();
        Console.WriteLine("├─ new-l2 step 2/4: validate (chain.config.json sanity check)");
        var configPath = Path.Combine(output, "chain.config.json");
        var validateRc = ValidateChainConfigCommand.Run(new[] { configPath });
        if (validateRc != 0)
        {
            Console.Error.WriteLine($"new-l2 aborted at validate (exit {validateRc}).");
            Console.Error.WriteLine($"This usually indicates a template / serializer drift — please file a bug.");
            return validateRc;
        }

        // Step 3: init-l2 → create data/ logs/ Plugins/ at <output>/.
        Console.WriteLine();
        Console.WriteLine("├─ new-l2 step 3/4: init-l2");
        var initArgs = new[]
        {
            "--chain-id", chainId.ToString(),
            "--da", da,
            "--output", output,
        };
        var initRc = InitL2Command.Run(initArgs);
        if (initRc != 0)
        {
            Console.Error.WriteLine($"new-l2 aborted at init-l2 (exit {initRc}).");
            return initRc;
        }

        // Step 4: scaffold-executor --with-tests → emit <output>/<Name>Executor/ +
        //         <output>/<Name>Executor.UnitTests/.
        Console.WriteLine();
        Console.WriteLine("└─ new-l2 step 4/4: scaffold-executor --with-tests");
        var executorOutput = Path.Combine(output, $"{name}Executor");
        var scaffoldArgs = new[]
        {
            "--name", name,
            "--chain-id", chainId.ToString(),
            "--output", executorOutput,
            "--with-tests",
        };
        var scaffoldRc = ScaffoldExecutorCommand.Run(scaffoldArgs);
        if (scaffoldRc != 0)
        {
            Console.Error.WriteLine($"new-l2 aborted at scaffold-executor (exit {scaffoldRc}).");
            return scaffoldRc;
        }

        // Print a "what's next" summary so the operator sees the full picture without
        // having to re-read three separate command outputs.
        Console.WriteLine();
        Console.WriteLine($"✅ new-l2 created chain {chainId} ({name}) at {output}/");
        Console.WriteLine();
        Console.WriteLine("Next:");
        Console.WriteLine($"  # 1. Build + test the executor scaffold");
        Console.WriteLine($"  dotnet build {executorOutput}/{name}Executor.csproj /p:NuGetAudit=false");
        Console.WriteLine($"  dotnet test  {executorOutput}.UnitTests/{name}Executor.UnitTests.csproj /p:NuGetAudit=false");
        Console.WriteLine();
        Console.WriteLine($"  # 2. Sanity-check the chain config");
        Console.WriteLine($"  neo-stack validate {output}/chain.config.json");
        Console.WriteLine();
        Console.WriteLine($"  # 3. Preview the chain through the in-process devnet");
        Console.WriteLine($"  dotnet run --project tools/Neo.L2.Devnet -- 5 --config {output}/chain.config.json");
        Console.WriteLine();
        Console.WriteLine($"  # 4. Edit {executorOutput}/{name}Executor.cs — replace the placeholder");
        Console.WriteLine($"  #    NoOp opcode with your chain's opcodes. See");
        Console.WriteLine($"  #    {executorOutput}/README.md for the 5-step customization checklist.");
        Console.WriteLine();
        Console.WriteLine($"  # 5. When ready for L1 deploy, generate the NeoHub deploy bundle:");
        Console.WriteLine($"  dotnet run --project tools/Neo.Hub.Deploy -- scaffold --output {output}/deploy-plan.json");
        Console.WriteLine($"  dotnet run --project tools/Neo.Hub.Deploy -- plan --plan {output}/deploy-plan.json --output {output}/deploy-bundle.json");
        Console.WriteLine($"  #    Feed the bundle to your wallet to deploy each NeoHub contract via");
        Console.WriteLine($"  #    ContractManagement.Deploy. Capture the REAL on-chain hash your wallet");
        Console.WriteLine($"  #    returns from each deploy (NOT the deterministic stub hashes in the");
        Console.WriteLine($"  #    bundle, which only exist for plan reproducibility), then call:");
        Console.WriteLine($"  neo-stack register-chain --chain-id {chainId} --output {output} \\");
        Console.WriteLine($"    --genesis-state-root <authenticated non-zero UInt256> \\");
        Console.WriteLine($"    --operator <real hash> --verifier <real hash> --bridge <real hash> --message <real hash>");
        Console.WriteLine($"  # See docs/launching-an-l2.md for the full L1-deploy walkthrough.");
        return 0;
    }
}
