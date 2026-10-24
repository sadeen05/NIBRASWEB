using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace NIBRAS.API.Services;

/// <summary>
/// Startup validation for the private document store. Outside Development the root is mandatory
/// and must be a fully qualified absolute path; in every environment it must stay outside
/// <c>wwwroot</c>. The service repeats the checks as defence in depth, but the host must not be
/// allowed to start with a configuration that can only fail on the first upload.
/// </summary>
public sealed class DocumentStorageOptionsValidator : IValidateOptions<DocumentStorageOptions>
{
    private readonly IHostEnvironment _environment;

    public DocumentStorageOptionsValidator(IHostEnvironment environment) => _environment = environment;

    public ValidateOptionsResult Validate(string? name, DocumentStorageOptions options)
    {
        var failures = new List<string>();

        if (options.MaxFileSizeBytes <= 0)
        {
            failures.Add("Documents:MaxFileSizeBytes must be greater than zero.");
        }
        else if (options.MaxFileSizeBytes > DocumentStorageOptions.DefaultMaxFileSizeBytes)
        {
            failures.Add(
                $"Documents:MaxFileSizeBytes must not exceed {DocumentStorageOptions.DefaultMaxFileSizeBytes} bytes.");
        }

        var configuredRoot = options.StorageRoot?.Trim();

        if (!_environment.IsDevelopment())
        {
            if (string.IsNullOrWhiteSpace(configuredRoot))
            {
                failures.Add(
                    "Documents:StorageRoot must be configured explicitly outside Development; the App_Data fallback is not allowed.");
            }
            else if (!Path.IsPathFullyQualified(configuredRoot))
            {
                failures.Add(
                    "Documents:StorageRoot must be a fully qualified absolute path outside Development.");
            }
        }

        try
        {
            var root = DocumentStoragePath.ResolveRoot(_environment.ContentRootPath, configuredRoot);
            var webRoot = DocumentStoragePath.ResolveWebRoot(_environment.ContentRootPath);
            if (DocumentStoragePath.IsInside(root, webRoot))
            {
                failures.Add(
                    "Documents:StorageRoot must not be inside wwwroot; documents are only served through the authenticated download endpoint.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or NotSupportedException
                                   or PathTooLongException
                                   or System.Security.SecurityException)
        {
            failures.Add("Documents:StorageRoot must be a valid file-system path.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
