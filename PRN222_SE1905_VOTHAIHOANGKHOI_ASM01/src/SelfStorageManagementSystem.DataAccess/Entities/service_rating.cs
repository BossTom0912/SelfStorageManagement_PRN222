using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class service_rating
{
    public long ticket_id { get; set; }

    public long customer_id { get; set; }

    public short score { get; set; }

    public string? comment { get; set; }

    public DateTimeOffset created_at { get; set; }

    public virtual customer_profile customer { get; set; } = null!;

    public virtual support_ticket ticket { get; set; } = null!;
}
