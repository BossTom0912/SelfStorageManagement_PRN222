using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class promotion_rule
{
    public long id { get; set; }

    public long promotion_id { get; set; }

    public string rule_type { get; set; } = null!;

    public string _operator { get; set; } = null!;

    public string rule_value { get; set; } = null!;

    public virtual promotion promotion { get; set; } = null!;
}
