using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class rental_renewal
{
    public long id { get; set; }

    public long agreement_id { get; set; }

    public long requested_by { get; set; }

    public DateOnly old_end_date { get; set; }

    public DateOnly requested_end_date { get; set; }

    public DateOnly? approved_end_date { get; set; }

    public decimal old_monthly_rate { get; set; }

    public decimal? new_monthly_rate { get; set; }

    public string status { get; set; } = null!;

    public long? reviewed_by { get; set; }

    public DateTimeOffset? reviewed_at { get; set; }

    public DateTimeOffset created_at { get; set; }

    public virtual rental_agreement agreement { get; set; } = null!;

    public virtual user requested_byNavigation { get; set; } = null!;

    public virtual user? reviewed_byNavigation { get; set; }
}
