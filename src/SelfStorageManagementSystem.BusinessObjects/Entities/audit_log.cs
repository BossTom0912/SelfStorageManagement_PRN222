using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class audit_log
{
    public long id { get; set; }

    public long? actor_user_id { get; set; }

    public string? actor_role { get; set; }

    public string entity_schema { get; set; } = null!;

    public string entity_type { get; set; } = null!;

    public string entity_id { get; set; } = null!;

    public string action { get; set; } = null!;

    public string? old_values { get; set; }

    public string? new_values { get; set; }

    public string? request_id { get; set; }

    public string? ip_address { get; set; }

    public DateTimeOffset occurred_at { get; set; }

    public virtual user? actor_user { get; set; }
}
