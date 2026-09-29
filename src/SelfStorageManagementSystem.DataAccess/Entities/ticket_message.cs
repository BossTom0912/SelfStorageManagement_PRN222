using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class ticket_message
{
    public long id { get; set; }

    public long ticket_id { get; set; }

    public long? author_user_id { get; set; }

    public string body { get; set; } = null!;

    public bool is_internal { get; set; }

    public DateTimeOffset created_at { get; set; }

    public virtual user? author_user { get; set; }

    public virtual support_ticket ticket { get; set; } = null!;

    public virtual ICollection<ticket_attachment> ticket_attachmentmessages { get; set; } = new List<ticket_attachment>();

    public virtual ICollection<ticket_attachment> ticket_attachmentticket_messages { get; set; } = new List<ticket_attachment>();
}
