using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class ticket_charge_proposal
{
    public long id { get; set; }

    public long ticket_id { get; set; }

    public long proposed_by { get; set; }

    public string description { get; set; } = null!;

    public decimal amount { get; set; }

    public string status { get; set; } = null!;

    public DateTimeOffset created_at { get; set; }

    public DateTimeOffset updated_at { get; set; }

    public virtual invoice? invoice { get; set; }

    public virtual employee_profile proposed_byNavigation { get; set; } = null!;

    public virtual support_ticket ticket { get; set; } = null!;

    public virtual ticket_charge_approval? ticket_charge_approval { get; set; }
}
