using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class refund_approval
{
    public long refund_id { get; set; }

    public string decision { get; set; } = null!;

    public long decided_by { get; set; }

    public string? reason { get; set; }

    public DateTimeOffset decided_at { get; set; }

    public virtual user decided_byNavigation { get; set; } = null!;

    public virtual refund refund { get; set; } = null!;
}
