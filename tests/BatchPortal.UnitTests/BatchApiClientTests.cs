using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BatchPortal.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatchPortal.UnitTests;

public class BatchApiClientTests
{
    [Fact]
    public async Task GetOutputPreviewAsync_ShouldTruncate_WhenMoreLinesThanMax()
    {
        var handler = new FakeHandler("line1\nline2\nline3\n");
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost")
        };
        var client = new BatchApiClient(httpClient, NullLogger<BatchApiClient>.Instance);

        var (lines, truncated) = await client.GetOutputPreviewAsync(Guid.NewGuid(), maxLines: 2, CancellationToken.None);

        Assert.Equal(2, lines.Count);
        Assert.True(truncated);
        Assert.Equal("line1", lines[0]);
        Assert.Equal("line2", lines[1]);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly string _content;

        public FakeHandler(string content)
        {
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_content)
            };
            return Task.FromResult(response);
        }
    }
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_response);
    }

    private static BatchApiClient CreateClient(HttpResponseMessage response)
    {
        var handler = new StubHandler(response);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        return new BatchApiClient(httpClient, NullLogger<BatchApiClient>.Instance);
    }

    [Fact]
    public async Task CreateBatchAsync_Should_Throw_On_NonSuccess()
    {
        var client = CreateClient(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        Func<Task> act = () => client.CreateBatchAsync(Guid.NewGuid().ToString(), "user-1", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateBatchAsync_Should_Return_Id_On_Success()
    {
        var batchId = Guid.NewGuid();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { id = batchId, status = "queued" }),
                Encoding.UTF8,
                "application/json")
        };

        var client = CreateClient(response);

        var result = await client.CreateBatchAsync(Guid.NewGuid().ToString(), "user-2", CancellationToken.None);

        result.Should().Be(batchId.ToString());
    }
}

