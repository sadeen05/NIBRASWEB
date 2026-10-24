using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NIBRAS;

namespace NIBRAS.API.Services;

/// <summary>
/// The private, non-static land-document store. Files are written with a random name below a
/// configured root; the original name is kept only as display metadata. A PDF, JPEG or PNG is
/// accepted only when its signature matches, so a renamed executable is refused.
/// </summary>
public sealed class DocumentStorageService : IDocumentStorage
{
    private const int HashBufferSize = 81_920;
    private const int HeaderSize = 8;

    private readonly string _root;
    private readonly string _webRoot;
    private readonly long _maxFileSizeBytes;
    private readonly ILogger<DocumentStorageService> _logger;

    public DocumentStorageService(
        IOptions<DocumentStorageOptions> options,
        IHostEnvironment environment,
        ILogger<DocumentStorageService> logger)
    {
        _logger = logger;
        _maxFileSizeBytes = options.Value.MaxFileSizeBytes;

        var configuredRoot = options.Value.StorageRoot?.Trim();

        // Outside Development the store must be configured explicitly and absolutely: the
        // App_Data fallback and a relative path are not acceptable production durability or
        // safety guarantees.
        if (!environment.IsDevelopment())
        {
            if (string.IsNullOrWhiteSpace(configuredRoot))
            {
                throw new InvalidOperationException(
                    "Documents:StorageRoot must be configured explicitly outside Development; the App_Data fallback is not allowed.");
            }

            if (!Path.IsPathFullyQualified(configuredRoot))
            {
                throw new InvalidOperationException(
                    "Documents:StorageRoot must be a fully qualified absolute path outside Development.");
            }
        }

        _root = DocumentStoragePath.ResolveRoot(environment.ContentRootPath, configuredRoot);
        _webRoot = DocumentStoragePath.ResolveWebRoot(environment.ContentRootPath);

        if (DocumentStoragePath.IsInside(_root, _webRoot))
        {
            throw new InvalidOperationException(
                "Documents:StorageRoot must not be inside wwwroot; documents are only served through the authenticated download endpoint.");
        }

        if (_maxFileSizeBytes <= 0)
        {
            throw new InvalidOperationException("Documents:MaxFileSizeBytes must be greater than zero.");
        }
    }

    public async Task<StoredDocument> SaveAsync(
        IFormFile file,
        int landId,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length <= 0)
        {
            throw new ValidationFailedException("The document file is required and must not be empty.");
        }

        if (file.Length > _maxFileSizeBytes)
        {
            throw new ValidationFailedException(
                $"The document file must not exceed {_maxFileSizeBytes / (1024 * 1024)} MiB.");
        }

        EnsureRoot();

        var originalFileName = NormalizeOriginalFileName(file.FileName);
        var tempDirectory = Path.Combine(_root, ".tmp");
        Directory.CreateDirectory(tempDirectory);

        var tempPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.tmp");
        string? finalPath = null;
        var moved = false;

        try
        {
            var header = new byte[HeaderSize];
            var headerLength = 0;
            long totalBytes = 0;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = file.OpenReadStream())
            await using (var output = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                HashBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[HashBufferSize];
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    totalBytes += read;
                    if (totalBytes > _maxFileSizeBytes)
                    {
                        throw new ValidationFailedException(
                            $"The document file must not exceed {_maxFileSizeBytes / (1024 * 1024)} MiB.");
                    }

                    if (headerLength < header.Length)
                    {
                        var copy = Math.Min(read, header.Length - headerLength);
                        buffer.AsSpan(0, copy).CopyTo(header.AsSpan(headerLength));
                        headerLength += copy;
                    }

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await output.FlushAsync(cancellationToken);
            }

            if (totalBytes == 0)
            {
                throw new ValidationFailedException("The document file is required and must not be empty.");
            }

            var contentType = DetectContentType(header, headerLength);
            if (contentType is null)
            {
                throw new ValidationFailedException(
                    "The document must be a PDF, JPEG or PNG file.");
            }

            var extension = contentType switch
            {
                "application/pdf" => ".pdf",
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                _ => throw new ValidationFailedException("Unsupported document type."),
            };

            var relativePath = Path.Combine(
                $"land-{landId}",
                $"{Guid.NewGuid():N}{extension}").Replace('\\', '/');

            finalPath = ResolvePath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            File.Move(tempPath, finalPath);
            moved = true;

            var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            return new StoredDocument(
                relativePath,
                originalFileName,
                contentType,
                totalBytes,
                sha256);
        }
        catch
        {
            TryDelete(tempPath);
            if (moved && finalPath is not null)
            {
                TryDelete(finalPath);
            }

            throw;
        }
    }

    public Stream OpenRead(string relativePath)
    {
        var fullPath = ResolvePath(relativePath);
        if (!File.Exists(fullPath))
        {
            throw new NotFoundException("The document file is not available.");
        }

        return new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            HashBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            TryDelete(ResolvePath(relativePath));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed cleanup must not hide the original database failure. The orphan file is
            // private and never referenced by a row; it is logged by type only.
            _logger.LogWarning(
                "A stored document file could not be removed after a failed upload ({ErrorType}).",
                ex.GetType().Name);
        }

        return Task.CompletedTask;
    }

    private void EnsureRoot()
    {
        Directory.CreateDirectory(_root);
    }

    private string ResolvePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ValidationFailedException("The document path is missing.");
        }

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_root, normalized));
        if (!DocumentStoragePath.IsInside(fullPath, _root))
        {
            throw new ValidationFailedException("The document path is invalid.");
        }

        return fullPath;
    }

    private static string NormalizeOriginalFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/')).Trim();
        name = new string(name.Where(character => !char.IsControl(character)).ToArray());
        if (string.IsNullOrWhiteSpace(name))
        {
            return "document";
        }

        if (name.Length <= 255)
        {
            return name;
        }

        // Keep the extension (truncated if the client sent an absurdly long one) and make the
        // base name fit. The result must fit the nvarchar(255) column even when the extension
        // alone is longer than the column.
        var extension = Path.GetExtension(name);
        if (extension.Length > 64)
        {
            extension = extension[..64];
        }

        var baseName = Path.GetFileNameWithoutExtension(name);
        var available = Math.Max(0, 255 - extension.Length);
        if (baseName.Length > available)
        {
            baseName = baseName[..available];
        }

        var result = baseName + extension;
        return string.IsNullOrWhiteSpace(result) ? "document" : result;
    }

    private static string? DetectContentType(byte[] header, int headerLength)
    {
        var span = header.AsSpan(0, headerLength);
        if (span.StartsWith("%PDF-"u8))
        {
            return "application/pdf";
        }

        if (span.Length >= 3 && span[0] == 0xFF && span[1] == 0xD8 && span[2] == 0xFF)
        {
            return "image/jpeg";
        }

        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (span.StartsWith(png))
        {
            return "image/png";
        }

        return null;
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                "A temporary document file could not be removed ({ErrorType}).",
                ex.GetType().Name);
        }
    }
}
