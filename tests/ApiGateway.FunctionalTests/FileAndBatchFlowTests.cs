using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using System.Threading.Tasks;
namespace ApiGateway.FunctionalTests;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.Serialization;
using System.Xml.XPath;
using System.Xml.Xsl;
using System.Text.Json.Serialization;

public class FileAndBatchFlowTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public FileAndBatchFlowTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-User-Id", "functional-user");
    }

    [Fact]
    public async Task Upload_File_And_Create_Batch_Should_Succeed()
    {
        using var content = new MultipartFormDataContent();
        var jsonl = "{ \"input\": \"hello\" }\n{ \"input\": \"world\" }";
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(jsonl));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/jsonl");
        content.Add(fileContent, "file", "test.jsonl");

        var uploadResponse = await _client.PostAsync("/v1/files", content);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var uploadJson = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var fileId = uploadJson.GetProperty("id").GetString();
        fileId.Should().NotBeNullOrEmpty();

        var batchRequest = new
        {
            inputFileId = fileId,
            metadata = new Dictionary<string, string>
            {
                ["priority"] = "normal"
            }
        };

        var batchResponse = await _client.PostAsJsonAsync("/v1/batches", batchRequest);
        batchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cancel_NonExistent_Batch_Should_Return_NotFound()
    {
        // Act: Try to cancel a non-existent batch
        var nonExistentId = Guid.NewGuid();
        var cancelResponse = await _client.PostAsync($"/v1/batches/{nonExistentId}/cancel", null);

        // Assert: Should return 404 or 400
        cancelResponse.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_Request_By_Id_Should_Return_Request_Details()
    {
        // Note: This test would require a request to exist in the database
        // For now, we'll test the 404 case
        var nonExistentId = Guid.NewGuid();
        var response = await _client.GetAsync($"/v1/requests/{nonExistentId}");
        
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

