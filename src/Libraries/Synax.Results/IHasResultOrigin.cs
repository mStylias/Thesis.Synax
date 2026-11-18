namespace Synax.Results;

/// <summary>
/// Exposes a creation origin on a typed error. When an error implementing this interface is
/// implicitly converted to a failed result, the origin transfers to the result — giving the
/// terse <c>return error;</c> form the same origin tracking as <c>Result.Fail(error)</c>,
/// with no stack walking. Error factories capture the origin via caller-info attributes
/// (e.g. <c>HttpError.NotFound(...)</c> records the site that created the error).
/// </summary>
public interface IHasResultOrigin
{
    ResultOrigin? ResultOrigin { get; }
}
