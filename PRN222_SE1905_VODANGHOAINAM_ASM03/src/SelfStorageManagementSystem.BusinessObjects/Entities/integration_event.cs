using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class integration_event
{
    public long id { get; set; }

    public string source { get; set; } = null!;

    public string external_event_id { get; set; } = null!;

    public string event_type { get; set; } = null!;

    public string payload { get; set; } = null!;

    public string status { get; set; } = null!;

    public string? error_message { get; set; }

    public DateTimeOffset received_at { get; set; }

    public DateTimeOffset? processed_at { get; set; }
}
