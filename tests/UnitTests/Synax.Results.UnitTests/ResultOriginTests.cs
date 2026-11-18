namespace Synax.Results.UnitTests;

public class ResultOriginTests
{
    [Test]
    public async Task Constructor_WhenCalled_ShouldSetProperties()
    {
        // Arrange & Act
        var origin = new ResultOrigin("MyMethod", "/path/to/file.cs", 42);

        // Assert
        await Assert.That(origin.MemberName).IsEqualTo("MyMethod");
        await Assert.That(origin.FilePath).IsEqualTo("/path/to/file.cs");
        await Assert.That(origin.LineNumber).IsEqualTo(42);
    }

    [Test]
    public async Task ToString_WhenCalled_ShouldFormatCorrectly()
    {
        // Arrange
        var origin = new ResultOrigin("DoWork", "/src/Service.cs", 15);

        // Act
        var text = origin.ToString();

        // Assert
        await Assert.That(text).IsEqualTo("DoWork at /src/Service.cs:15");
    }

    [Test]
    public async Task Equality_WhenSameValues_ShouldBeEqual()
    {
        // Arrange
        var a = new ResultOrigin("Method", "File.cs", 10);
        var b = new ResultOrigin("Method", "File.cs", 10);

        // Assert
        await Assert.That(a).IsEqualTo(b);
    }

    [Test]
    public async Task Equality_WhenDifferentValues_ShouldNotBeEqual()
    {
        // Arrange
        var a = new ResultOrigin("MethodA", "File.cs", 10);
        var b = new ResultOrigin("MethodB", "File.cs", 20);

        // Assert
        await Assert.That(a).IsNotEqualTo(b);
    }
}
