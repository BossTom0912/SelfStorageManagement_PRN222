using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class appointment
{
    public long id { get; set; }

    public long facility_id { get; set; }

    public long? reservation_id { get; set; }

    public long? agreement_id { get; set; }

    public long? assigned_staff_id { get; set; }

    public string appointment_type { get; set; } = null!;

    public DateTimeOffset starts_at { get; set; }

    public DateTimeOffset ends_at { get; set; }

    public string status { get; set; } = null!;

    public string? notes { get; set; }

    public DateTimeOffset created_at { get; set; }

    public DateTimeOffset updated_at { get; set; }

    public virtual rental_agreement? agreement { get; set; }

    public virtual employee_profile? assigned_staff { get; set; }

    public virtual facility facility { get; set; } = null!;

    public virtual ICollection<move_out_request> move_out_requests { get; set; } = new List<move_out_request>();

    public virtual reservation? reservation { get; set; }
}
