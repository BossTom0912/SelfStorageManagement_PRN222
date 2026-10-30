using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class authorized_access_member
{
    public long id { get; set; }

    public long agreement_id { get; set; }

    public string full_name { get; set; } = null!;

    public string? identity_fingerprint { get; set; }

    public string? relationship_to_customer { get; set; }

    public DateTimeOffset valid_from { get; set; }

    public DateTimeOffset? valid_to { get; set; }

    public string status { get; set; } = null!;

    public long created_by { get; set; }

    public DateTimeOffset created_at { get; set; }

    public virtual ICollection<access_credential> access_credentials { get; set; } = new List<access_credential>();

    public virtual rental_agreement agreement { get; set; } = null!;

    public virtual user created_byNavigation { get; set; } = null!;
}
