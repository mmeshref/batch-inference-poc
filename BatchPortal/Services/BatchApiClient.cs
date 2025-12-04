using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BatchPortal.Services;

public sealed class BatchApiClient
{
    private const string UserIdHeader = "X-User-Id";
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly ILogger<BatchApiClient> _logger;

    public BatchApiClient(HttpClient httpClient, ILogger<BatchApiClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
        _serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        if (_httpClient.BaseAddress is null)
        {
            throw new InvalidOperationException("Batch API client requires HttpClient.BaseAddress to be configured.");
        }
    }

    public Uri? GetOutputFileDownloadUrl(Guid? outputFileId)
    {
        if (!outputFileId.HasValue)
        {
            return null;
        }

        var baseUrl = _httpClient.BaseAddress?.ToString()?.TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
        {
            return null;
        }

        var url = $"{baseUrl}/v1/files/{outputFileId.Value}/raw";
        return new Uri(url);
    }

    public async Task<(IReadOnlyList<string> lines, bool truncated)> GetOutputPreviewAsync(
        Guid outputFileId,
        int maxLines = 20,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"/v1/files/{outputFileId}/raw", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (Array.Empty<string>(), false);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var lines = new List<string>();
        string? line;
        var count = 0;
        while (count < maxLines && (line = await reader.ReadLineAsync()) != null)
        {
            lines.Add(line);
            count++;
        }

        var truncated = !reader.EndOfStream;
        return (lines, truncated);
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
        int priority,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!Guid.TryParse(inputFileId, out var inputGuid))
        {
            throw new ArgumentException("Input file id must be a valid GUID.", nameof(inputFileId));
        }

        var metadata = new Dictionary<string, string>();
        if (priority >= 10)
        {
            metadata["priority"] = "high";
        }
        else if (priority >= 5)
        {
            metadata["priority"] = "medium";
        }

        var payload = new CreateBatchPayload(
            inputGuid,
            metadata.Count > 0 ? metadata : null);

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

    public async Task<bool> CancelBatchAsync(Guid batchId, string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/batches/{batchId}/cancel");
        request.Headers.TryAddWithoutValidation(UserIdHeader, userId);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Cannot cancel batch {BatchId}: {Reason}", batchId, body);
            return false;
        }

        await EnsureSuccess(response);
        return true;
    }

    private async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("API request failed with status {StatusCode}: {Body}", response.StatusCode, body);
        throw new InvalidOperationException(
            $"API request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}).");
    }

    private sealed record FileUploadResponse(Guid Id);

    private sealed record BatchCreationResponse(Guid Id, string Status);

    private sealed record CreateBatchPayload(
        Guid InputFileId,
        Dictionary<string, string>? Metadata);
}

