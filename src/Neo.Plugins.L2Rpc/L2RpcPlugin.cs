using Neo.L2.Telemetry;
using Neo.Plugins;
using Neo.Plugins.RpcServer;

namespace Neo.Plugins.L2Rpc;

/// <summary>
/// Registers the N4 L2 RPC surface with the official Neo RPC server.
/// </summary>
/// <remarks>
/// See doc.md §14.1. A production composition root injects an
/// <see cref="IL2RpcStore"/> through <see cref="NeoSystem.AddService(object)"/>. Until a
/// store is present, no L2 methods are registered, so an incomplete node fails closed
/// with the official method-not-found response instead of serving mock or empty data.
/// </remarks>
public class L2RpcPlugin : Plugin
{
    private readonly object _gate = new();
    private readonly IL2RpcMethodRegistrar _registrar;
    private readonly Dictionary<uint, NeoSystem> _systems = [];
    private readonly Dictionary<uint, Registration> _registrations = [];
    private IL2Metrics _metrics = NoOpMetrics.Instance;
    private bool _disposed;

    /// <inheritdoc />
    public override string Name => "L2RpcPlugin";

    /// <inheritdoc />
    public override string Description => "Registers Neo Elastic Network L2 methods with Neo.Plugins.RpcServer.";

    /// <summary>Constructs the production plugin backed by the official RPC registrar.</summary>
    public L2RpcPlugin() : this(OfficialL2RpcMethodRegistrar.Instance) { }

    internal L2RpcPlugin(IL2RpcMethodRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        _registrar = registrar;
    }

    /// <summary>
    /// Sets the telemetry sink used by handlers registered after this call.
    /// </summary>
    /// <remarks>
    /// Call before adding the <see cref="IL2RpcStore"/> service. Existing registrations
    /// are immutable because replacing their store or metrics sink after publication
    /// would make responses for one network change ownership at runtime.
    /// </remarks>
    public void WithMetrics(IL2Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_registrations.Count != 0)
                throw new InvalidOperationException("L2 RPC metrics must be configured before the first network is registered");
            _metrics = metrics;
        }
    }

    /// <summary>Returns whether this plugin instance registered a handler for a network.</summary>
    public bool IsRegistered(uint network)
    {
        lock (_gate) return _registrations.ContainsKey(network);
    }

    /// <inheritdoc />
    protected override void OnSystemLoaded(NeoSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        var network = system.Settings.Network;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_systems.TryGetValue(network, out var existing))
            {
                if (!ReferenceEquals(existing, system))
                    throw new InvalidOperationException(
                        $"L2 RPC network {network} is already bound to a different NeoSystem instance");
            }
            else
            {
                _systems.Add(network, system);
                system.ServiceAdded += OnServiceAdded;
            }
        }

        if (!TryRegister(system))
        {
            Logs.RuntimeLogger.Warning(
                "L2RpcPlugin is fail-closed for network {Network}: no IL2RpcStore service is registered. " +
                "Add the production store with NeoSystem.AddService(store).",
                network);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    base.Dispose(disposing);
                    return;
                }
                _disposed = true;
                foreach (var system in _systems.Values)
                    system.ServiceAdded -= OnServiceAdded;
                foreach (var registration in _registrations.Values)
                    registration.Adapter.Dispose();
                _registrations.Clear();
                _systems.Clear();
            }
        }
        base.Dispose(disposing);
    }

    private void OnServiceAdded(object? sender, object service)
    {
        if (sender is not NeoSystem system || service is not IL2RpcStore store) return;
        TryRegister(system, store);
    }

    private bool TryRegister(NeoSystem system, IL2RpcStore? store = null)
    {
        var network = system.Settings.Network;
        lock (_gate)
        {
            if (_disposed) return false;
            if (_registrations.TryGetValue(network, out var existing))
            {
                if (store is not null && !ReferenceEquals(existing.Store, store))
                {
                    Logs.RuntimeLogger.Warning(
                        "L2RpcPlugin ignored a replacement IL2RpcStore for network {Network}; " +
                        "the first registered store remains authoritative until process restart.",
                        network);
                }
                return true;
            }

            store ??= system.GetService<IL2RpcStore>();
            if (store is null) return false;

            var adapter = new L2RpcServerAdapter(new L2RpcMethods(store, _metrics));
            try
            {
                _registrar.RegisterMethods(adapter, network);
                _registrations.Add(network, new Registration(store, adapter));
                return true;
            }
            catch
            {
                adapter.Dispose();
                throw;
            }
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record Registration(IL2RpcStore Store, L2RpcServerAdapter Adapter);
}

internal interface IL2RpcMethodRegistrar
{
    void RegisterMethods(object handler, uint network);
}

internal sealed class OfficialL2RpcMethodRegistrar : IL2RpcMethodRegistrar
{
    internal static OfficialL2RpcMethodRegistrar Instance { get; } = new();

    private OfficialL2RpcMethodRegistrar() { }

    public void RegisterMethods(object handler, uint network) =>
        RpcServerPlugin.RegisterMethods(handler, network);
}
