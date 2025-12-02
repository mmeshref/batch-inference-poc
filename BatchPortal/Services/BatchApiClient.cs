using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace BatchPortal.Services;

public sealed class BatchApiClient
{
    private const string UserIdHeader = "X-User-Id";
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _serializerOptions;

    public BatchApiClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        var baseUrl = configuration["ApiGateway:BaseUrl"]
                      ?? throw new InvalidOperationException("ApiGateway:BaseUrl not configured.");

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseAddress))
        {
            throw new InvalidOperationException("ApiGateway:BaseUrl must be an absolute URI.");
        }

        _httpClient.BaseAddress = baseAddress;
    }

    public async Task<string> UploadFileAsync(IFormFile file, string userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        using var content = new MultipartFormDataContent();
        var fileStream = file.OpenReadStream();
        var streamContent = new StreamContent(fileStream);
        if (!string.IsNullOrWhiteSpace(file.ContentType))
        {
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
        }

        content.Add(streamContent, "file", file.FileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/files")
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation(UserIdHeader, userId);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccess(response);

        var payload = await response.Content.ReadFromJsonAsync<FileUploadResponse>(_serializerOptions, cancellationToken)
                      ?? throw new InvalidOperationException("File upload response payload was empty.");

        if (payload.Id == Guid.Empty)
        {
            throw new InvalidOperationException("File upload response did not contain a valid identifier.");
        }

        return payload.Id.ToString();
    }

    public async Task<string> CreateBatchAsync(
        string inputFileId,
        string userId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!Guid.TryParse(inputFileId, out var inputGuid))
        {
            throw new ArgumentException("Input file id must be a valid GUID.", nameof(inputFileId));
        }

        var payload = new CreateBatchPayload(
            inputGuid,
            null);

        var json = JsonSerializer.Serialize(payload, _serializerOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/batches")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(UserIdHeader, userId);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccess(response);

        var batchResponse = await response.Content.ReadFromJsonAsync<BatchCreationResponse>(_serializerOptions, cancellationToken)
                             ?? throw new InvalidOperationException("Batch creation response payload was empty.");

        if (batchResponse.Id == Guid.Empty)
        {
            throw new InvalidOperationException("Batch creation response did not contain a valid identifier.");
        }

        return batchResponse.Id.ToString();
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException(
            $"API request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}): {body}");
    }

    private sealed record FileUploadResponse(Guid Id);

    private sealed record BatchCreationResponse(Guid Id, string Status);

    private sealed record CreateBatchPayload(
        Guid InputFileId,
        Dictionary<string, string>? Metadata);
}

