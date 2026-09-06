namespace Neo.L2.Settlement.Rpc;

/// <summary>
/// Azure Key Vault key status enumeration for monitoring signing operations.
/// </summary>
public enum KeyVaultKeyStatus
{
    /// <summary>Signing operations are enabled on this key.</summary>
    SigningEnabled,

    /// <summary>No signing operations configured on this key.</summary>
    NoSigningOperations,

    /// <summary>Key is disabled or not accessible.</summary>
    Disabled,

    /// <summary>Unknown or unmapped key status.</summary>
    Unknown
}
