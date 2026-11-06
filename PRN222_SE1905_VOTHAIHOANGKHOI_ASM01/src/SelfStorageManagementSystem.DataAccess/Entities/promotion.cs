using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class promotion
{
    public long id { get; set; }

    public string code { get; set; } = null!;

    public string name { get; set; } = null!;

    public string? description { get; set; }

    public string discount_type { get; set; } = null!;

    public decimal discount_value { get; set; }

    public decimal? max_discount_amount { get; set; }

    public int? usage_limit { get; set; }

    public int? per_customer_limit { get; set; }

    public DateTimeOffset valid_from { get; set; }

    public DateTimeOffset valid_to { get; set; }

    public bool is_active { get; set; }

    public long? created_by { get; set; }

    public DateTimeOffset created_at { get; set; }

    public virtual user? created_byNavigation { get; set; }

    public virtual ICollection<promotion_redemption> promotion_redemptions { get; set; } = new List<promotion_redemption>();

    public virtual ICollection<promotion_rule> promotion_rules { get; set; } = new List<promotion_rule>();
}
