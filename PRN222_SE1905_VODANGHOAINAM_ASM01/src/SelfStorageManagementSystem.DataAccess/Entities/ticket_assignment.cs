using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class ticket_assignment
{
    public long id { get; set; }

    public long ticket_id { get; set; }

    public long employee_id { get; set; }

    public long? assigned_by { get; set; }

    public DateTimeOffset assigned_at { get; set; }

    public DateTimeOffset? ended_at { get; set; }

    public string? end_reason { get; set; }

    public virtual user? assigned_byNavigation { get; set; }

    public virtual employee_profile employee { get; set; } = null!;

    public virtual support_ticket ticket { get; set; } = null!;
}
