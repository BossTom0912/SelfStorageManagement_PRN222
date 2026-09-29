using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class facility_rate
{
    public long id { get; set; }

    public long facility_id { get; set; }

    public long unit_type_id { get; set; }

    public decimal monthly_rate { get; set; }

    public decimal deposit_amount { get; set; }

    public decimal booking_fee { get; set; }

    public DateOnly valid_from { get; set; }

    public DateOnly? valid_to { get; set; }

    public long? created_by { get; set; }

    public DateTimeOffset created_at { get; set; }

    public virtual user? created_byNavigation { get; set; }

    public virtual facility facility { get; set; } = null!;

    public virtual ICollection<reservation> reservations { get; set; } = new List<reservation>();

    public virtual unit_type unit_type { get; set; } = null!;
}
