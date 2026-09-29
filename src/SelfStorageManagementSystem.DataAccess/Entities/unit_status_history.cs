using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class unit_status_history
{
    public long id { get; set; }

    public long storage_unit_id { get; set; }

    public string? old_status { get; set; }

    public string new_status { get; set; } = null!;

    public string? reason { get; set; }

    public long? changed_by { get; set; }

    public DateTimeOffset changed_at { get; set; }

    public virtual user? changed_byNavigation { get; set; }

    public virtual storage_unit storage_unit { get; set; } = null!;
}
