using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class support_ticket
{
    public long id { get; set; }

    public string ticket_no { get; set; } = null!;

    public long customer_id { get; set; }

    public long facility_id { get; set; }

    public long? agreement_id { get; set; }

    public long? storage_unit_id { get; set; }

    public string category { get; set; } = null!;

    public string priority { get; set; } = null!;

    public string subject { get; set; } = null!;

    public string description { get; set; } = null!;

    public string status { get; set; } = null!;

    public string? resolution { get; set; }

    public DateTimeOffset? resolved_at { get; set; }

    public DateTimeOffset created_at { get; set; }

    public DateTimeOffset updated_at { get; set; }

    public virtual rental_agreement? agreement { get; set; }

    public virtual customer_profile customer { get; set; } = null!;

    public virtual facility facility { get; set; } = null!;

    public virtual ICollection<maintenance_work_order> maintenance_work_orders { get; set; } = new List<maintenance_work_order>();

    public virtual rental_agreement? rental_agreement { get; set; }

    public virtual service_rating? service_rating { get; set; }

    public virtual ICollection<staff_task> staff_tasks { get; set; } = new List<staff_task>();

    public virtual storage_unit? storage_unit { get; set; }

    public virtual storage_unit? storage_unitNavigation { get; set; }

    public virtual ticket_assignment? ticket_assignment { get; set; }

    public virtual ICollection<ticket_attachment> ticket_attachments { get; set; } = new List<ticket_attachment>();

    public virtual ICollection<ticket_charge_proposal> ticket_charge_proposals { get; set; } = new List<ticket_charge_proposal>();

    public virtual ICollection<ticket_message> ticket_messages { get; set; } = new List<ticket_message>();
}
