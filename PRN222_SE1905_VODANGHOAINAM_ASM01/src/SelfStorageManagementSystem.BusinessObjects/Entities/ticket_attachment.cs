using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class ticket_attachment
{
    public long id { get; set; }

    public long ticket_id { get; set; }

    public long? message_id { get; set; }

    public long uploaded_by { get; set; }

    public string file_name { get; set; } = null!;

    public string mime_type { get; set; } = null!;

    public long file_size_bytes { get; set; }

    public string object_url { get; set; } = null!;

    public string? sha256 { get; set; }

    public DateTimeOffset created_at { get; set; }

    public virtual ticket_message? message { get; set; }

    public virtual support_ticket ticket { get; set; } = null!;

    public virtual ticket_message? ticket_message { get; set; }

    public virtual user uploaded_byNavigation { get; set; } = null!;
}
