using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class reservation
{
    public long id { get; set; }

    public string reservation_code { get; set; } = null!;

    public long customer_id { get; set; }

    public long facility_id { get; set; }

    public long unit_type_id { get; set; }

    public long facility_rate_id { get; set; }

    public DateOnly start_date { get; set; }

    public DateOnly end_date { get; set; }

    public decimal monthly_rate_snapshot { get; set; }

    public decimal deposit_snapshot { get; set; }

    public decimal booking_fee_snapshot { get; set; }

    public decimal discount_snapshot { get; set; }

    public decimal quoted_total { get; set; }

    public DateTimeOffset hold_until { get; set; }

    public string status { get; set; } = null!;

    public DateTimeOffset? confirmed_at { get; set; }

    public DateTimeOffset? cancelled_at { get; set; }

    public string? cancellation_reason { get; set; }

    public DateTimeOffset created_at { get; set; }

    public DateTimeOffset updated_at { get; set; }

    public virtual ICollection<appointment> appointments { get; set; } = new List<appointment>();

    public virtual customer_profile customer { get; set; } = null!;

    public virtual facility facility { get; set; } = null!;

    public virtual facility_rate facility_rate { get; set; } = null!;

    public virtual ICollection<identity_verification> identity_verifications { get; set; } = new List<identity_verification>();

    public virtual ICollection<inspection> inspections { get; set; } = new List<inspection>();

    public virtual ICollection<invoice> invoices { get; set; } = new List<invoice>();

    public virtual promotion_redemption? promotion_redemption { get; set; }

    public virtual rental_agreement? rental_agreement { get; set; }

    public virtual unit_allocation? unit_allocation { get; set; }

    public virtual unit_type unit_type { get; set; } = null!;
}
