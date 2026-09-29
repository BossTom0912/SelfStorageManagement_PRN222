using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class user
{
    public long id { get; set; }

    public string email { get; set; } = null!;

    public string? phone_number { get; set; }

    public string password_hash { get; set; } = null!;

    public string status { get; set; } = null!;

    public DateTimeOffset? last_login_at { get; set; }

    public DateTimeOffset created_at { get; set; }

    public DateTimeOffset updated_at { get; set; }

    public virtual ICollection<audit_log> audit_logs { get; set; } = new List<audit_log>();

    public virtual ICollection<authorized_access_member> authorized_access_members { get; set; } = new List<authorized_access_member>();

    public virtual customer_profile? customer_profile { get; set; }

    public virtual ICollection<delinquency_action> delinquency_actions { get; set; } = new List<delinquency_action>();

    public virtual employee_profile? employee_profile { get; set; }

    public virtual ICollection<facility_rate> facility_rates { get; set; } = new List<facility_rate>();

    public virtual ICollection<fee_rule> fee_rules { get; set; } = new List<fee_rule>();

    public virtual ICollection<login_history> login_histories { get; set; } = new List<login_history>();

    public virtual ICollection<maintenance_work_order> maintenance_work_orders { get; set; } = new List<maintenance_work_order>();

    public virtual ICollection<move_out_request> move_out_requests { get; set; } = new List<move_out_request>();

    public virtual ICollection<notification> notifications { get; set; } = new List<notification>();

    public virtual ICollection<policy_version> policy_versions { get; set; } = new List<policy_version>();

    public virtual ICollection<price_range> price_ranges { get; set; } = new List<price_range>();

    public virtual ICollection<promotion> promotions { get; set; } = new List<promotion>();

    public virtual ICollection<refund> refunds { get; set; } = new List<refund>();

    public virtual ICollection<rental_renewal> rental_renewalrequested_byNavigations { get; set; } = new List<rental_renewal>();

    public virtual ICollection<rental_renewal> rental_renewalreviewed_byNavigations { get; set; } = new List<rental_renewal>();

    public virtual ICollection<staff_facility_assignment> staff_facility_assignments { get; set; } = new List<staff_facility_assignment>();

    public virtual ICollection<staff_shift> staff_shifts { get; set; } = new List<staff_shift>();

    public virtual ICollection<staff_task> staff_tasks { get; set; } = new List<staff_task>();

    public virtual ICollection<ticket_assignment> ticket_assignments { get; set; } = new List<ticket_assignment>();

    public virtual ICollection<ticket_attachment> ticket_attachments { get; set; } = new List<ticket_attachment>();

    public virtual ICollection<ticket_message> ticket_messages { get; set; } = new List<ticket_message>();

    public virtual ICollection<unit_allocation> unit_allocations { get; set; } = new List<unit_allocation>();

    public virtual ICollection<unit_status_history> unit_status_histories { get; set; } = new List<unit_status_history>();

    public virtual ICollection<unit_transfer_request> unit_transfer_requestrequested_byNavigations { get; set; } = new List<unit_transfer_request>();

    public virtual ICollection<unit_transfer_request> unit_transfer_requestreviewed_byNavigations { get; set; } = new List<unit_transfer_request>();

    public virtual ICollection<user_role> user_rolegranted_byNavigations { get; set; } = new List<user_role>();

    public virtual ICollection<user_role> user_roleusers { get; set; } = new List<user_role>();
}
