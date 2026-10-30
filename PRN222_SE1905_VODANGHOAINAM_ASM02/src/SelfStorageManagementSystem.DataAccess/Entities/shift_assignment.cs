using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class shift_assignment
{
    public long shift_id { get; set; }

    public long employee_id { get; set; }

    public string duty_role { get; set; } = null!;

    public DateTimeOffset? check_in_at { get; set; }

    public DateTimeOffset? check_out_at { get; set; }

    public string status { get; set; } = null!;

    public virtual employee_profile employee { get; set; } = null!;

    public virtual staff_shift shift { get; set; } = null!;
}
