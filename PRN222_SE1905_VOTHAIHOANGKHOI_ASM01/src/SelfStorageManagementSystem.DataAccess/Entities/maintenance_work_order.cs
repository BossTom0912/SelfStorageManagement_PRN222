using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class maintenance_work_order
{
    public long id { get; set; }

    public string work_order_no { get; set; } = null!;

    public long facility_id { get; set; }

    public long? storage_unit_id { get; set; }

    public long? source_ticket_id { get; set; }

    public long? source_inspection_id { get; set; }

    public long? assigned_employee_id { get; set; }

    public string title { get; set; } = null!;

    public string description { get; set; } = null!;

    public string priority { get; set; } = null!;

    public bool blocks_booking { get; set; }

    public string status { get; set; } = null!;

    public decimal? estimated_cost { get; set; }

    public decimal? actual_cost { get; set; }

    public long opened_by { get; set; }

    public long? verified_by { get; set; }

    public DateTimeOffset? started_at { get; set; }

    public DateTimeOffset? completed_at { get; set; }

    public DateTimeOffset created_at { get; set; }

    public DateTimeOffset updated_at { get; set; }

    public virtual employee_profile? assigned_employee { get; set; }

    public virtual facility facility { get; set; } = null!;

    public virtual user opened_byNavigation { get; set; } = null!;

    public virtual inspection? source_inspection { get; set; }

    public virtual support_ticket? source_ticket { get; set; }

    public virtual ICollection<staff_task> staff_tasks { get; set; } = new List<staff_task>();

    public virtual storage_unit? storage_unit { get; set; }

    public virtual employee_profile? verified_byNavigation { get; set; }
}
