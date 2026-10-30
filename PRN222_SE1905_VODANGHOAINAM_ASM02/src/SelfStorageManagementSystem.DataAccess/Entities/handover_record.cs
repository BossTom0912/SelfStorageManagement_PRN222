using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class handover_record
{
    public long id { get; set; }

    public long agreement_id { get; set; }

    public long unit_allocation_id { get; set; }

    public long inspection_id { get; set; }

    public long handled_by { get; set; }

    public string handover_type { get; set; } = null!;

    public string? customer_signature_ref { get; set; }

    public string? staff_signature_ref { get; set; }

    public DateTimeOffset? customer_signed_at { get; set; }

    public DateTimeOffset? staff_signed_at { get; set; }

    public string? notes { get; set; }

    public DateTimeOffset created_at { get; set; }

    public virtual rental_agreement agreement { get; set; } = null!;

    public virtual employee_profile handled_byNavigation { get; set; } = null!;

    public virtual inspection inspection { get; set; } = null!;

    public virtual unit_allocation unit_allocation { get; set; } = null!;
}
