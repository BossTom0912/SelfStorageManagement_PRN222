using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class staff_facility_assignment
{
    public long id { get; set; }

    public long employee_id { get; set; }

    public long facility_id { get; set; }

    public string assignment_role { get; set; } = null!;

    public DateTimeOffset starts_at { get; set; }

    public DateTimeOffset? ends_at { get; set; }

    public long? assigned_by { get; set; }

    public DateTimeOffset created_at { get; set; }

    public virtual user? assigned_byNavigation { get; set; }

    public virtual employee_profile employee { get; set; } = null!;

    public virtual facility facility { get; set; } = null!;
}
