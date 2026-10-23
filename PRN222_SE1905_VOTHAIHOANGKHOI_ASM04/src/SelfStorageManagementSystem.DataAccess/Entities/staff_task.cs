using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class staff_task
{
    public long id { get; set; }

    public long facility_id { get; set; }

    public long? shift_id { get; set; }

    public long? assigned_employee_id { get; set; }

    public long? ticket_id { get; set; }

    public long? maintenance_work_order_id { get; set; }

    public string task_type { get; set; } = null!;

    public string title { get; set; } = null!;

    public DateTimeOffset? due_at { get; set; }

    public string status { get; set; } = null!;

    public short progress_percent { get; set; }

    public long created_by { get; set; }

    public DateTimeOffset created_at { get; set; }

    public DateTimeOffset updated_at { get; set; }

    public virtual employee_profile? assigned_employee { get; set; }

    public virtual user created_byNavigation { get; set; } = null!;

    public virtual facility facility { get; set; } = null!;

    public virtual maintenance_work_order? maintenance_work_order { get; set; }

    public virtual staff_shift? shift { get; set; }

    public virtual support_ticket? ticket { get; set; }
}
