using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class move_out_request
{
    public long id { get; set; }

    public long agreement_id { get; set; }

    public long requested_by { get; set; }

    public DateOnly requested_move_out_date { get; set; }

    public long? appointment_id { get; set; }

    public string status { get; set; } = null!;

    public string? reason { get; set; }

    public DateTimeOffset? finalized_at { get; set; }

    public DateTimeOffset created_at { get; set; }

    public DateTimeOffset updated_at { get; set; }

    public virtual rental_agreement agreement { get; set; } = null!;

    public virtual appointment? appointment { get; set; }

    public virtual user requested_byNavigation { get; set; } = null!;
}
