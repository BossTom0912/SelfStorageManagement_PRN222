using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class employee_profile
{
    public long user_id { get; set; }

    public string employee_code { get; set; } = null!;

    public string full_name { get; set; } = null!;

    public DateOnly hire_date { get; set; }

    public string employment_status { get; set; } = null!;

    public DateTimeOffset created_at { get; set; }

    public DateTimeOffset updated_at { get; set; }

    public virtual ICollection<access_credential> access_credentials { get; set; } = new List<access_credential>();

    public virtual ICollection<appointment> appointments { get; set; } = new List<appointment>();

    public virtual ICollection<handover_record> handover_records { get; set; } = new List<handover_record>();

    public virtual ICollection<identity_verification> identity_verifications { get; set; } = new List<identity_verification>();

    public virtual ICollection<inspection> inspections { get; set; } = new List<inspection>();

    public virtual ICollection<maintenance_work_order> maintenance_work_orderassigned_employees { get; set; } = new List<maintenance_work_order>();

    public virtual ICollection<maintenance_work_order> maintenance_work_orderverified_byNavigations { get; set; } = new List<maintenance_work_order>();

    public virtual ICollection<refund_approval> refund_approvals { get; set; } = new List<refund_approval>();

    public virtual ICollection<shift_assignment> shift_assignments { get; set; } = new List<shift_assignment>();

    public virtual ICollection<staff_facility_assignment> staff_facility_assignments { get; set; } = new List<staff_facility_assignment>();

    public virtual ICollection<staff_task> staff_tasks { get; set; } = new List<staff_task>();

    public virtual ICollection<ticket_assignment> ticket_assignments { get; set; } = new List<ticket_assignment>();

    public virtual ICollection<ticket_charge_approval> ticket_charge_approvals { get; set; } = new List<ticket_charge_approval>();

    public virtual ICollection<ticket_charge_proposal> ticket_charge_proposals { get; set; } = new List<ticket_charge_proposal>();

    public virtual user user { get; set; } = null!;
}
