using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class access_credential
{
    public long id { get; set; }

    public long agreement_id { get; set; }

    public long? authorized_member_id { get; set; }

    public string credential_type { get; set; } = null!;

    public string? external_secret_ref { get; set; }

    public string? secret_digest { get; set; }

    public string? display_hint { get; set; }

    public DateTimeOffset issued_at { get; set; }

    public DateTimeOffset? expires_at { get; set; }

    public string status { get; set; } = null!;

    public long? issued_by { get; set; }

    public DateTimeOffset? revoked_at { get; set; }

    public DateTimeOffset created_at { get; set; }

    public virtual ICollection<access_event> access_events { get; set; } = new List<access_event>();

    public virtual rental_agreement agreement { get; set; } = null!;

    public virtual authorized_access_member? authorized_member { get; set; }

    public virtual employee_profile? issued_byNavigation { get; set; }
}
