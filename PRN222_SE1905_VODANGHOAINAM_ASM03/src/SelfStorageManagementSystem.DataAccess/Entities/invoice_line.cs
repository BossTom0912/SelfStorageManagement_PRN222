using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class invoice_line
{
    public long id { get; set; }

    public long invoice_id { get; set; }

    public string line_type { get; set; } = null!;

    public string description { get; set; } = null!;

    public decimal quantity { get; set; }

    public decimal unit_price { get; set; }

    public decimal? line_amount { get; set; }

    public string metadata { get; set; } = null!;

    public virtual invoice invoice { get; set; } = null!;
}
