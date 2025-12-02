using Shared;

namespace ApiGateway.Services;

public interface IFileService
{
    Task<FileEntity> SaveFileAsync(
        string userId,
        string originalFilename,
        Stream content,
        string purpose,
        CancellationToken cancellationToken);
}

