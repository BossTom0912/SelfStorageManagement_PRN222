using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class payment_allocation
{
    public long payment_id { get; set; }

    public long invoice_id { get; set; }

    public decimal allocated_amount { get; set; }

    public DateTimeOffset allocated_at { get; set; }

    public virtual invoice invoice { get; set; } = null!;

    public virtual payment payment { get; set; } = null!;
}
