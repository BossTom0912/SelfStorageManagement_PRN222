using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class access_event
{
    public long id { get; set; }

    public long? credential_id { get; set; }

    public long access_point_id { get; set; }

    public DateTimeOffset occurred_at { get; set; }

    public string result { get; set; } = null!;

    public string? reason { get; set; }

    public string? external_event_id { get; set; }

    public string metadata { get; set; } = null!;

    public virtual access_point access_point { get; set; } = null!;

    public virtual access_credential? credential { get; set; }
}
