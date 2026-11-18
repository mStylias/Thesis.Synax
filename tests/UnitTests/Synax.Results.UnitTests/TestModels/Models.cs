namespace Synax.Results.UnitTests.TestModels;

public sealed class UserDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
}

public sealed class AppError
{
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
}
