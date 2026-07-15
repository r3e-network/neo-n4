namespace Neo.Plugins.L2;

/// <summary>Runtime assurance profile enforced by <see cref="L2DAPlugin"/>.</summary>
/// <remarks>See doc.md §7.4, §12, and §17.</remarks>
public enum DADeploymentProfile : byte
{
    /// <summary>Allows explicitly labeled local and semantic-simulation backends.</summary>
    Development = 0,

    /// <summary>Requires a public writer and distinct independently operated reader.</summary>
    Production = 1,
}
