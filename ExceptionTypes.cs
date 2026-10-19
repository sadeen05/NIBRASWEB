namespace NIBRAS;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}

public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message)
    {
    }
}

public class BusinessRuleException : Exception
{
    public BusinessRuleException()
    {
    }

    public BusinessRuleException(string message) : base(message)
    {
    }
}

public sealed class ValidationFailedException : Exception
{
    public ValidationFailedException(string message) : base(message)
    {
    }
}

/// <summary>
/// The address of the account has not been confirmed yet, so the account may not be used.
/// The message never contains a credential, a token or any detail about the account.
/// </summary>
public sealed class EmailNotVerifiedException : Exception
{
    public EmailNotVerifiedException(string message) : base(message)
    {
    }
}
