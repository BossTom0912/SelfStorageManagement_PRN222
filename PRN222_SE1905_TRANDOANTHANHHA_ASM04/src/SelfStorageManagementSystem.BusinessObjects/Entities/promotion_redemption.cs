using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class promotion_redemption
{
    public long id { get; set; }

    public long promotion_id { get; set; }

    public long customer_id { get; set; }

    public long? reservation_id { get; set; }

    public long? invoice_id { get; set; }

    public decimal discount_amount { get; set; }

    public string status { get; set; } = null!;

    public DateTimeOffset redeemed_at { get; set; }

    public virtual customer_profile customer { get; set; } = null!;

    public virtual invoice? invoice { get; set; }

    public virtual promotion promotion { get; set; } = null!;

    public virtual reservation? reservation { get; set; }
}
