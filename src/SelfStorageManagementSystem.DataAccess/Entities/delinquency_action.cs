using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class delinquency_action
{
    public long id { get; set; }

    public long delinquency_case_id { get; set; }

    public string action_type { get; set; } = null!;

    public long? performed_by { get; set; }

    public string details { get; set; } = null!;

    public DateTimeOffset occurred_at { get; set; }

    public virtual delinquency_case delinquency_case { get; set; } = null!;

    public virtual user? performed_byNavigation { get; set; }
}
