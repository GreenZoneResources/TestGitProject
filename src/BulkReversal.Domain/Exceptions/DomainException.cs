namespace BulkReversal.Domain.Exceptions;

/// <summary>Raised when an entity's business rules/invariants are violated.</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
