namespace Synax.Results;

/// <summary>
/// Captures the source location where a failure originated.
/// Populated automatically via caller-info attributes on <see cref="Result.Fail"/>.
/// </summary>
public sealed record ResultOrigin(string MemberName, string FilePath, int LineNumber)
{
    public override string ToString() => $"{MemberName} at {FilePath}:{LineNumber}";
}
