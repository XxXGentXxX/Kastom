namespace Kastom.Core.Installation;

public sealed record CompatibilityResult(bool IsCompatible, string? Reason = null);
