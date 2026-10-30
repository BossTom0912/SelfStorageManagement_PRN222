using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class refund
{
    public long id { get; set; }

    public long payment_id { get; set; }

    public long? agreement_id { get; set; }

    public decimal amount { get; set; }

    public string currency { get; set; } = null!;

    public string reason { get; set; } = null!;

    public string provider { get; set; } = null!;

    public string? provider_refund_id { get; set; }

    public string idempotency_key { get; set; } = null!;

    public string status { get; set; } = null!;

    public long? requested_by { get; set; }

    public DateTimeOffset? refunded_at { get; set; }

    public DateTimeOffset created_at { get; set; }

    public DateTimeOffset updated_at { get; set; }

    public virtual rental_agreement? agreement { get; set; }

    public virtual payment payment { get; set; } = null!;

    public virtual refund_approval? refund_approval { get; set; }

    public virtual user? requested_byNavigation { get; set; }
}
