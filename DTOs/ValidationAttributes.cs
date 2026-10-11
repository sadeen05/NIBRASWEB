using System.ComponentModel.DataAnnotations;
using NIBRAS.API.Services;

namespace NIBRAS.API.DTOs;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class NoControlCharactersAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string text)
            return ValidationResult.Success;

        return text.Any(character => character < '\u0020')
            ? new ValidationResult($"{validationContext.DisplayName} contains invalid control characters.")
            : ValidationResult.Success;
    }
}

/// <summary>
/// SEC-17: free-text fields (reasons, review notes) must reject control characters
/// U+0000-U+001F except horizontal tab (U+0009) and line feed (U+000A), which the
/// specification permits in free text. Identifiers and file paths keep the stricter
/// <see cref="NoControlCharactersAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class NoControlCharactersExceptTabAndNewlineAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string text)
            return ValidationResult.Success;

        return text.Any(character =>
                character < '\u0020' && character != '\u0009' && character != '\u000A')
            ? new ValidationResult($"{validationContext.DisplayName} contains invalid control characters.")
            : ValidationResult.Success;
    }
}

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class MaxUtf8BytesAttribute(int maximumBytes) : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string text || PasswordPolicy.GetUtf8ByteCount(text) <= maximumBytes)
            return ValidationResult.Success;

        return new ValidationResult($"{validationContext.DisplayName} exceeds the supported byte length.");
    }
}

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class SafeRelativePathAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string path)
            return ValidationResult.Success;

        var hasScheme = path.Contains("://", StringComparison.Ordinal);
        var isRooted = path.StartsWith('/') || path.StartsWith('\\');
        var hasDriveLetter = path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';
        var hasParentSegment = path.Split('/', '\\').Any(segment => segment == "..");

        return hasScheme || isRooted || hasDriveLetter || hasParentSegment
            ? new ValidationResult($"{validationContext.DisplayName} must be a relative path with no scheme, drive letter, leading slash, or '..' segment.")
            : ValidationResult.Success;
    }
}
