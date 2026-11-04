namespace Sethika.Domain.Core;

/// <summary>
/// A value of a seeded list (<c>core.LookupValues</c>): a payment method, a need's category, a unit
/// (plan 09, M20). Reference data the application only reads.
/// </summary>
public class LookupValue
{
    /// <summary><c>PAYMENT_METHOD</c>'s type code in <c>core.LookupTypes</c>.</summary>
    public const string PaymentMethod = "PAYMENT_METHOD";

    public int Id { get; set; }
    public short LookupTypeId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? NameSi { get; set; }
    public string? NameTa { get; set; }
    public short SortOrder { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>A seeded list (<c>core.LookupTypes</c>), by its code: <c>PAYMENT_METHOD</c>, <c>UNIT</c>, ...</summary>
public class LookupType
{
    public short Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
}
