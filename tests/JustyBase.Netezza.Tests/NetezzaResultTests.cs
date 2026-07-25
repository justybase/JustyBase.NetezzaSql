namespace JustyBase.Netezza.Tests;

public sealed class NetezzaResultTests
{
    [Fact]
    public void Ok_CreatesSuccessfulResult()
    {
        var result = NetezzaResult<int>.Ok(42);

        Assert.True(result.Success);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Fail_CreatesFailedResult()
    {
        var result = NetezzaResult<int>.Fail("something went wrong");

        Assert.False(result.Success);
        Assert.Equal(0, result.Value);
        Assert.Equal("something went wrong", result.Error);
    }

    [Fact]
    public void GetValueOrThrow_ReturnsValueOnSuccess()
    {
        var result = NetezzaResult<string>.Ok("hello");

        Assert.Equal("hello", result.GetValueOrThrow());
    }

    [Fact]
    public void GetValueOrThrow_ThrowsOnFailure()
    {
        var result = NetezzaResult<string>.Fail("error");

        Assert.Throws<InvalidOperationException>(() => result.GetValueOrThrow());
    }

    [Fact]
    public void GetValueOrDefault_ReturnsValueOnSuccess()
    {
        var result = NetezzaResult<int>.Ok(42);

        Assert.Equal(42, result.GetValueOrDefault(-1));
    }

    [Fact]
    public void GetValueOrDefault_ReturnsDefaultOnFailure()
    {
        var result = NetezzaResult<int>.Fail("error");

        Assert.Equal(-1, result.GetValueOrDefault(-1));
    }

    [Fact]
    public void GetValueOrDefault_ReturnsNullDefaultOnFailureForReferenceType()
    {
        var result = NetezzaResult<string>.Fail("error");

        Assert.Null(result.GetValueOrDefault());
    }
}
