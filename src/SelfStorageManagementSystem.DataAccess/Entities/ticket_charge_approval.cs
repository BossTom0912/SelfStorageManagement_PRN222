using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class ticket_charge_approval
{
    public long proposal_id { get; set; }

    public string decision { get; set; } = null!;

    public long decided_by { get; set; }

    public string? reason { get; set; }

    public DateTimeOffset decided_at { get; set; }

    public virtual employee_profile decided_byNavigation { get; set; } = null!;

    public virtual ticket_charge_proposal proposal { get; set; } = null!;
}
