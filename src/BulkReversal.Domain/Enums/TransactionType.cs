namespace BulkReversal.Domain.Enums;

/// <summary>Transaction channel/type as captured on the Reversal Upload Template (BRD Section 6, Col B).</summary>
public enum TransactionType
{
    Nip = 1,
    BillPayment = 2,
    Card = 3
}
