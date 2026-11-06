using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class rental_agreement
{
    public long id { get; set; }

    public string agreement_no { get; set; } = null!;

    public long reservation_id { get; set; }

    public long customer_id { get; set; }

    public long facility_id { get; set; }

    public long policy_version_id { get; set; }

    public DateOnly start_date { get; set; }

    public DateOnly end_date { get; set; }

    public decimal monthly_rate_snapshot { get; set; }

    public decimal deposit_snapshot { get; set; }

    public decimal deposit_balance { get; set; }

    public string status { get; set; } = null!;

    public DateTimeOffset? signed_at { get; set; }

    public DateTimeOffset? checked_in_at { get; set; }

    public DateTimeOffset? checked_out_at { get; set; }

    public DateOnly? actual_end_date { get; set; }

    public DateTimeOffset created_at { get; set; }

    public DateTimeOffset updated_at { get; set; }

    public virtual ICollection<access_credential> access_credentials { get; set; } = new List<access_credential>();

    public virtual ICollection<appointment> appointments { get; set; } = new List<appointment>();

    public virtual ICollection<authorized_access_member> authorized_access_members { get; set; } = new List<authorized_access_member>();

    public virtual customer_profile customer { get; set; } = null!;

    public virtual ICollection<delinquency_case> delinquency_cases { get; set; } = new List<delinquency_case>();

    public virtual facility facility { get; set; } = null!;

    public virtual ICollection<handover_record> handover_records { get; set; } = new List<handover_record>();

    public virtual ICollection<inspection> inspections { get; set; } = new List<inspection>();

    public virtual ICollection<invoice> invoices { get; set; } = new List<invoice>();

    public virtual move_out_request? move_out_request { get; set; }

    public virtual policy_version policy_version { get; set; } = null!;

    public virtual ICollection<refund> refunds { get; set; } = new List<refund>();

    public virtual rental_renewal? rental_renewal { get; set; }

    public virtual reservation reservation { get; set; } = null!;

    public virtual ICollection<support_ticket> support_ticketagreements { get; set; } = new List<support_ticket>();

    public virtual ICollection<support_ticket> support_ticketrental_agreements { get; set; } = new List<support_ticket>();

    public virtual ICollection<unit_allocation> unit_allocations { get; set; } = new List<unit_allocation>();

    public virtual unit_transfer_request? unit_transfer_request { get; set; }
}
