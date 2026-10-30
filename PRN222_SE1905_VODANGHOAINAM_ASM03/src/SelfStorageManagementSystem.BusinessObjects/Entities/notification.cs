using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class notification
{
    public long id { get; set; }

    public long user_id { get; set; }

    public string channel { get; set; } = null!;

    public string template_code { get; set; } = null!;

    public string? subject { get; set; }

    public string payload { get; set; } = null!;

    public string status { get; set; } = null!;

    public DateTimeOffset scheduled_at { get; set; }

    public DateTimeOffset? sent_at { get; set; }

    public short attempts { get; set; }

    public string? last_error { get; set; }

    public string? deduplication_key { get; set; }

    public DateTimeOffset created_at { get; set; }

    public virtual user user { get; set; } = null!;
}
