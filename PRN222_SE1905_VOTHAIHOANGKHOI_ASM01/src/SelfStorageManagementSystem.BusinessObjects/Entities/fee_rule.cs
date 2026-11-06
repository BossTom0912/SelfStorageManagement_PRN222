using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class fee_rule
{
    public long id { get; set; }

    public long? facility_id { get; set; }

    public string code { get; set; } = null!;

    public string fee_type { get; set; } = null!;

    public string calculation_method { get; set; } = null!;

    public decimal? amount { get; set; }

    public decimal? rate_percent { get; set; }

    public int grace_days { get; set; }

    public string conditions { get; set; } = null!;

    public DateOnly valid_from { get; set; }

    public DateOnly? valid_to { get; set; }

    public bool is_active { get; set; }

    public long? created_by { get; set; }

    public DateTimeOffset created_at { get; set; }

    public virtual user? created_byNavigation { get; set; }

    public virtual facility? facility { get; set; }
}
