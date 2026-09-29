using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class unit_transfer_request
{
    public long id { get; set; }

    public long agreement_id { get; set; }

    public long requested_unit_type_id { get; set; }

    public long from_unit_id { get; set; }

    public long? to_unit_id { get; set; }

    public DateOnly requested_effective_date { get; set; }

    public string reason { get; set; } = null!;

    public string status { get; set; } = null!;

    public long requested_by { get; set; }

    public long? reviewed_by { get; set; }

    public DateTimeOffset? reviewed_at { get; set; }

    public DateTimeOffset? completed_at { get; set; }

    public DateTimeOffset created_at { get; set; }

    public virtual rental_agreement agreement { get; set; } = null!;

    public virtual storage_unit from_unit { get; set; } = null!;

    public virtual user requested_byNavigation { get; set; } = null!;

    public virtual unit_type requested_unit_type { get; set; } = null!;

    public virtual user? reviewed_byNavigation { get; set; }

    public virtual storage_unit? to_unit { get; set; }
}
