namespace BulkReversal.Domain.Constants;

/// <summary>
/// GL account codes for automated reversal posting (BRD Section 7). Carried forward from the
/// approved Business Case; kept as named constants so postings stay traceable to source GLs
/// even though the actual booking is performed by the Bank's core-banking system on reversal.
/// </summary>
public static class AccountingConstants
{
    public static class Nip
    {
        public const string OutwardGl = "NGN1150300010001";
        public const string NpsOutwardGl = "NGN1600300010001";
        public const string VatPayableGl = "NGN1720500010001";
        public const string ChargePl = "PL51250";
        public const string StampDutyGl = "NGN1513300010001";
    }

    public static class BillPayment
    {
        public const string UssdVasGl = "NGN1504900010001";
        public const string YarabAirtimeGl = "NGN1282400010001";
        public const string DigitalExpensePl = "PL51153";
    }

    public static class Card
    {
        public const string CardGl1 = "NGN1471300020001";
        public const string CardGl2 = "NGN1471600020001";
        public const string CardGl3 = "NGN1471100020001";
        public const string CardGl4 = "NGN1471400020001";
    }
}
