using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class customer_profile
{
    public long user_id { get; set; }

    public string full_name { get; set; } = null!;

    public string? identity_number { get; set; }

    public DateOnly? date_of_birth { get; set; }

    public string? address { get; set; }

    public string? emergency_contact_name { get; set; }

    public string? emergency_contact_phone { get; set; }

    public DateTimeOffset created_at { get; set; }

    public DateTimeOffset updated_at { get; set; }

    public virtual ICollection<invoice> invoices { get; set; } = new List<invoice>();

    public virtual ICollection<payment> payments { get; set; } = new List<payment>();

    public virtual ICollection<promotion_redemption> promotion_redemptions { get; set; } = new List<promotion_redemption>();

    public virtual ICollection<rental_agreement> rental_agreements { get; set; } = new List<rental_agreement>();

    public virtual ICollection<reservation> reservations { get; set; } = new List<reservation>();

    public virtual ICollection<service_rating> service_ratings { get; set; } = new List<service_rating>();

    public virtual ICollection<support_ticket> support_tickets { get; set; } = new List<support_ticket>();

    public virtual user user { get; set; } = null!;
}
