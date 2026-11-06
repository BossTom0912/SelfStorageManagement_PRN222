using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class invoice
{
    public long id { get; set; }

    public string invoice_no { get; set; } = null!;

    public long customer_id { get; set; }

    public long? reservation_id { get; set; }

    public long? agreement_id { get; set; }

    public long? ticket_charge_proposal_id { get; set; }

    public string? billing_period { get; set; }

    public DateOnly issue_date { get; set; }

    public DateOnly due_date { get; set; }

    public string currency { get; set; } = null!;

    public decimal subtotal_amount { get; set; }

    public decimal discount_amount { get; set; }

    public decimal tax_amount { get; set; }

    public decimal total_amount { get; set; }

    public decimal paid_amount { get; set; }

    public string status { get; set; } = null!;

    public DateTimeOffset? opened_at { get; set; }

    public DateTimeOffset? voided_at { get; set; }

    public DateTimeOffset created_at { get; set; }

    public DateTimeOffset updated_at { get; set; }

    public virtual rental_agreement? agreement { get; set; }

    public virtual customer_profile customer { get; set; } = null!;

    public virtual delinquency_case? delinquency_case { get; set; }

    public virtual ICollection<invoice_line> invoice_lines { get; set; } = new List<invoice_line>();

    public virtual ICollection<payment_allocation> payment_allocations { get; set; } = new List<payment_allocation>();

    public virtual ICollection<payment> payments { get; set; } = new List<payment>();

    public virtual promotion_redemption? promotion_redemption { get; set; }

    public virtual reservation? reservation { get; set; }

    public virtual ticket_charge_proposal? ticket_charge_proposal { get; set; }
}
