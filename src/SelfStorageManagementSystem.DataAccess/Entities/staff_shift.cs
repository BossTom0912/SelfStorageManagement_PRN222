using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class staff_shift
{
    public long id { get; set; }

    public long facility_id { get; set; }

    public string shift_name { get; set; } = null!;

    public DateTimeOffset starts_at { get; set; }

    public DateTimeOffset ends_at { get; set; }

    public string status { get; set; } = null!;

    public long created_by { get; set; }

    public DateTimeOffset created_at { get; set; }

    public virtual user created_byNavigation { get; set; } = null!;

    public virtual facility facility { get; set; } = null!;

    public virtual ICollection<shift_assignment> shift_assignments { get; set; } = new List<shift_assignment>();

    public virtual ICollection<staff_task> staff_tasks { get; set; } = new List<staff_task>();
}
