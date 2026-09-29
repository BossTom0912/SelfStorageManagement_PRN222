using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class delinquency_case
{
    public long id { get; set; }

    public long agreement_id { get; set; }

    public long invoice_id { get; set; }

    public string status { get; set; } = null!;

    public DateTimeOffset opened_at { get; set; }

    public DateTimeOffset grace_ends_at { get; set; }

    public decimal outstanding_snapshot { get; set; }

    public DateTimeOffset? resolved_at { get; set; }

    public string? notes { get; set; }

    public virtual rental_agreement agreement { get; set; } = null!;

    public virtual ICollection<delinquency_action> delinquency_actions { get; set; } = new List<delinquency_action>();

    public virtual invoice invoice { get; set; } = null!;
}
