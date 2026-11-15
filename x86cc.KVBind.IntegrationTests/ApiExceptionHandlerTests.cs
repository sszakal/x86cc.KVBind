using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using x86cc.KVBind.Sample.Api;

namespace x86cc.KVBind.IntegrationTests;

// Only what the request got wrong is answered with 400 and its message; a defect or bad stored data that
// surfaces as the same exception types stays unhandled (a 500), so its message never reaches the client.
public sealed class ApiExceptionHandlerTests
{
    public static TheoryData<Exception> ServerErrors() => new()
    {
        new InvalidOperationException("Sequence contains no elements"),
        new FormatException("The input string was not in a correct format."),
        new JsonException("Unable to resolve KV value type 'X'."),
    };

    [Fact]
    public async Task ARequestError_IsA400_WithItsMessage()
    {
        var (handled, context, body) = await Handle(new ApiRequestException("Field 'Count' takes a single value."));

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        body.Should().Contain("Field 'Count' takes a single value.");
    }

    [Theory]
    [MemberData(nameof(ServerErrors))]
    public async Task AServerSideFailure_IsLeftUnhandled(Exception exception)
    {
        var (handled, _, body) = await Handle(exception);

        handled.Should().BeFalse();
        body.Should().BeEmpty();
    }

    [Fact]
    public void WhatKVBindRejectsOnRequestInput_BecomesARequestError()
    {
        var act = () => ApiRequestException.Run(() => throw new InvalidOperationException("Unable to resolve path segment 'Nope'."));

        act.Should().Throw<ApiRequestException>().WithMessage("*'Nope'*").WithInnerException<InvalidOperationException>();
    }

    private static async Task<(bool Handled, HttpContext Context, string Body)> Handle(Exception exception)
    {
        var context = new DefaultHttpContext();
        using var stream = new MemoryStream();
        context.Response.Body = stream;

        var handled = await new ApiExceptionHandler().TryHandleAsync(context, exception, CancellationToken.None);

        return (handled, context, System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }
}
