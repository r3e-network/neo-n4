using System.Globalization;
using System.Text.Json;
using Neo.L2.Sequencer;

namespace Neo.Stack.Cli.Commands;

internal static class StartCommandPreflight
{
    public static int Verify(string[] args, string roleName, out uint chainId, out string chainDir, out string configPath)
    {
        chainId = 0;
        chainDir = "";
        configPath = "";
        var rawChainId = ArgUtil.Get(args, "--chain-id", "1001");
        if (!uint.TryParse(rawChainId, out var parsed))
        {
            Console.Error.WriteLine($"--chain-id must be a non-negative integer, got '{rawChainId}'");
            return 1;
        }
        try
        {
            chainId = Neo.L2.ChainIdValidator.ValidateL2(parsed, "--chain-id");
        }
        catch (InvalidDataException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        var outputFlag = ArgUtil.Get(args, "--output", "");
        chainDir = Path.GetFullPath(outputFlag.Length > 0
            ? outputFlag
            : ArgUtil.Get(args, "--path", $"./chain-{chainId}"));
        if (!Directory.Exists(chainDir))
        {
            Console.Error.WriteLine($"Chain dir not found: {chainDir}");
            Console.Error.WriteLine("Run `neo-stack create-chain --chain-id <id>` followed by `neo-stack init-l2 --chain-id <id>` first.");
            return 2;
        }
        configPath = Path.Combine(chainDir, "chain.config.json");
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"Missing config: {configPath}");
            Console.Error.WriteLine("Run `neo-stack create-chain --chain-id <id>` first.");
            return 3;
        }
        Console.WriteLine($"Pre-flight OK for {roleName}:");
        Console.WriteLine($"  chainDir   = {chainDir}");
        Console.WriteLine($"  configPath = {configPath}");
        return 0;
    }
}

internal static class StartSequencerCommand
{
    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    public static Task<int> RunAsync(
        string[] args,
        IOperatorProcessRunner? processRunner = null,
        INativeSequencerCommitteeReader? committeeReader = null,
        CancellationToken cancellationToken = default)
    {
        return NodeOperatorCommand.RunAsync(
            args,
            roleName: "sequencer",
            requiredPluginAssembly: "DBFTPlugin",
            requiredPluginConfig: "DBFTPlugin.json",
            allowCommitteeSync: true,
            processRunner,
            committeeReader,
            cancellationToken);
    }
}

internal static class StartBatcherCommand
{
    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    public static Task<int> RunAsync(
        string[] args,
        IOperatorProcessRunner? processRunner = null,
        INativeSequencerCommitteeReader? committeeReader = null,
        CancellationToken cancellationToken = default)
    {
        return NodeOperatorCommand.RunAsync(
            args,
            roleName: "batcher",
            requiredPluginAssembly: "Neo.Plugins.L2Batch",
            requiredPluginConfig: "config.json",
            allowCommitteeSync: false,
            processRunner,
            committeeReader,
            cancellationToken);
    }
}

