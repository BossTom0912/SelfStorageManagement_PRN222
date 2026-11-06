using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class inspection
{
    public long id { get; set; }

    public long facility_id { get; set; }

    public long? storage_unit_id { get; set; }

    public long? reservation_id { get; set; }

    public long? agreement_id { get; set; }

    public long inspected_by { get; set; }

    public string inspection_type { get; set; } = null!;

    public string status { get; set; } = null!;

    public string? overall_condition { get; set; }

    public string? summary { get; set; }

    public DateTimeOffset? inspected_at { get; set; }

    public DateTimeOffset created_at { get; set; }

    public virtual rental_agreement? agreement { get; set; }

    public virtual facility facility { get; set; } = null!;

    public virtual ICollection<handover_record> handover_records { get; set; } = new List<handover_record>();

    public virtual employee_profile inspected_byNavigation { get; set; } = null!;

    public virtual ICollection<inspection_item> inspection_items { get; set; } = new List<inspection_item>();

    public virtual ICollection<maintenance_work_order> maintenance_work_orders { get; set; } = new List<maintenance_work_order>();

    public virtual reservation? reservation { get; set; }

    public virtual storage_unit? storage_unit { get; set; }
}
