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
