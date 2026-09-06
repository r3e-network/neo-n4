namespace Neo.L2.Settlement.Rpc;

/// <summary>
/// AWS KMS key state enumeration for status monitoring.
/// </summary>
public enum KeyState
{
    /// <summary>Key is enabled and ready for signing.</summary>
    Enabled,

    /// <summary>Key is disabled and cannot be used for operations.</summary>
    Disabled,

    /// <summary>Key material is pending import into KMS.</summary>
    PendingImport,

    /// <summary>Key is scheduled for deletion.</summary>
    PendingDeletion,

    /// <summary>Key has been deprecated and replaced.</summary>
    Deprecated,

    /// <summary>Unknown or unmapped key state.</summary>
    Unknown
}
