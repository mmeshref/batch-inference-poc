using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shared;

namespace ApiGateway.Services;

public sealed class FileService : IFileService
{
    private readonly BatchDbContext _dbContext;
    private readonly ILogger<FileService> _logger;
    private readonly string _basePath;

    public FileService(BatchDbContext dbContext, IConfiguration configuration, ILogger<FileService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
        _basePath = configuration["Storage:BasePath"] ?? "/tmp/dwb-files";
        Directory.CreateDirectory(_basePath);
    }

    public async Task<FileEntity> SaveFileAsync(
        string userId,
        string originalFilename,
        Stream content,
        string purpose,
        CancellationToken cancellationToken)
    {
        var fileId = Guid.NewGuid();
        var extension = Path.GetExtension(originalFilename);
        var storedFileName = string.IsNullOrWhiteSpace(extension)
            ? fileId.ToString()
            : $"{fileId}{extension}";
        var destinationPath = Path.Combine(_basePath, storedFileName);

        content.Position = 0;
        await using (var fileStream = File.Create(destinationPath))
        {
            await content.CopyToAsync(fileStream, cancellationToken);
        }

        var entity = new FileEntity
        {
            Id = fileId,
            UserId = userId,
            Filename = originalFilename,
            StoragePath = destinationPath,
            Purpose = purpose,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Files.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Stored file {FileId} for user {UserId} at {Path}", fileId, userId, destinationPath);
        return entity;
    }
}