internal static class NodeOperatorCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        string roleName,
        string requiredPluginAssembly,
        string requiredPluginConfig,
        bool allowCommitteeSync,
        IOperatorProcessRunner? processRunner,
        INativeSequencerCommitteeReader? committeeReader,
        CancellationToken cancellationToken)
    {
        var preflight = StartCommandPreflight.Verify(args, roleName, out var chainId, out var chainDir, out var chainConfigPath);
        if (preflight != 0) return preflight;

        var defaultDeploymentDirectory = Path.Combine(
            chainDir,
            string.Equals(roleName, "batcher", StringComparison.Ordinal) ? "batcher-node" : "node");
        var nodeConfigPath = Path.GetFullPath(
            ArgUtil.Get(args, "--node-config", Path.Combine(defaultDeploymentDirectory, "config.json")),
            chainDir);
        if (!File.Exists(nodeConfigPath))
        {
            Console.Error.WriteLine($"Neo.CLI config not found: {nodeConfigPath}");
            Console.Error.WriteLine("Pass --node-config or initialize from a reviewed Neo.CLI config with `neo-stack init-l2 --node-config <path>`. ");
            return 4;
        }
        if (!SequencerLaunchConfiguration.TryLoad(
                chainConfigPath,
                nodeConfigPath,
                chainId,
                out var launchConfiguration,
                out var configurationError))
        {
            Console.Error.WriteLine(configurationError);
            return 4;
        }

        var expectedNetworkValue = ArgUtil.Get(args, "--expected-network", "");
        if (expectedNetworkValue.Length > 0
            && (!uint.TryParse(expectedNetworkValue, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedNetwork)
                || expectedNetwork != launchConfiguration.Network))
        {
            Console.Error.WriteLine($"--expected-network must equal node ProtocolConfiguration.Network ({launchConfiguration.Network}).");
            return 4;
        }

        var dryRun = ArgUtil.HasFlag(args, "--dry-run");
        var syncOnly = ArgUtil.HasFlag(args, "--sync-only");
        var syncCommittee = allowCommitteeSync
            && (syncOnly || ArgUtil.HasFlag(args, "--sync-committee"));
        if (!allowCommitteeSync && (syncOnly || ArgUtil.HasFlag(args, "--sync-committee")))
        {
            Console.Error.WriteLine("Committee synchronization is only valid for start-sequencer.");
            return 4;
        }
        if (syncCommittee && !dryRun && !ArgUtil.HasFlag(args, "--broadcast"))
        {
            Console.Error.WriteLine("Committee synchronization is state-changing; pass --broadcast or use --dry-run.");
            return 7;
        }
        if (!TryGetCommitteeActivationTimeout(
                args,
                launchConfiguration,
                out var committeeActivationTimeout,
                out var activationTimeoutError))
        {
            Console.Error.WriteLine(activationTimeoutError);
            return 7;
        }
        if (syncCommittee)
        {
            var script = SequencerCommitteeTransactionBuilder.BuildSetValidatorsScript(
                launchConfiguration.Validators,
                launchConfiguration.Validators.Count);
            if (dryRun)
            {
                Console.WriteLine($"Committee update script: 0x{Convert.ToHexString(script).ToLowerInvariant()}");
            }
            else
            {
                var syncResult = await OperatorTransactionBroadcaster.BroadcastAsync(
                    args,
                    script,
                    $"L2 dBFT sequencer committee for chain {chainId}",
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (syncResult != 0) return syncResult;
                var verifyResult = await WaitForNativeCommitteeAsync(
                    args,
                    launchConfiguration,
                    committeeReader,
                    committeeActivationTimeout,
                    cancellationToken).ConfigureAwait(false);
                if (verifyResult != 0) return verifyResult;
            }
        }
        else if (!launchConfiguration.GenesisValidatorsMatch)
        {
            var verifyResult = await VerifyNativeCommitteeAsync(
                args,
                launchConfiguration,
                committeeReader,
                cancellationToken).ConfigureAwait(false);
            if (verifyResult != 0) return verifyResult;
        }
        if (syncOnly) return 0;

        if (!OperatorExecutableResolver.TryResolve(
                ArgUtil.Get(args, "--neo-cli", ""),
                "NEO_N4_NEO_CLI",
                defaultDeploymentDirectory,
                out var executable,
                out var executableError))
        {
            Console.Error.WriteLine(executableError);
            return 5;
        }
        var deploymentConfigPath = Path.Combine(executable.DeploymentRoot, "config.json");
        if (!PathsEqual(nodeConfigPath, deploymentConfigPath))
        {
            Console.Error.WriteLine(
                $"Neo.CLI must load its reviewed config from the deployment root: {deploymentConfigPath}");
            return 4;
        }
        if (!RequirePlugin(
                executable.DeploymentRoot,
                requiredPluginAssembly,
                requiredPluginConfig,
                roleName,
                chainId,
                out var pluginError))
        {
            Console.Error.WriteLine(pluginError);
            return 6;
        }
        if (!TryGetForwardedArguments(args, out var forwardedArguments, out var forwardingError))
        {
            Console.Error.WriteLine(forwardingError);
            return 7;
        }
        if (!TryGetShutdownGrace(args, out var shutdownGrace, out var graceError))
        {
            Console.Error.WriteLine(graceError);
            return 7;
        }

        var defaultDataDirectory = Path.Combine(
            chainDir,
            string.Equals(roleName, "batcher", StringComparison.Ordinal) ? "batcher-data" : "data");
        var dataDirectory = Path.GetFullPath(
            ArgUtil.Get(args, "--data-dir", defaultDataDirectory),
            chainDir);
        if (!Directory.Exists(dataDirectory))
        {
            Console.Error.WriteLine($"Node data directory not found: {dataDirectory}");
            Console.Error.WriteLine("Run `neo-stack init-l2` first or pass an initialized --data-dir.");
            return 4;
        }
        var configuredStoragePath = Path.GetFullPath(
            launchConfiguration.StoragePath,
            executable.DeploymentRoot);
        if (!PathsEqual(configuredStoragePath, dataDirectory))
        {
            Console.Error.WriteLine(
                $"Neo.CLI Storage.Path resolves to {configuredStoragePath}, expected isolated role data directory {dataDirectory}.");
            return 4;
        }
        if (string.Equals(roleName, "sequencer", StringComparison.Ordinal))
        {
            if (!launchConfiguration.UnlockWalletActive)
            {
                Console.Error.WriteLine("Sequencer Neo.CLI config must set ApplicationConfiguration.UnlockWallet.IsActive=true.");
                return 4;
            }
            if (string.IsNullOrWhiteSpace(launchConfiguration.WalletPath))
            {
                Console.Error.WriteLine("Sequencer Neo.CLI config must set ApplicationConfiguration.UnlockWallet.Path.");
                return 4;
            }
            var walletPath = Path.GetFullPath(launchConfiguration.WalletPath, executable.DeploymentRoot);
            if (!File.Exists(walletPath))
            {
                Console.Error.WriteLine($"Sequencer wallet not found: {walletPath}");
                return 4;
            }
        }

        var arguments = executable.PrefixArguments
            .Concat(forwardedArguments)
            .ToArray();
        var spec = new OperatorProcessSpec(
            executable.FileName,
            arguments,
            executable.DeploymentRoot,
            shutdownGrace);
        Console.WriteLine($"Launching {roleName} process:");
        Console.WriteLine($"  network     = {launchConfiguration.Network}");
        Console.WriteLine($"  validators  = {launchConfiguration.Validators.Count}");
        Console.WriteLine($"  command     = {spec.DisplayCommand}");
        if (dryRun) return 0;
        return await (processRunner ?? new SystemOperatorProcessRunner())
            .RunAsync(spec, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool RequirePlugin(
        string deploymentRoot,
        string assemblyName,
        string configName,
        string roleName,
        uint chainId,
        out string error)
    {
        var pluginDirectory = Path.Combine(deploymentRoot, "Plugins", assemblyName);
        var assemblyPath = Path.Combine(pluginDirectory, assemblyName + ".dll");
        var configPath = Path.Combine(pluginDirectory, configName);
        if (!File.Exists(assemblyPath))
        {
            error = $"Required plugin assembly not found: {assemblyPath}";
            return false;
        }
        if (!File.Exists(configPath))
        {
            error = $"Required plugin config not found: {configPath}";
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            var plugin = document.RootElement.GetProperty("PluginConfiguration");
            if (string.Equals(roleName, "sequencer", StringComparison.Ordinal))
            {
                if (!plugin.TryGetProperty("AutoStart", out var autoStart) || !autoStart.GetBoolean())
                    throw new InvalidDataException("DBFTPlugin PluginConfiguration.AutoStart must be true.");
            }
            else if (!plugin.TryGetProperty("ChainId", out var configuredChainId)
                || configuredChainId.GetUInt32() != chainId)
            {
                throw new InvalidDataException($"L2Batch plugin ChainId must equal {chainId}.");
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or KeyNotFoundException
            or InvalidOperationException)
        {
            error = $"Invalid required plugin config {configPath}: {exception.Message}";
            return false;
        }
        error = "";
        return true;
    }

    private static async Task<int> VerifyNativeCommitteeAsync(
        string[] args,
        SequencerLaunchConfiguration launchConfiguration,
        INativeSequencerCommitteeReader? committeeReader,
        CancellationToken cancellationToken)
    {
        var rpcEndpoint = ArgUtil.Get(args, "--rpc", "");
        if (rpcEndpoint.Length == 0)
        {
            Console.Error.WriteLine(
                "The desired validator set differs from genesis; --rpc is required to verify finalized native committee state.");
            return 4;
        }
        try
        {
            var current = await (committeeReader ?? new RpcNativeSequencerCommitteeReader())
                .ReadAsync(rpcEndpoint, launchConfiguration.Validators.Count, cancellationToken)
                .ConfigureAwait(false);
            if (!current.SequenceEqual(launchConfiguration.Validators))
            {
                Console.Error.WriteLine(
                    "Finalized native sequencer committee does not match chain.config.json validators.");
                return 4;
            }
            Console.WriteLine("Finalized native sequencer committee matches chain.config.json.");
            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Failed to verify finalized native sequencer committee: {exception.Message}");
            return 4;
        }
    }

    private static async Task<int> WaitForNativeCommitteeAsync(
        string[] args,
        SequencerLaunchConfiguration launchConfiguration,
        INativeSequencerCommitteeReader? committeeReader,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var rpcEndpoint = ArgUtil.Get(args, "--rpc", "");
        if (rpcEndpoint.Length == 0)
        {
            Console.Error.WriteLine("--rpc is required to wait for native committee activation.");
            return 4;
        }

        var reader = committeeReader ?? new RpcNativeSequencerCommitteeReader();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        Exception? lastError = null;
        while (System.Diagnostics.Stopwatch.GetElapsedTime(started) < timeout)
        {
            if (cancellationToken.IsCancellationRequested) return 130;
            try
            {
                var current = await reader
                    .ReadAsync(rpcEndpoint, launchConfiguration.Validators.Count, cancellationToken)
                    .ConfigureAwait(false);
                if (current.SequenceEqual(launchConfiguration.Validators))
                {
                    Console.WriteLine("Finalized native sequencer committee is active.");
                    return 0;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return 130;
            }
            catch (Exception exception)
            {
                lastError = exception;
            }

            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);
            var remaining = timeout - elapsed;
            if (remaining <= TimeSpan.Zero) break;
            try
            {
                await Task.Delay(
                    remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return 130;
            }
        }

        var detail = lastError is null ? "validator set still differs" : lastError.Message;
        Console.Error.WriteLine(
            $"Timed out after {timeout.TotalSeconds:F0}s waiting for the scheduled native committee to activate: {detail}");
        return 4;
    }

    private static bool TryGetCommitteeActivationTimeout(
        string[] args,
        SequencerLaunchConfiguration launchConfiguration,
        out TimeSpan timeout,
        out string error)
    {
        var cycleMilliseconds = checked(
            (long)launchConfiguration.MillisecondsPerBlock * launchConfiguration.CommitteeMembersCount);
        var defaultSeconds = Math.Clamp((cycleMilliseconds * 2 + 999) / 1000, 30, 3600);
        var raw = ArgUtil.Get(
            args,
            "--committee-activation-timeout-seconds",
            defaultSeconds.ToString(CultureInfo.InvariantCulture));
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds is < 1 or > 7200)
        {
            timeout = default;
            error = "--committee-activation-timeout-seconds must be between 1 and 7200.";
            return false;
        }
        timeout = TimeSpan.FromSeconds(seconds);
        error = "";
        return true;
    }

    private static bool TryGetForwardedArguments(
        string[] args,
        out IReadOnlyList<string> forwarded,
        out string error)
    {
        var separator = Array.IndexOf(args, "--");
        forwarded = separator < 0 ? Array.Empty<string>() : args[(separator + 1)..];
        if (forwarded.Count == 0)
        {
            error = "";
            return true;
        }
        var validLevels = new[] { "Verbose", "Debug", "Information", "Warning", "Error", "Fatal" };
        if (forwarded.Count == 2
            && forwarded[0] is "--verbose" or "/verbose"
            && validLevels.Contains(forwarded[1], StringComparer.OrdinalIgnoreCase))
        {
            error = "";
            return true;
        }
        error = "Only `--verbose <Verbose|Debug|Information|Warning|Error|Fatal>` may be forwarded to Neo.CLI; config, wallet, storage, plugin download, background, and verification overrides are prohibited.";
        return false;
    }

    private static bool TryGetShutdownGrace(string[] args, out TimeSpan grace, out string error)
    {
        var raw = ArgUtil.Get(args, "--shutdown-grace-seconds", "30");
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds is < 1 or > 300)
        {
            grace = default;
            error = "--shutdown-grace-seconds must be between 1 and 300.";
            return false;
        }
        grace = TimeSpan.FromSeconds(seconds);
        error = "";
        return true;
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            comparison);
    }
}

internal static class StartProverCommand
{
    public static int Run(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    public static async Task<int> RunAsync(
        string[] args,
        IOperatorProcessRunner? processRunner = null,
        CancellationToken cancellationToken = default)
    {
        var preflight = StartCommandPreflight.Verify(args, "prover", out _, out var chainDir, out _);
        if (preflight != 0) return preflight;
        if (!OperatorExecutableResolver.TryResolve(
                ArgUtil.Get(args, "--prover", ""),
                "NEO_N4_PROVE_BATCH",
                Path.Combine(chainDir, "prover"),
                out var executable,
                out var executableError))
        {
            Console.Error.WriteLine(executableError);
            return 5;
        }

        var watchDirectory = Path.GetFullPath(
            ArgUtil.Get(args, "--watch", Path.Combine(chainDir, "prover", "inbox")),
            chainDir);
        var archiveDirectory = Path.GetFullPath(
            ArgUtil.Get(args, "--archive", Path.Combine(chainDir, "prover", "archive")),
            chainDir);
        if (!Directory.Exists(watchDirectory))
        {
            Console.Error.WriteLine($"Prover watch directory not found: {watchDirectory}");
            return 4;
        }
        Directory.CreateDirectory(archiveDirectory);
        if (PathsEqual(watchDirectory, archiveDirectory))
        {
            Console.Error.WriteLine("Prover --watch and --archive must be different directories.");
            return 4;
        }

        var separator = Array.IndexOf(args, "--");
        var forwarded = separator < 0 ? Array.Empty<string>() : args[(separator + 1)..];
        if (forwarded.Length != 0
            && (forwarded.Length != 2
                || forwarded[0] != "--poll-secs"
                || !uint.TryParse(forwarded[1], NumberStyles.None, CultureInfo.InvariantCulture, out var pollSeconds)
                || pollSeconds is 0 or > 3600))
        {
            Console.Error.WriteLine("Only `--poll-secs <1..3600>` may be forwarded to the prover daemon.");
            return 7;
        }
        var graceValue = ArgUtil.Get(args, "--shutdown-grace-seconds", "120");
        if (!int.TryParse(graceValue, NumberStyles.None, CultureInfo.InvariantCulture, out var graceSeconds)
            || graceSeconds is < 1 or > 600)
        {
            Console.Error.WriteLine("--shutdown-grace-seconds must be between 1 and 600 for the prover.");
            return 7;
        }

        var arguments = executable.PrefixArguments
            .Concat(new[] { "daemon", "--watch", watchDirectory, "--archive", archiveDirectory })
            .Concat(forwarded)
            .ToArray();
        var spec = new OperatorProcessSpec(
            executable.FileName,
            arguments,
            chainDir,
            TimeSpan.FromSeconds(graceSeconds));
        Console.WriteLine("Launching SP1 prover daemon:");
        Console.WriteLine($"  command     = {spec.DisplayCommand}");
        if (ArgUtil.HasFlag(args, "--dry-run")) return 0;
        return await (processRunner ?? new SystemOperatorProcessRunner())
            .RunAsync(spec, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            comparison);
    }
}
