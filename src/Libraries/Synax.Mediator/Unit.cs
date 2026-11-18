namespace Synax.Mediator;

/// <summary>
/// Represents a void return type for commands that produce no result.
/// </summary>
public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>
{
    public static readonly Unit Value = default;

    public int CompareTo(Unit other) => 0;
    public bool Equals(Unit other) => true;
    public override bool Equals(object? obj) => obj is Unit;
    public override int GetHashCode() => 0;
    public override string ToString() => "()";

    public static bool operator ==(Unit left, Unit right) => true;
    public static bool operator !=(Unit left, Unit right) => false;
}
