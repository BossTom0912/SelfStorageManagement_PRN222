using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class price_range
{
    public long id { get; set; }

    public long unit_type_id { get; set; }

    public decimal min_monthly_rate { get; set; }

    public decimal max_monthly_rate { get; set; }

    public DateOnly valid_from { get; set; }

    public DateOnly? valid_to { get; set; }

    public long? created_by { get; set; }

    public DateTimeOffset created_at { get; set; }

    public virtual user? created_byNavigation { get; set; }

    public virtual unit_type unit_type { get; set; } = null!;
}
