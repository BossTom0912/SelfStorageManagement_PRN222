using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class unit_allocation
{
    public long id { get; set; }

    public long storage_unit_id { get; set; }

    public long? reservation_id { get; set; }

    public long? agreement_id { get; set; }

    public string allocation_kind { get; set; } = null!;

    public DateOnly allocation_start_date { get; set; }

    public DateOnly allocation_end_date { get; set; }

    public string status { get; set; } = null!;

    public string? reason { get; set; }

    public long? assigned_by { get; set; }

    public DateTimeOffset created_at { get; set; }

    public DateTimeOffset? ended_at { get; set; }

    public virtual rental_agreement? agreement { get; set; }

    public virtual user? assigned_byNavigation { get; set; }

    public virtual ICollection<handover_record> handover_records { get; set; } = new List<handover_record>();

    public virtual reservation? reservation { get; set; }

    public virtual storage_unit storage_unit { get; set; } = null!;
}
