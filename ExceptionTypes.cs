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
