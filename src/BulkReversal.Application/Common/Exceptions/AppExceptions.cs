namespace BulkReversal.Application.Common.Exceptions;

/// <summary>Requested resource does not exist. Maps to HTTP 404.</summary>
public class NotFoundAppException : Exception
{
    public NotFoundAppException(string entity, object key)
        : base($"{entity} '{key}' was not found.") { }
}

/// <summary>Request is well-formed but violates a business/state rule. Maps to HTTP 409.</summary>
public class ConflictAppException : Exception
{
    public ConflictAppException(string message) : base(message) { }
}

/// <summary>Caller is authenticated but not permitted to perform this action. Maps to HTTP 403.</summary>
public class ForbiddenAppException : Exception
{
    public ForbiddenAppException(string message) : base(message) { }
}

/// <summary>Input failed validation. Maps to HTTP 400. Carries field-level errors for ProblemDetails.</summary>
public class ValidationAppException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationAppException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public ValidationAppException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] })
    {
    }
}
