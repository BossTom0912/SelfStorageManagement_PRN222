using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class payment
{
    public long id { get; set; }

    public long customer_id { get; set; }

    public long target_invoice_id { get; set; }

    public decimal amount { get; set; }

    public string currency { get; set; } = null!;

    public string method { get; set; } = null!;

    public string provider { get; set; } = null!;

    public string? provider_transaction_id { get; set; }

    public string idempotency_key { get; set; } = null!;

    public string status { get; set; } = null!;

    public DateTimeOffset? paid_at { get; set; }

    public string? failure_reason { get; set; }

    public string metadata { get; set; } = null!;

    public DateTimeOffset created_at { get; set; }

    public DateTimeOffset updated_at { get; set; }

    public virtual customer_profile customer { get; set; } = null!;

    public virtual ICollection<payment_allocation> payment_allocations { get; set; } = new List<payment_allocation>();

    public virtual ICollection<refund> refunds { get; set; } = new List<refund>();

    public virtual invoice target_invoice { get; set; } = null!;
}
