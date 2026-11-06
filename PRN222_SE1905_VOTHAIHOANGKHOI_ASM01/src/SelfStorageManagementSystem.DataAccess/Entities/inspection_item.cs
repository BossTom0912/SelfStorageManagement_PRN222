using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class inspection_item
{
    public long id { get; set; }

    public long inspection_id { get; set; }

    public string item_name { get; set; } = null!;

    public string condition { get; set; } = null!;

    public string? notes { get; set; }

    public string? photo_url { get; set; }

    public decimal charge_amount { get; set; }

    public virtual inspection inspection { get; set; } = null!;
}
