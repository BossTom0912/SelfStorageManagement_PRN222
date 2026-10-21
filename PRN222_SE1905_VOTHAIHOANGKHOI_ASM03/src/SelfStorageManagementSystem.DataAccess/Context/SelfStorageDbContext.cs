using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using SelfStorageManagementSystem.DataAccess.Entities;

namespace SelfStorageManagementSystem.DataAccess.Context;

public partial class SelfStorageDbContext : DbContext
{
    public SelfStorageDbContext(DbContextOptions<SelfStorageDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<StorageUnitHaTDT> StorageUnitHaTDTs { get; set; }

    public virtual DbSet<UnitTypeHaTDT> UnitTypeHaTDTs { get; set; }

    public virtual DbSet<access_credential> access_credentials { get; set; }

    public virtual DbSet<access_event> access_events { get; set; }

    public virtual DbSet<access_point> access_points { get; set; }

    public virtual DbSet<appointment> appointments { get; set; }

    public virtual DbSet<audit_log> audit_logs { get; set; }

    public virtual DbSet<authorized_access_member> authorized_access_members { get; set; }

    public virtual DbSet<customer_profile> customer_profiles { get; set; }

    public virtual DbSet<delinquency_action> delinquency_actions { get; set; }

    public virtual DbSet<delinquency_case> delinquency_cases { get; set; }

    public virtual DbSet<employee_profile> employee_profiles { get; set; }

    public virtual DbSet<facility> facilities { get; set; }

    public virtual DbSet<facility_area> facility_areas { get; set; }

    public virtual DbSet<facility_rate> facility_rates { get; set; }

    public virtual DbSet<fee_rule> fee_rules { get; set; }

    public virtual DbSet<handover_record> handover_records { get; set; }

    public virtual DbSet<identity_verification> identity_verifications { get; set; }

    public virtual DbSet<inspection> inspections { get; set; }

    public virtual DbSet<inspection_item> inspection_items { get; set; }

    public virtual DbSet<integration_event> integration_events { get; set; }

    public virtual DbSet<invoice> invoices { get; set; }

    public virtual DbSet<invoice_line> invoice_lines { get; set; }

    public virtual DbSet<login_history> login_histories { get; set; }

    public virtual DbSet<maintenance_work_order> maintenance_work_orders { get; set; }

    public virtual DbSet<move_out_request> move_out_requests { get; set; }

    public virtual DbSet<notification> notifications { get; set; }

    public virtual DbSet<payment> payments { get; set; }

    public virtual DbSet<payment_allocation> payment_allocations { get; set; }

    public virtual DbSet<policy_version> policy_versions { get; set; }

    public virtual DbSet<price_range> price_ranges { get; set; }

    public virtual DbSet<promotion> promotions { get; set; }

    public virtual DbSet<promotion_redemption> promotion_redemptions { get; set; }

    public virtual DbSet<promotion_rule> promotion_rules { get; set; }

    public virtual DbSet<refund> refunds { get; set; }

    public virtual DbSet<refund_approval> refund_approvals { get; set; }

    public virtual DbSet<rental_agreement> rental_agreements { get; set; }

    public virtual DbSet<rental_renewal> rental_renewals { get; set; }

    public virtual DbSet<reservation> reservations { get; set; }

    public virtual DbSet<role> roles { get; set; }

    public virtual DbSet<service_rating> service_ratings { get; set; }

    public virtual DbSet<shift_assignment> shift_assignments { get; set; }

    public virtual DbSet<staff_facility_assignment> staff_facility_assignments { get; set; }

    public virtual DbSet<staff_shift> staff_shifts { get; set; }

    public virtual DbSet<staff_task> staff_tasks { get; set; }

    public virtual DbSet<storage_unit> storage_units { get; set; }

    public virtual DbSet<support_ticket> support_tickets { get; set; }

    public virtual DbSet<ticket_assignment> ticket_assignments { get; set; }

    public virtual DbSet<ticket_attachment> ticket_attachments { get; set; }

    public virtual DbSet<ticket_charge_approval> ticket_charge_approvals { get; set; }

    public virtual DbSet<ticket_charge_proposal> ticket_charge_proposals { get; set; }

    public virtual DbSet<ticket_message> ticket_messages { get; set; }

    public virtual DbSet<unit_allocation> unit_allocations { get; set; }

    public virtual DbSet<unit_map_position> unit_map_positions { get; set; }

    public virtual DbSet<unit_status_history> unit_status_histories { get; set; }

    public virtual DbSet<unit_transfer_request> unit_transfer_requests { get; set; }

    public virtual DbSet<unit_type> unit_types { get; set; }

    public virtual DbSet<user> users { get; set; }

    public virtual DbSet<user_role> user_roles { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StorageUnitHaTDT>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("StorageUnitHaTDT", "core");

            entity.Property(e => e.StorageUnitHaTDTId).ValueGeneratedOnAdd();
            entity.Property(e => e.floor_label).HasMaxLength(255);
            entity.Property(e => e.notes).HasMaxLength(255);
            entity.Property(e => e.physical_status).HasMaxLength(255);
            entity.Property(e => e.unit_code).HasMaxLength(255);
            entity.Property(e => e.zone_label).HasMaxLength(255);
        });

        modelBuilder.Entity<UnitTypeHaTDT>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("UnitTypeHaTDT", "core");

            entity.Property(e => e.UnitTypeHaTDTId).ValueGeneratedOnAdd();
            entity.Property(e => e.area_m2).HasColumnType("numeric(10, 2)");
            entity.Property(e => e.code).HasMaxLength(255);
            entity.Property(e => e.description).HasMaxLength(255);
            entity.Property(e => e.height_m).HasColumnType("numeric(8, 2)");
            entity.Property(e => e.length_m).HasColumnType("numeric(8, 2)");
            entity.Property(e => e.max_weight_kg).HasColumnType("numeric(12, 2)");
            entity.Property(e => e.name).HasMaxLength(255);
            entity.Property(e => e.volume_m3).HasColumnType("numeric(12, 2)");
            entity.Property(e => e.width_m).HasColumnType("numeric(8, 2)");
        });

        modelBuilder.Entity<access_credential>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__access_c__3213E83F4BC232A8");

            entity.ToTable("access_credentials", "core", tb => tb.HasTrigger("trg_access_credentials_validate_scope"));

            entity.HasIndex(e => new { e.agreement_id, e.status }, "access_credentials_agreement_idx");

            entity.HasIndex(e => e.authorized_member_id, "access_credentials_authorized_member_id_idx").HasFilter("([authorized_member_id] IS NOT NULL)");

            entity.HasIndex(e => e.issued_by, "access_credentials_issued_by_idx").HasFilter("([issued_by] IS NOT NULL)");

            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.credential_type).HasMaxLength(255);
            entity.Property(e => e.display_hint).HasMaxLength(255);
            entity.Property(e => e.external_secret_ref).HasMaxLength(255);
            entity.Property(e => e.issued_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.secret_digest).HasMaxLength(255);
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("pending");

            entity.HasOne(d => d.agreement).WithMany(p => p.access_credentials)
                .HasForeignKey(d => d.agreement_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__access_cr__agree__46136164");

            entity.HasOne(d => d.authorized_member).WithMany(p => p.access_credentials)
                .HasForeignKey(d => d.authorized_member_id)
                .HasConstraintName("FK__access_cr__autho__4707859D");

            entity.HasOne(d => d.issued_byNavigation).WithMany(p => p.access_credentials)
                .HasForeignKey(d => d.issued_by)
                .HasConstraintName("FK__access_cr__issue__4BCC3ABA");
        });

        modelBuilder.Entity<access_event>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__access_e__3213E83F365B7538");

            entity.ToTable("access_events", "core");

            entity.HasIndex(e => e.external_event_id, "UQ__access_e__A01442886FC5E1AF").IsUnique();

            entity.HasIndex(e => new { e.credential_id, e.occurred_at }, "access_events_credential_occurred_idx")
                .IsDescending(false, true)
                .HasFilter("([credential_id] IS NOT NULL)");

            entity.HasIndex(e => new { e.access_point_id, e.occurred_at }, "access_events_point_occurred_idx").IsDescending(false, true);

            entity.Property(e => e.external_event_id).HasMaxLength(255);
            entity.Property(e => e.metadata).HasDefaultValue("{}");
            entity.Property(e => e.reason).HasMaxLength(255);
            entity.Property(e => e.result).HasMaxLength(255);

            entity.HasOne(d => d.access_point).WithMany(p => p.access_events)
                .HasForeignKey(d => d.access_point_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__access_ev__acces__536D5C82");

            entity.HasOne(d => d.credential).WithMany(p => p.access_events)
                .HasForeignKey(d => d.credential_id)
                .HasConstraintName("FK__access_ev__crede__52793849");
        });

        modelBuilder.Entity<access_point>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__access_p__3213E83F729D07FA");

            entity.ToTable("access_points", "core");

            entity.HasIndex(e => new { e.facility_id, e.code }, "UQ__access_p__21BF3E60ACC34DAF").IsUnique();

            entity.HasIndex(e => new { e.facility_id, e.status }, "access_points_facility_id_idx");

            entity.Property(e => e.access_point_type).HasMaxLength(255);
            entity.Property(e => e.code).HasMaxLength(255);
            entity.Property(e => e.external_device_code).HasMaxLength(255);
            entity.Property(e => e.name).HasMaxLength(255);
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("active");

            entity.HasOne(d => d.facility).WithMany(p => p.access_points)
                .HasForeignKey(d => d.facility_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__access_po__facil__405A880E");
        });

        modelBuilder.Entity<appointment>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__appointm__3213E83F9FF868F1");

            entity.ToTable("appointments", "core", tb => tb.HasTrigger("trg_appointments_validate_scope"));

            entity.HasIndex(e => e.agreement_id, "appointments_agreement_id_idx").HasFilter("([agreement_id] IS NOT NULL)");

            entity.HasIndex(e => e.assigned_staff_id, "appointments_assigned_staff_id_idx").HasFilter("([assigned_staff_id] IS NOT NULL)");

            entity.HasIndex(e => new { e.facility_id, e.starts_at, e.status }, "appointments_facility_schedule_idx");

            entity.HasIndex(e => e.reservation_id, "appointments_reservation_id_idx").HasFilter("([reservation_id] IS NOT NULL)");

            entity.Property(e => e.appointment_type).HasMaxLength(255);
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.notes).HasMaxLength(255);
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("scheduled");
            entity.Property(e => e.updated_at).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.agreement).WithMany(p => p.appointments)
                .HasForeignKey(d => d.agreement_id)
                .HasConstraintName("FK__appointme__agree__2334397B");

            entity.HasOne(d => d.assigned_staff).WithMany(p => p.appointments)
                .HasForeignKey(d => d.assigned_staff_id)
                .HasConstraintName("FK__appointme__assig__24285DB4");

            entity.HasOne(d => d.facility).WithMany(p => p.appointments)
                .HasForeignKey(d => d.facility_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__appointme__facil__214BF109");

            entity.HasOne(d => d.reservation).WithMany(p => p.appointments)
                .HasForeignKey(d => d.reservation_id)
                .HasConstraintName("FK__appointme__reser__22401542");
        });

        modelBuilder.Entity<audit_log>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__audit_lo__3213E83FAF2591FA");

            entity.ToTable("audit_logs", "core");

            entity.HasIndex(e => new { e.actor_user_id, e.occurred_at }, "audit_logs_actor_idx")
                .IsDescending(false, true)
                .HasFilter("([actor_user_id] IS NOT NULL)");

            entity.HasIndex(e => new { e.entity_type, e.entity_id, e.occurred_at }, "audit_logs_entity_idx").IsDescending(false, false, true);

            entity.HasIndex(e => e.request_id, "audit_logs_request_id_idx").HasFilter("([request_id] IS NOT NULL)");

            entity.Property(e => e.action).HasMaxLength(255);
            entity.Property(e => e.actor_role).HasMaxLength(255);
            entity.Property(e => e.entity_id).HasMaxLength(255);
            entity.Property(e => e.entity_schema)
                .HasMaxLength(255)
                .HasDefaultValue("core");
            entity.Property(e => e.entity_type).HasMaxLength(255);
            entity.Property(e => e.ip_address).HasMaxLength(45);
            entity.Property(e => e.occurred_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.request_id).HasMaxLength(255);

            entity.HasOne(d => d.actor_user).WithMany(p => p.audit_logs)
                .HasForeignKey(d => d.actor_user_id)
                .HasConstraintName("FK__audit_log__actor__76B698BF");
        });

        modelBuilder.Entity<authorized_access_member>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__authoriz__3213E83FD5A61B0C");

            entity.ToTable("authorized_access_members", "core");

            entity.HasIndex(e => new { e.agreement_id, e.status }, "authorized_access_members_agreement_idx");

            entity.HasIndex(e => e.created_by, "authorized_access_members_created_by_idx");

            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.full_name).HasMaxLength(255);
            entity.Property(e => e.identity_fingerprint).HasMaxLength(255);
            entity.Property(e => e.relationship_to_customer).HasMaxLength(255);
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("active");
            entity.Property(e => e.valid_from).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.agreement).WithMany(p => p.authorized_access_members)
                .HasForeignKey(d => d.agreement_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__authorize__agree__4F12BBB9");

            entity.HasOne(d => d.created_byNavigation).WithMany(p => p.authorized_access_members)
                .HasForeignKey(d => d.created_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__authorize__creat__52E34C9D");
        });

        modelBuilder.Entity<customer_profile>(entity =>
        {
            entity.HasKey(e => e.user_id).HasName("PK__customer__B9BE370FFAEACC7A");

            entity.ToTable("customer_profiles", "core");

            entity.HasIndex(e => e.identity_number, "customer_profiles_identity_number_uidx")
                .IsUnique()
                .HasFilter("([identity_number] IS NOT NULL)");

            entity.Property(e => e.user_id).ValueGeneratedNever();
            entity.Property(e => e.address).HasMaxLength(255);
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.emergency_contact_name).HasMaxLength(255);
            entity.Property(e => e.emergency_contact_phone).HasMaxLength(255);
            entity.Property(e => e.full_name).HasMaxLength(255);
            entity.Property(e => e.identity_number).HasMaxLength(255);
            entity.Property(e => e.updated_at).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.user).WithOne(p => p.customer_profile)
                .HasForeignKey<customer_profile>(d => d.user_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__customer___user___60A75C0F");
        });

        modelBuilder.Entity<delinquency_action>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__delinque__3213E83FA03B3509");

            entity.ToTable("delinquency_actions", "core");

            entity.HasIndex(e => new { e.delinquency_case_id, e.occurred_at }, "delinquency_actions_case_occurred_idx");

            entity.HasIndex(e => e.performed_by, "delinquency_actions_performed_by_idx").HasFilter("([performed_by] IS NOT NULL)");

            entity.Property(e => e.action_type).HasMaxLength(255);
            entity.Property(e => e.details).HasDefaultValue("{}");
            entity.Property(e => e.occurred_at).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.delinquency_case).WithMany(p => p.delinquency_actions)
                .HasForeignKey(d => d.delinquency_case_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__delinquen__delin__62AFA012");

            entity.HasOne(d => d.performed_byNavigation).WithMany(p => p.delinquency_actions)
                .HasForeignKey(d => d.performed_by)
                .HasConstraintName("FK__delinquen__perfo__6497E884");
        });

        modelBuilder.Entity<delinquency_case>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__delinque__3213E83FAA7BFAB8");

            entity.ToTable("delinquency_cases", "core", tb => tb.HasTrigger("trg_delinquency_cases_validate_scope"));

            entity.HasIndex(e => new { e.agreement_id, e.status, e.opened_at }, "delinquency_cases_agreement_idx").IsDescending(false, false, true);

            entity.HasIndex(e => e.invoice_id, "delinquency_cases_invoice_id_idx");

            entity.HasIndex(e => e.invoice_id, "delinquency_cases_one_open_invoice_uidx")
                .IsUnique()
                .HasFilter("([resolved_at] IS NULL)");

            entity.Property(e => e.notes).HasMaxLength(255);
            entity.Property(e => e.opened_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.outstanding_snapshot).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("grace");

            entity.HasOne(d => d.agreement).WithMany(p => p.delinquency_cases)
                .HasForeignKey(d => d.agreement_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__delinquen__agree__592635D8");

            entity.HasOne(d => d.invoice).WithOne(p => p.delinquency_case)
                .HasForeignKey<delinquency_case>(d => d.invoice_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__delinquen__invoi__5A1A5A11");
        });

        modelBuilder.Entity<employee_profile>(entity =>
        {
            entity.HasKey(e => e.user_id).HasName("PK__employee__B9BE370F23BC88FC");

            entity.ToTable("employee_profiles", "core");

            entity.HasIndex(e => e.employee_code, "UQ__employee__B0AA7345AF63F14A").IsUnique();

            entity.Property(e => e.user_id).ValueGeneratedNever();
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.employee_code).HasMaxLength(255);
            entity.Property(e => e.employment_status)
                .HasMaxLength(255)
                .HasDefaultValue("active");
            entity.Property(e => e.full_name).HasMaxLength(255);
            entity.Property(e => e.updated_at).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.user).WithOne(p => p.employee_profile)
                .HasForeignKey<employee_profile>(d => d.user_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__employee___user___66603565");
        });

        modelBuilder.Entity<facility>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__faciliti__3213E83FB42CB5DF");

            entity.ToTable("facilities", "core");

            entity.HasIndex(e => e.code, "UQ__faciliti__357D4CF9991155D7").IsUnique();

            entity.HasIndex(e => new { e.city, e.status }, "facilities_city_status_idx");

            entity.Property(e => e.address_line).HasMaxLength(255);
            entity.Property(e => e.city).HasMaxLength(255);
            entity.Property(e => e.code).HasMaxLength(255);
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.district).HasMaxLength(255);
            entity.Property(e => e.latitude).HasColumnType("numeric(9, 6)");
            entity.Property(e => e.longitude).HasColumnType("numeric(9, 6)");
            entity.Property(e => e.name).HasMaxLength(255);
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("active");
            entity.Property(e => e.timezone)
                .HasMaxLength(255)
                .HasDefaultValue("Asia/Ho_Chi_Minh");
            entity.Property(e => e.updated_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.ward).HasMaxLength(255);
        });

        modelBuilder.Entity<facility_area>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__facility__3213E83FAE0FA427");

            entity.ToTable("facility_areas", "core");

            entity.HasIndex(e => new { e.facility_id, e.code }, "UQ__facility__21BF3E60A76C8DEA").IsUnique();

            entity.HasIndex(e => new { e.id, e.facility_id }, "UQ__facility__C93D669457B28390").IsUnique();

            entity.HasIndex(e => e.parent_area_id, "facility_areas_parent_area_id_idx").HasFilter("([parent_area_id] IS NOT NULL)");

            entity.Property(e => e.area_type).HasMaxLength(255);
            entity.Property(e => e.code).HasMaxLength(255);
            entity.Property(e => e.is_active).HasDefaultValue(true);
            entity.Property(e => e.map_metadata).HasDefaultValue("{}");
            entity.Property(e => e.name).HasMaxLength(255);

            entity.HasOne(d => d.facility).WithMany(p => p.facility_areas)
                .HasForeignKey(d => d.facility_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__facility___facil__01142BA1");

            entity.HasOne(d => d.parent_area).WithMany(p => p.Inverseparent_area)
                .HasForeignKey(d => d.parent_area_id)
                .HasConstraintName("FK__facility___paren__02084FDA");

            entity.HasOne(d => d.facility_areaNavigation).WithMany(p => p.Inversefacility_areaNavigation)
                .HasPrincipalKey(p => new { p.id, p.facility_id })
                .HasForeignKey(d => new { d.parent_area_id, d.facility_id })
                .HasConstraintName("FK__facility_areas__08B54D69");
        });

        modelBuilder.Entity<facility_rate>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__facility__3213E83FECF06BDE");

            entity.ToTable("facility_rates", "core", tb =>
                {
                    tb.HasTrigger("trg_facility_rate_no_overlap");
                    tb.HasTrigger("trg_facility_rates_validate_price_range");
                });

            entity.HasIndex(e => e.created_by, "facility_rates_created_by_idx").HasFilter("([created_by] IS NOT NULL)");

            entity.HasIndex(e => e.unit_type_id, "facility_rates_unit_type_id_idx");

            entity.Property(e => e.booking_fee).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.deposit_amount).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.monthly_rate).HasColumnType("numeric(14, 2)");

            entity.HasOne(d => d.created_byNavigation).WithMany(p => p.facility_rates)
                .HasForeignKey(d => d.created_by)
                .HasConstraintName("FK__facility___creat__3D2915A8");

            entity.HasOne(d => d.facility).WithMany(p => p.facility_rates)
                .HasForeignKey(d => d.facility_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__facility___facil__37703C52");

            entity.HasOne(d => d.unit_type).WithMany(p => p.facility_rates)
                .HasForeignKey(d => d.unit_type_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__facility___unit___3864608B");
        });

        modelBuilder.Entity<fee_rule>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__fee_rule__3213E83F37FC3FB0");

            entity.ToTable("fee_rules", "core", tb => tb.HasTrigger("trg_fee_rule_no_overlap"));

            entity.HasIndex(e => e.created_by, "fee_rules_created_by_idx").HasFilter("([created_by] IS NOT NULL)");

            entity.HasIndex(e => e.facility_id, "fee_rules_facility_id_idx").HasFilter("([facility_id] IS NOT NULL)");

            entity.HasIndex(e => new { e.facility_id, e.code, e.valid_from }, "fee_rules_scope_code_start_uidx").IsUnique();

            entity.Property(e => e.amount).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.calculation_method).HasMaxLength(255);
            entity.Property(e => e.code).HasMaxLength(255);
            entity.Property(e => e.conditions).HasDefaultValue("{}");
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.fee_type).HasMaxLength(255);
            entity.Property(e => e.is_active).HasDefaultValue(true);
            entity.Property(e => e.rate_percent).HasColumnType("numeric(7, 4)");

            entity.HasOne(d => d.created_byNavigation).WithMany(p => p.fee_rules)
                .HasForeignKey(d => d.created_by)
                .HasConstraintName("FK__fee_rules__creat__531856C7");

            entity.HasOne(d => d.facility).WithMany(p => p.fee_rules)
                .HasForeignKey(d => d.facility_id)
                .HasConstraintName("FK__fee_rules__facil__498EEC8D");
        });

        modelBuilder.Entity<handover_record>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__handover__3213E83FA6B001A7");

            entity.ToTable("handover_records", "core", tb => tb.HasTrigger("trg_handover_records_validate_scope"));

            entity.HasIndex(e => new { e.agreement_id, e.created_at }, "handover_records_agreement_idx").IsDescending(false, true);

            entity.HasIndex(e => e.handled_by, "handover_records_handled_by_idx");

            entity.HasIndex(e => e.inspection_id, "handover_records_inspection_id_idx");

            entity.HasIndex(e => e.unit_allocation_id, "handover_records_unit_allocation_id_idx");

            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.customer_signature_ref).HasMaxLength(255);
            entity.Property(e => e.handover_type).HasMaxLength(255);
            entity.Property(e => e.notes).HasMaxLength(255);
            entity.Property(e => e.staff_signature_ref).HasMaxLength(255);

            entity.HasOne(d => d.agreement).WithMany(p => p.handover_records)
                .HasForeignKey(d => d.agreement_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__handover___agree__477199F1");

            entity.HasOne(d => d.handled_byNavigation).WithMany(p => p.handover_records)
                .HasForeignKey(d => d.handled_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__handover___handl__4A4E069C");

            entity.HasOne(d => d.inspection).WithMany(p => p.handover_records)
                .HasForeignKey(d => d.inspection_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__handover___inspe__4959E263");

            entity.HasOne(d => d.unit_allocation).WithMany(p => p.handover_records)
                .HasForeignKey(d => d.unit_allocation_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__handover___unit___4865BE2A");
        });

        modelBuilder.Entity<identity_verification>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__identity__3213E83F90D30DF8");

            entity.ToTable("identity_verifications", "core");

            entity.HasIndex(e => new { e.reservation_id, e.verified_at }, "identity_verifications_reservation_idx").IsDescending(false, true);

            entity.HasIndex(e => e.verified_by, "identity_verifications_verified_by_idx");

            entity.Property(e => e.document_fingerprint).HasMaxLength(255);
            entity.Property(e => e.notes).HasMaxLength(255);
            entity.Property(e => e.result).HasMaxLength(255);
            entity.Property(e => e.verification_method).HasMaxLength(255);
            entity.Property(e => e.verified_at).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.reservation).WithMany(p => p.identity_verifications)
                .HasForeignKey(d => d.reservation_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__identity___reser__2DB1C7EE");

            entity.HasOne(d => d.verified_byNavigation).WithMany(p => p.identity_verifications)
                .HasForeignKey(d => d.verified_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__identity___verif__2EA5EC27");
        });

        modelBuilder.Entity<inspection>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__inspecti__3213E83F2562B7F6");

            entity.ToTable("inspections", "core", tb => tb.HasTrigger("trg_inspections_validate_scope"));

            entity.HasIndex(e => e.agreement_id, "inspections_agreement_id_idx").HasFilter("([agreement_id] IS NOT NULL)");

            entity.HasIndex(e => new { e.facility_id, e.inspection_type, e.created_at }, "inspections_facility_type_date_idx").IsDescending(false, false, true);

            entity.HasIndex(e => e.inspected_by, "inspections_inspected_by_idx");

            entity.HasIndex(e => e.reservation_id, "inspections_reservation_id_idx").HasFilter("([reservation_id] IS NOT NULL)");

            entity.HasIndex(e => e.storage_unit_id, "inspections_storage_unit_id_idx").HasFilter("([storage_unit_id] IS NOT NULL)");

            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.inspection_type).HasMaxLength(255);
            entity.Property(e => e.overall_condition).HasMaxLength(255);
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("draft");
            entity.Property(e => e.summary).HasMaxLength(255);

            entity.HasOne(d => d.agreement).WithMany(p => p.inspections)
                .HasForeignKey(d => d.agreement_id)
                .HasConstraintName("FK__inspectio__agree__373B3228");

            entity.HasOne(d => d.facility).WithMany(p => p.inspections)
                .HasForeignKey(d => d.facility_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__inspectio__facil__345EC57D");

            entity.HasOne(d => d.inspected_byNavigation).WithMany(p => p.inspections)
                .HasForeignKey(d => d.inspected_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__inspectio__inspe__382F5661");

            entity.HasOne(d => d.reservation).WithMany(p => p.inspections)
                .HasForeignKey(d => d.reservation_id)
                .HasConstraintName("FK__inspectio__reser__36470DEF");

            entity.HasOne(d => d.storage_unit).WithMany(p => p.inspections)
                .HasForeignKey(d => d.storage_unit_id)
                .HasConstraintName("FK__inspectio__stora__3552E9B6");
        });

        modelBuilder.Entity<inspection_item>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__inspecti__3213E83FE5862117");

            entity.ToTable("inspection_items", "core");

            entity.HasIndex(e => e.inspection_id, "inspection_items_inspection_id_idx");

            entity.Property(e => e.charge_amount).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.condition).HasMaxLength(255);
            entity.Property(e => e.item_name).HasMaxLength(255);
            entity.Property(e => e.notes).HasMaxLength(255);
            entity.Property(e => e.photo_url).HasMaxLength(255);

            entity.HasOne(d => d.inspection).WithMany(p => p.inspection_items)
                .HasForeignKey(d => d.inspection_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__inspectio__inspe__41B8C09B");
        });

        modelBuilder.Entity<integration_event>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__integrat__3213E83F838BAE37");

            entity.ToTable("integration_events", "core");

            entity.HasIndex(e => new { e.source, e.external_event_id }, "UQ__integrat__86EB106A3DC52DA1").IsUnique();

            entity.HasIndex(e => new { e.received_at, e.id }, "integration_events_processing_queue_idx").HasFilter("([status] IN ('received', 'failed'))");

            entity.Property(e => e.error_message).HasMaxLength(255);
            entity.Property(e => e.event_type).HasMaxLength(255);
            entity.Property(e => e.external_event_id).HasMaxLength(255);
            entity.Property(e => e.received_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.source).HasMaxLength(255);
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("received");
        });

        modelBuilder.Entity<invoice>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__invoices__3213E83F00334C18");

            entity.ToTable("invoices", "core", tb =>
                {
                    tb.HasTrigger("trg_invoice_status_transition");
                    tb.HasTrigger("trg_invoices_validate_scope");
                });

            entity.HasIndex(e => e.ticket_charge_proposal_id, "UQ__invoices__DABC6CDB679892BA").IsUnique();

            entity.HasIndex(e => e.invoice_no, "UQ__invoices__F58CA1E2CD23F016").IsUnique();

            entity.HasIndex(e => new { e.agreement_id, e.billing_period }, "invoices_agreement_billing_period_uidx")
                .IsUnique()
                .HasFilter("([agreement_id] IS NOT NULL AND [billing_period] IS NOT NULL AND [status]<>'voided')");

            entity.HasIndex(e => e.agreement_id, "invoices_agreement_id_idx").HasFilter("([agreement_id] IS NOT NULL)");

            entity.HasIndex(e => new { e.customer_id, e.status, e.due_date }, "invoices_customer_status_due_idx");

            entity.HasIndex(e => e.due_date, "invoices_open_due_idx").HasFilter("([status] IN ('open', 'partially_paid', 'overdue'))");

            entity.HasIndex(e => e.reservation_id, "invoices_reservation_id_idx").HasFilter("([reservation_id] IS NOT NULL)");

            entity.Property(e => e.billing_period).HasMaxLength(64);
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.currency)
                .HasMaxLength(3)
                .IsUnicode(false)
                .HasDefaultValue("VND")
                .IsFixedLength();
            entity.Property(e => e.discount_amount).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.invoice_no).HasMaxLength(255);
            entity.Property(e => e.paid_amount).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("draft");
            entity.Property(e => e.subtotal_amount).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.tax_amount).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.total_amount).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.updated_at).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.agreement).WithMany(p => p.invoices)
                .HasForeignKey(d => d.agreement_id)
                .HasConstraintName("FK__invoices__agreem__4A18FC72");

            entity.HasOne(d => d.customer).WithMany(p => p.invoices)
                .HasForeignKey(d => d.customer_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__invoices__custom__4830B400");

            entity.HasOne(d => d.reservation).WithMany(p => p.invoices)
                .HasForeignKey(d => d.reservation_id)
                .HasConstraintName("FK__invoices__reserv__4924D839");

            entity.HasOne(d => d.ticket_charge_proposal).WithOne(p => p.invoice)
                .HasForeignKey<invoice>(d => d.ticket_charge_proposal_id)
                .HasConstraintName("FK__invoices__ticket__4B0D20AB");
        });

        modelBuilder.Entity<invoice_line>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__invoice___3213E83F70141318");

            entity.ToTable("invoice_lines", "core", tb =>
                {
                    tb.HasTrigger("trg_invoice_lines_guard");
                    tb.HasTrigger("trg_invoice_lines_refresh_totals");
                });

            entity.HasIndex(e => e.invoice_id, "invoice_lines_invoice_id_idx");

            entity.Property(e => e.description).HasMaxLength(255);
            entity.Property(e => e.line_amount)
                .HasComputedColumnSql("(CONVERT([numeric](14,2),round([quantity]*[unit_price],(2))))", true)
                .HasColumnType("numeric(14, 2)");
            entity.Property(e => e.line_type).HasMaxLength(255);
            entity.Property(e => e.metadata).HasDefaultValue("{}");
            entity.Property(e => e.quantity)
                .HasDefaultValue(1m)
                .HasColumnType("numeric(12, 3)");
            entity.Property(e => e.unit_price).HasColumnType("numeric(14, 2)");

            entity.HasOne(d => d.invoice).WithMany(p => p.invoice_lines)
                .HasForeignKey(d => d.invoice_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__invoice_l__invoi__60083D91");
        });

        modelBuilder.Entity<login_history>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__login_hi__3213E83FAD5B792B");

            entity.ToTable("login_history", "core");

            entity.HasIndex(e => new { e.attempted_email, e.occurred_at }, "login_history_email_occurred_idx")
                .IsDescending(false, true)
                .HasFilter("([attempted_email] IS NOT NULL)");

            entity.HasIndex(e => new { e.user_id, e.occurred_at }, "login_history_user_occurred_idx")
                .IsDescending(false, true)
                .HasFilter("([user_id] IS NOT NULL)");

            entity.Property(e => e.attempted_email).HasMaxLength(255);
            entity.Property(e => e.ip_address).HasMaxLength(45);
            entity.Property(e => e.occurred_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.result).HasMaxLength(255);
            entity.Property(e => e.user_agent).HasMaxLength(255);

            entity.HasOne(d => d.user).WithMany(p => p.login_histories)
                .HasForeignKey(d => d.user_id)
                .HasConstraintName("FK__login_his__user___7B7B4DDC");
        });

        modelBuilder.Entity<maintenance_work_order>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__maintena__3213E83FC343208F");

            entity.ToTable("maintenance_work_orders", "core", tb => tb.HasTrigger("trg_maintenance_work_orders_validate_scope"));

            entity.HasIndex(e => e.work_order_no, "UQ__maintena__58954FA629B442C1").IsUnique();

            entity.HasIndex(e => e.assigned_employee_id, "maintenance_work_orders_assigned_employee_id_idx").HasFilter("([assigned_employee_id] IS NOT NULL)");

            entity.HasIndex(e => new { e.facility_id, e.status, e.priority, e.created_at }, "maintenance_work_orders_facility_queue_idx");

            entity.HasIndex(e => e.opened_by, "maintenance_work_orders_opened_by_idx");

            entity.HasIndex(e => e.source_inspection_id, "maintenance_work_orders_source_inspection_id_idx").HasFilter("([source_inspection_id] IS NOT NULL)");

            entity.HasIndex(e => e.source_ticket_id, "maintenance_work_orders_source_ticket_id_idx").HasFilter("([source_ticket_id] IS NOT NULL)");

            entity.HasIndex(e => new { e.storage_unit_id, e.status }, "maintenance_work_orders_storage_unit_id_idx").HasFilter("([storage_unit_id] IS NOT NULL)");

            entity.HasIndex(e => e.verified_by, "maintenance_work_orders_verified_by_idx").HasFilter("([verified_by] IS NOT NULL)");

            entity.Property(e => e.actual_cost).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.blocks_booking).HasDefaultValue(true);
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.description).HasMaxLength(255);
            entity.Property(e => e.estimated_cost).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.priority)
                .HasMaxLength(255)
                .HasDefaultValue("normal");
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("open");
            entity.Property(e => e.title).HasMaxLength(255);
            entity.Property(e => e.updated_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.work_order_no).HasMaxLength(255);

            entity.HasOne(d => d.assigned_employee).WithMany(p => p.maintenance_work_orderassigned_employees)
                .HasForeignKey(d => d.assigned_employee_id)
                .HasConstraintName("FK__maintenan__assig__1FEDB87C");

            entity.HasOne(d => d.facility).WithMany(p => p.maintenance_work_orders)
                .HasForeignKey(d => d.facility_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__maintenan__facil__1C1D2798");

            entity.HasOne(d => d.opened_byNavigation).WithMany(p => p.maintenance_work_orders)
                .HasForeignKey(d => d.opened_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__maintenan__opene__278EDA44");

            entity.HasOne(d => d.source_inspection).WithMany(p => p.maintenance_work_orders)
                .HasForeignKey(d => d.source_inspection_id)
                .HasConstraintName("FK__maintenan__sourc__1EF99443");

            entity.HasOne(d => d.source_ticket).WithMany(p => p.maintenance_work_orders)
                .HasForeignKey(d => d.source_ticket_id)
                .HasConstraintName("FK__maintenan__sourc__1E05700A");

            entity.HasOne(d => d.storage_unit).WithMany(p => p.maintenance_work_orders)
                .HasForeignKey(d => d.storage_unit_id)
                .HasConstraintName("FK__maintenan__stora__1D114BD1");

            entity.HasOne(d => d.verified_byNavigation).WithMany(p => p.maintenance_work_orderverified_byNavigations)
                .HasForeignKey(d => d.verified_by)
                .HasConstraintName("FK__maintenan__verif__2882FE7D");
        });

        modelBuilder.Entity<move_out_request>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__move_out__3213E83F8091633E");

            entity.ToTable("move_out_requests", "core");

            entity.HasIndex(e => new { e.agreement_id, e.status, e.created_at }, "move_out_requests_agreement_idx").IsDescending(false, false, true);

            entity.HasIndex(e => e.appointment_id, "move_out_requests_appointment_id_idx").HasFilter("([appointment_id] IS NOT NULL)");

            entity.HasIndex(e => e.agreement_id, "move_out_requests_one_open_uidx")
                .IsUnique()
                .HasFilter("([status]<>'completed' AND [status]<>'cancelled')");

            entity.HasIndex(e => e.requested_by, "move_out_requests_requested_by_idx");

            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.reason).HasMaxLength(255);
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("requested");
            entity.Property(e => e.updated_at).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.agreement).WithOne(p => p.move_out_request)
                .HasForeignKey<move_out_request>(d => d.agreement_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__move_out___agree__6D9742D9");

            entity.HasOne(d => d.appointment).WithMany(p => p.move_out_requests)
                .HasForeignKey(d => d.appointment_id)
                .HasConstraintName("FK__move_out___appoi__6F7F8B4B");

            entity.HasOne(d => d.requested_byNavigation).WithMany(p => p.move_out_requests)
                .HasForeignKey(d => d.requested_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__move_out___reque__6E8B6712");
        });

        modelBuilder.Entity<notification>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__notifica__3213E83F41921952");

            entity.ToTable("notifications", "core");

            entity.HasIndex(e => e.deduplication_key, "UQ__notifica__336F0ABB33F7FE7C").IsUnique();

            entity.HasIndex(e => new { e.scheduled_at, e.id }, "notifications_delivery_queue_idx").HasFilter("([status] IN ('pending', 'failed'))");

            entity.HasIndex(e => new { e.user_id, e.created_at }, "notifications_user_created_idx").IsDescending(false, true);

            entity.Property(e => e.channel).HasMaxLength(255);
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.deduplication_key).HasMaxLength(255);
            entity.Property(e => e.last_error).HasMaxLength(255);
            entity.Property(e => e.payload).HasDefaultValue("{}");
            entity.Property(e => e.scheduled_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("pending");
            entity.Property(e => e.subject).HasMaxLength(255);
            entity.Property(e => e.template_code).HasMaxLength(255);

            entity.HasOne(d => d.user).WithMany(p => p.notifications)
                .HasForeignKey(d => d.user_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__notificat__user___6B44E613");
        });

        modelBuilder.Entity<payment>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__payments__3213E83F96A54490");

            entity.ToTable("payments", "core", tb =>
                {
                    tb.HasTrigger("trg_payments_refresh_invoices");
                    tb.HasTrigger("trg_payments_validate_target");
                });

            entity.HasIndex(e => e.idempotency_key, "UQ__payments__A7BA59F4D277C47A").IsUnique();

            entity.HasIndex(e => new { e.customer_id, e.created_at }, "payments_customer_created_idx").IsDescending(false, true);

            entity.HasIndex(e => new { e.provider, e.provider_transaction_id }, "payments_provider_transaction_uidx")
                .IsUnique()
                .HasFilter("([provider_transaction_id] IS NOT NULL)");

            entity.HasIndex(e => e.target_invoice_id, "payments_target_invoice_id_idx");

            entity.Property(e => e.amount).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.currency)
                .HasMaxLength(3)
                .IsUnicode(false)
                .HasDefaultValue("VND")
                .IsFixedLength();
            entity.Property(e => e.failure_reason).HasMaxLength(255);
            entity.Property(e => e.idempotency_key).HasMaxLength(255);
            entity.Property(e => e.metadata).HasDefaultValue("{}");
            entity.Property(e => e.method).HasMaxLength(255);
            entity.Property(e => e.provider).HasMaxLength(255);
            entity.Property(e => e.provider_transaction_id).HasMaxLength(255);
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("initiated");
            entity.Property(e => e.updated_at).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.customer).WithMany(p => p.payments)
                .HasForeignKey(d => d.customer_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__payments__custom__6991A7CB");

            entity.HasOne(d => d.target_invoice).WithMany(p => p.payments)
                .HasForeignKey(d => d.target_invoice_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__payments__target__6A85CC04");
        });

        modelBuilder.Entity<payment_allocation>(entity =>
        {
            entity.HasKey(e => new { e.payment_id, e.invoice_id }).HasName("PK__payment___6247163E3450A9EC");

            entity.ToTable("payment_allocations", "core", tb => tb.HasTrigger("trg_payment_allocation_guard"));

            entity.HasIndex(e => new { e.invoice_id, e.payment_id }, "payment_allocations_invoice_id_idx");

            entity.Property(e => e.allocated_amount).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.allocated_at).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.invoice).WithMany(p => p.payment_allocations)
                .HasForeignKey(d => d.invoice_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__payment_a__invoi__78D3EB5B");

            entity.HasOne(d => d.payment).WithMany(p => p.payment_allocations)
                .HasForeignKey(d => d.payment_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__payment_a__payme__77DFC722");
        });

        modelBuilder.Entity<policy_version>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__policy_v__3213E83FC0245A34");

            entity.ToTable("policy_versions", "core", tb => tb.HasTrigger("trg_policy_version_no_overlap"));

            entity.HasIndex(e => new { e.policy_type, e.version }, "UQ__policy_v__913F32FA36A702C9").IsUnique();

            entity.HasIndex(e => e.created_by, "policy_versions_created_by_idx").HasFilter("([created_by] IS NOT NULL)");

            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.policy_type).HasMaxLength(255);
            entity.Property(e => e.version).HasMaxLength(255);

            entity.HasOne(d => d.created_byNavigation).WithMany(p => p.policy_versions)
                .HasForeignKey(d => d.created_by)
                .HasConstraintName("FK__policy_ve__creat__44CA3770");
        });

        modelBuilder.Entity<price_range>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__price_ra__3213E83FFAF0D362");

            entity.ToTable("price_ranges", "core", tb => tb.HasTrigger("trg_price_range_no_overlap"));

            entity.HasIndex(e => e.created_by, "price_ranges_created_by_idx").HasFilter("([created_by] IS NOT NULL)");

            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.max_monthly_rate).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.min_monthly_rate).HasColumnType("numeric(14, 2)");

            entity.HasOne(d => d.created_byNavigation).WithMany(p => p.price_ranges)
                .HasForeignKey(d => d.created_by)
                .HasConstraintName("FK__price_ran__creat__31B762FC");

            entity.HasOne(d => d.unit_type).WithMany(p => p.price_ranges)
                .HasForeignKey(d => d.unit_type_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__price_ran__unit___2FCF1A8A");
        });

        modelBuilder.Entity<promotion>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__promotio__3213E83F64A0F2B9");

            entity.ToTable("promotions", "core");

            entity.HasIndex(e => e.code, "UQ__promotio__357D4CF9B500A1F8").IsUnique();

            entity.HasIndex(e => new { e.valid_from, e.valid_to }, "promotions_active_period_idx").HasFilter("([is_active]=(1))");

            entity.HasIndex(e => e.created_by, "promotions_created_by_idx").HasFilter("([created_by] IS NOT NULL)");

            entity.Property(e => e.code).HasMaxLength(255);
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.description).HasMaxLength(255);
            entity.Property(e => e.discount_type).HasMaxLength(255);
            entity.Property(e => e.discount_value).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.is_active).HasDefaultValue(true);
            entity.Property(e => e.max_discount_amount).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.name).HasMaxLength(255);

            entity.HasOne(d => d.created_byNavigation).WithMany(p => p.promotions)
                .HasForeignKey(d => d.created_by)
                .HasConstraintName("FK__promotion__creat__5F7E2DAC");
        });

        modelBuilder.Entity<promotion_redemption>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__promotio__3213E83FBE045461");

            entity.ToTable("promotion_redemptions", "core", tb => tb.HasTrigger("trg_promotion_redemptions_validate_scope"));

            entity.HasIndex(e => new { e.customer_id, e.promotion_id, e.status }, "promotion_redemptions_customer_idx");

            entity.HasIndex(e => e.invoice_id, "promotion_redemptions_invoice_id_idx").HasFilter("([invoice_id] IS NOT NULL)");

            entity.HasIndex(e => e.invoice_id, "promotion_redemptions_one_per_invoice_uidx")
                .IsUnique()
                .HasFilter("([invoice_id] IS NOT NULL AND [status]<>'released')");

            entity.HasIndex(e => e.reservation_id, "promotion_redemptions_one_per_reservation_uidx")
                .IsUnique()
                .HasFilter("([reservation_id] IS NOT NULL AND [status]<>'released')");

            entity.HasIndex(e => new { e.promotion_id, e.status, e.redeemed_at }, "promotion_redemptions_promotion_idx");

            entity.HasIndex(e => e.reservation_id, "promotion_redemptions_reservation_id_idx").HasFilter("([reservation_id] IS NOT NULL)");

            entity.HasIndex(e => new { e.promotion_id, e.reservation_id }, "promotion_redemptions_reservation_uidx")
                .IsUnique()
                .HasFilter("([reservation_id] IS NOT NULL AND [status]<>'released')");

            entity.Property(e => e.discount_amount).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.redeemed_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("reserved");

            entity.HasOne(d => d.customer).WithMany(p => p.promotion_redemptions)
                .HasForeignKey(d => d.customer_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__promotion__custo__119F9925");

            entity.HasOne(d => d.invoice).WithOne(p => p.promotion_redemption)
                .HasForeignKey<promotion_redemption>(d => d.invoice_id)
                .HasConstraintName("FK__promotion__invoi__1387E197");

            entity.HasOne(d => d.promotion).WithMany(p => p.promotion_redemptions)
                .HasForeignKey(d => d.promotion_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__promotion__promo__10AB74EC");

            entity.HasOne(d => d.reservation).WithOne(p => p.promotion_redemption)
                .HasForeignKey<promotion_redemption>(d => d.reservation_id)
                .HasConstraintName("FK__promotion__reser__1293BD5E");
        });

        modelBuilder.Entity<promotion_rule>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__promotio__3213E83F2E93C891");

            entity.ToTable("promotion_rules", "core");

            entity.HasIndex(e => new { e.promotion_id, e.rule_type }, "UQ__promotio__DE90F0E9B3F67BD9").IsUnique();

            entity.HasIndex(e => e.promotion_id, "promotion_rules_promotion_id_idx");

            entity.Property(e => e._operator)
                .HasMaxLength(255)
                .HasDefaultValue("eq")
                .HasColumnName("operator");
            entity.Property(e => e.rule_type).HasMaxLength(255);

            entity.HasOne(d => d.promotion).WithMany(p => p.promotion_rules)
                .HasForeignKey(d => d.promotion_id)
                .HasConstraintName("FK__promotion__promo__662B2B3B");
        });

        modelBuilder.Entity<refund>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__refunds__3213E83FDD7E7419");

            entity.ToTable("refunds", "core", tb => tb.HasTrigger("trg_refunds_guard"));

            entity.HasIndex(e => e.idempotency_key, "UQ__refunds__A7BA59F4553E6B12").IsUnique();

            entity.HasIndex(e => e.agreement_id, "refunds_agreement_id_idx").HasFilter("([agreement_id] IS NOT NULL)");

            entity.HasIndex(e => e.payment_id, "refunds_payment_id_idx");

            entity.HasIndex(e => new { e.provider, e.provider_refund_id }, "refunds_provider_refund_uidx")
                .IsUnique()
                .HasFilter("([provider_refund_id] IS NOT NULL)");

            entity.HasIndex(e => e.requested_by, "refunds_requested_by_idx");

            entity.Property(e => e.amount).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.currency)
                .HasMaxLength(3)
                .IsUnicode(false)
                .HasDefaultValue("VND")
                .IsFixedLength();
            entity.Property(e => e.idempotency_key).HasMaxLength(255);
            entity.Property(e => e.provider).HasMaxLength(255);
            entity.Property(e => e.provider_refund_id).HasMaxLength(255);
            entity.Property(e => e.reason).HasMaxLength(255);
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("requested");
            entity.Property(e => e.updated_at).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.agreement).WithMany(p => p.refunds)
                .HasForeignKey(d => d.agreement_id)
                .HasConstraintName("FK__refunds__agreeme__7F80E8EA");

            entity.HasOne(d => d.payment).WithMany(p => p.refunds)
                .HasForeignKey(d => d.payment_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__refunds__payment__7E8CC4B1");

            entity.HasOne(d => d.requested_byNavigation).WithMany(p => p.refunds)
                .HasForeignKey(d => d.requested_by)
                .HasConstraintName("FK__refunds__request__04459E07");
        });

        modelBuilder.Entity<refund_approval>(entity =>
        {
            entity.HasKey(e => e.refund_id).HasName("PK__refund_a__897E9EA3E4AED43D");

            entity.ToTable("refund_approvals", "core");

            entity.HasIndex(e => e.decided_by, "refund_approvals_decided_by_idx");

            entity.Property(e => e.refund_id).ValueGeneratedNever();
            entity.Property(e => e.decided_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.decision).HasMaxLength(255);
            entity.Property(e => e.reason).HasMaxLength(255);

            entity.HasOne(d => d.decided_byNavigation).WithMany(p => p.refund_approvals)
                .HasForeignKey(d => d.decided_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_refund_approvals_decided_by_users");

            entity.HasOne(d => d.refund).WithOne(p => p.refund_approval)
                .HasForeignKey<refund_approval>(d => d.refund_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__refund_ap__refun__0AF29B96");
        });

        modelBuilder.Entity<rental_agreement>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__rental_a__3213E83F70764CFE");

            entity.ToTable("rental_agreements", "core", tb =>
                {
                    tb.HasTrigger("trg_agreement_status_transition");
                    tb.HasTrigger("trg_rental_agreements_validate_reservation");
                });

            entity.HasIndex(e => e.reservation_id, "UQ__rental_a__31384C284D6ED3A8").IsUnique();

            entity.HasIndex(e => e.agreement_no, "UQ__rental_a__A476927E924D2587").IsUnique();

            entity.HasIndex(e => new { e.id, e.customer_id, e.facility_id }, "UQ__rental_a__D1775C6C57E86D0E").IsUnique();

            entity.HasIndex(e => new { e.customer_id, e.status, e.end_date }, "rental_agreements_customer_status_idx");

            entity.HasIndex(e => new { e.facility_id, e.status, e.end_date }, "rental_agreements_facility_status_idx");

            entity.HasIndex(e => e.policy_version_id, "rental_agreements_policy_version_id_idx");

            entity.Property(e => e.agreement_no).HasMaxLength(255);
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.deposit_balance).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.deposit_snapshot).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.monthly_rate_snapshot).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("draft");
            entity.Property(e => e.updated_at).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.customer).WithMany(p => p.rental_agreements)
                .HasForeignKey(d => d.customer_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__rental_ag__custo__0697FACD");

            entity.HasOne(d => d.facility).WithMany(p => p.rental_agreements)
                .HasForeignKey(d => d.facility_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__rental_ag__facil__078C1F06");

            entity.HasOne(d => d.policy_version).WithMany(p => p.rental_agreements)
                .HasForeignKey(d => d.policy_version_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__rental_ag__polic__0880433F");

            entity.HasOne(d => d.reservation).WithOne(p => p.rental_agreement)
                .HasForeignKey<rental_agreement>(d => d.reservation_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__rental_ag__reser__05A3D694");
        });

        modelBuilder.Entity<rental_renewal>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__rental_r__3213E83F1D86883A");

            entity.ToTable("rental_renewals", "core");

            entity.HasIndex(e => new { e.agreement_id, e.created_at }, "rental_renewals_agreement_idx").IsDescending(false, true);

            entity.HasIndex(e => e.agreement_id, "rental_renewals_one_open_uidx")
                .IsUnique()
                .HasFilter("([status] IN ('pending_payment', 'paid'))");

            entity.HasIndex(e => e.requested_by, "rental_renewals_requested_by_idx");

            entity.HasIndex(e => e.reviewed_by, "rental_renewals_reviewed_by_idx").HasFilter("([reviewed_by] IS NOT NULL)");

            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.new_monthly_rate).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.old_monthly_rate).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("pending_payment");

            entity.HasOne(d => d.agreement).WithOne(p => p.rental_renewal)
                .HasForeignKey<rental_renewal>(d => d.agreement_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__rental_re__agree__57A801BA");

            entity.HasOne(d => d.requested_byNavigation).WithMany(p => p.rental_renewalrequested_byNavigations)
                .HasForeignKey(d => d.requested_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__rental_re__reque__589C25F3");

            entity.HasOne(d => d.reviewed_byNavigation).WithMany(p => p.rental_renewalreviewed_byNavigations)
                .HasForeignKey(d => d.reviewed_by)
                .HasConstraintName("FK__rental_re__revie__5D60DB10");
        });

        modelBuilder.Entity<reservation>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__reservat__3213E83F409812AA");

            entity.ToTable("reservations", "core", tb =>
                {
                    tb.HasTrigger("trg_reservation_status_transition");
                    tb.HasTrigger("trg_reservations_validate_rate");
                });

            entity.HasIndex(e => new { e.id, e.customer_id, e.facility_id, e.unit_type_id }, "UQ__reservat__6DF00CF33AB16E48").IsUnique();

            entity.HasIndex(e => e.reservation_code, "UQ__reservat__FA8FADE493286D56").IsUnique();

            entity.HasIndex(e => new { e.customer_id, e.created_at }, "reservations_customer_created_idx").IsDescending(false, true);

            entity.HasIndex(e => e.facility_rate_id, "reservations_facility_rate_id_idx");

            entity.HasIndex(e => new { e.facility_id, e.status, e.start_date }, "reservations_facility_status_start_idx");

            entity.HasIndex(e => e.hold_until, "reservations_open_hold_idx").HasFilter("([status] IN ('pending', 'awaiting_deposit'))");

            entity.HasIndex(e => e.unit_type_id, "reservations_unit_type_id_idx");

            entity.Property(e => e.booking_fee_snapshot).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.cancellation_reason).HasMaxLength(255);
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.deposit_snapshot).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.discount_snapshot).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.monthly_rate_snapshot).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.quoted_total).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.reservation_code).HasMaxLength(255);
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("pending");
            entity.Property(e => e.updated_at).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.customer).WithMany(p => p.reservations)
                .HasForeignKey(d => d.customer_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__reservati__custo__6DCC4D03");

            entity.HasOne(d => d.facility).WithMany(p => p.reservations)
                .HasForeignKey(d => d.facility_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__reservati__facil__6EC0713C");

            entity.HasOne(d => d.facility_rate).WithMany(p => p.reservations)
                .HasForeignKey(d => d.facility_rate_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__reservati__facil__70A8B9AE");

            entity.HasOne(d => d.unit_type).WithMany(p => p.reservations)
                .HasForeignKey(d => d.unit_type_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__reservati__unit___6FB49575");
        });

        modelBuilder.Entity<role>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__roles__3213E83F55D95144");

            entity.ToTable("roles", "core");

            entity.HasIndex(e => e.code, "UQ__roles__357D4CF9879FF776").IsUnique();

            entity.Property(e => e.code).HasMaxLength(255);
            entity.Property(e => e.description).HasMaxLength(255);
            entity.Property(e => e.display_name).HasMaxLength(255);
        });

        modelBuilder.Entity<service_rating>(entity =>
        {
            entity.HasKey(e => e.ticket_id).HasName("PK__service___D596F96B967A4758");

            entity.ToTable("service_ratings", "core", tb => tb.HasTrigger("trg_service_ratings_validate_scope"));

            entity.HasIndex(e => e.customer_id, "service_ratings_customer_id_idx");

            entity.Property(e => e.ticket_id).ValueGeneratedNever();
            entity.Property(e => e.comment).HasMaxLength(255);
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.customer).WithMany(p => p.service_ratings)
                .HasForeignKey(d => d.customer_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__service_r__custo__4183B671");

            entity.HasOne(d => d.ticket).WithOne(p => p.service_rating)
                .HasForeignKey<service_rating>(d => d.ticket_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__service_r__ticke__408F9238");
        });

        modelBuilder.Entity<shift_assignment>(entity =>
        {
            entity.HasKey(e => new { e.shift_id, e.employee_id }).HasName("PK__shift_as__E774929AA1423AAC");

            entity.ToTable("shift_assignments", "core", tb => tb.HasTrigger("trg_shift_assignments_validate_scope"));

            entity.HasIndex(e => new { e.employee_id, e.shift_id }, "shift_assignments_employee_id_idx");

            entity.Property(e => e.duty_role).HasMaxLength(255);
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("scheduled");

            entity.HasOne(d => d.employee).WithMany(p => p.shift_assignments)
                .HasForeignKey(d => d.employee_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__shift_ass__emplo__7FB5F314");

            entity.HasOne(d => d.shift).WithMany(p => p.shift_assignments)
                .HasForeignKey(d => d.shift_id)
                .HasConstraintName("FK__shift_ass__shift__7EC1CEDB");
        });

        modelBuilder.Entity<staff_facility_assignment>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__staff_fa__3213E83F5FAC3A27");

            entity.ToTable("staff_facility_assignments", "core", tb => tb.HasTrigger("trg_staff_facility_assignment_no_overlap"));

            entity.HasIndex(e => e.assigned_by, "staff_facility_assignments_assigned_by_idx").HasFilter("([assigned_by] IS NOT NULL)");

            entity.HasIndex(e => new { e.facility_id, e.assignment_role, e.starts_at, e.ends_at }, "staff_facility_assignments_facility_idx");

            entity.Property(e => e.assignment_role).HasMaxLength(255);
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.assigned_byNavigation).WithMany(p => p.staff_facility_assignments)
                .HasForeignKey(d => d.assigned_by)
                .HasConstraintName("FK__staff_fac__assig__7A672E12");

            entity.HasOne(d => d.employee).WithMany(p => p.staff_facility_assignments)
                .HasForeignKey(d => d.employee_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__staff_fac__emplo__778AC167");

            entity.HasOne(d => d.facility).WithMany(p => p.staff_facility_assignments)
                .HasForeignKey(d => d.facility_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__staff_fac__facil__787EE5A0");
        });

        modelBuilder.Entity<staff_shift>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__staff_sh__3213E83FAD859C60");

            entity.ToTable("staff_shifts", "core");

            entity.HasIndex(e => e.created_by, "staff_shifts_created_by_idx");

            entity.HasIndex(e => new { e.facility_id, e.starts_at, e.ends_at, e.status }, "staff_shifts_facility_schedule_idx");

            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.shift_name).HasMaxLength(255);
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("planned");

            entity.HasOne(d => d.created_byNavigation).WithMany(p => p.staff_shifts)
                .HasForeignKey(d => d.created_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__staff_shi__creat__79FD19BE");

            entity.HasOne(d => d.facility).WithMany(p => p.staff_shifts)
                .HasForeignKey(d => d.facility_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__staff_shi__facil__7720AD13");
        });

        modelBuilder.Entity<staff_task>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__staff_ta__3213E83FBA2EEF46");

            entity.ToTable("staff_tasks", "core", tb => tb.HasTrigger("trg_staff_tasks_validate_scope"));

            entity.HasIndex(e => new { e.assigned_employee_id, e.status, e.due_at }, "staff_tasks_assigned_employee_id_idx").HasFilter("([assigned_employee_id] IS NOT NULL)");

            entity.HasIndex(e => e.created_by, "staff_tasks_created_by_idx");

            entity.HasIndex(e => new { e.facility_id, e.status, e.due_at }, "staff_tasks_facility_queue_idx");

            entity.HasIndex(e => e.maintenance_work_order_id, "staff_tasks_maintenance_id_idx").HasFilter("([maintenance_work_order_id] IS NOT NULL)");

            entity.HasIndex(e => e.shift_id, "staff_tasks_shift_id_idx").HasFilter("([shift_id] IS NOT NULL)");

            entity.HasIndex(e => e.ticket_id, "staff_tasks_ticket_id_idx").HasFilter("([ticket_id] IS NOT NULL)");

            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("todo");
            entity.Property(e => e.task_type).HasMaxLength(255);
            entity.Property(e => e.title).HasMaxLength(255);
            entity.Property(e => e.updated_at).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.assigned_employee).WithMany(p => p.staff_tasks)
                .HasForeignKey(d => d.assigned_employee_id)
                .HasConstraintName("FK__staff_tas__assig__320C68B7");

            entity.HasOne(d => d.created_byNavigation).WithMany(p => p.staff_tasks)
                .HasForeignKey(d => d.created_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__staff_tas__creat__39AD8A7F");

            entity.HasOne(d => d.facility).WithMany(p => p.staff_tasks)
                .HasForeignKey(d => d.facility_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__staff_tas__facil__30242045");

            entity.HasOne(d => d.maintenance_work_order).WithMany(p => p.staff_tasks)
                .HasForeignKey(d => d.maintenance_work_order_id)
                .HasConstraintName("FK__staff_tas__maint__33F4B129");

            entity.HasOne(d => d.shift).WithMany(p => p.staff_tasks)
                .HasForeignKey(d => d.shift_id)
                .HasConstraintName("FK__staff_tas__shift__3118447E");

            entity.HasOne(d => d.ticket).WithMany(p => p.staff_tasks)
                .HasForeignKey(d => d.ticket_id)
                .HasConstraintName("FK__staff_tas__ticke__33008CF0");
        });

        modelBuilder.Entity<storage_unit>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__storage___3213E83FE0A5AB7C");

            entity.ToTable("storage_units", "core", tb =>
                {
                    tb.HasTrigger("trg_storage_unit_status_transition");
                    tb.HasTrigger("trg_storage_units_validate_area");
                });

            entity.HasIndex(e => new { e.id, e.facility_id, e.unit_type_id }, "UQ__storage___01486F6F13C6F72C").IsUnique();

            entity.HasIndex(e => new { e.facility_id, e.unit_code }, "UQ__storage___17930DB617838055").IsUnique();

            entity.HasIndex(e => new { e.id, e.facility_id }, "UQ__storage___C93D6694DDA638F1").IsUnique();

            entity.HasIndex(e => e.area_id, "storage_units_area_id_idx").HasFilter("([area_id] IS NOT NULL)");

            entity.HasIndex(e => new { e.facility_id, e.unit_type_id, e.physical_status }, "storage_units_catalog_idx").HasFilter("([is_listed]=(1))");

            entity.HasIndex(e => e.unit_type_id, "storage_units_unit_type_id_idx");

            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.floor_label).HasMaxLength(255);
            entity.Property(e => e.is_listed).HasDefaultValue(true);
            entity.Property(e => e.notes).HasMaxLength(255);
            entity.Property(e => e.physical_status)
                .HasMaxLength(255)
                .HasDefaultValue("available");
            entity.Property(e => e.unit_code).HasMaxLength(255);
            entity.Property(e => e.updated_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.zone_label).HasMaxLength(255);

            entity.HasOne(d => d.area).WithMany(p => p.storage_units)
                .HasForeignKey(d => d.area_id)
                .HasConstraintName("FK__storage_u__area___1AD3FDA4");

            entity.HasOne(d => d.facility).WithMany(p => p.storage_units)
                .HasForeignKey(d => d.facility_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__storage_u__facil__18EBB532");

            entity.HasOne(d => d.unit_type).WithMany(p => p.storage_units)
                .HasForeignKey(d => d.unit_type_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__storage_u__unit___19DFD96B");
        });

        modelBuilder.Entity<support_ticket>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__support___3213E83F380DAADD");

            entity.ToTable("support_tickets", "core", tb => tb.HasTrigger("trg_support_tickets_validate_scope"));

            entity.HasIndex(e => e.ticket_no, "UQ__support___D596C19625D56D90").IsUnique();

            entity.HasIndex(e => e.agreement_id, "support_tickets_agreement_id_idx").HasFilter("([agreement_id] IS NOT NULL)");

            entity.HasIndex(e => new { e.customer_id, e.created_at }, "support_tickets_customer_created_idx").IsDescending(false, true);

            entity.HasIndex(e => new { e.facility_id, e.status, e.priority, e.created_at }, "support_tickets_facility_queue_idx");

            entity.HasIndex(e => e.storage_unit_id, "support_tickets_storage_unit_id_idx").HasFilter("([storage_unit_id] IS NOT NULL)");

            entity.Property(e => e.category).HasMaxLength(255);
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.description).HasMaxLength(255);
            entity.Property(e => e.priority)
                .HasMaxLength(255)
                .HasDefaultValue("normal");
            entity.Property(e => e.resolution).HasMaxLength(255);
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("open");
            entity.Property(e => e.subject).HasMaxLength(255);
            entity.Property(e => e.ticket_no).HasMaxLength(255);
            entity.Property(e => e.updated_at).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.agreement).WithMany(p => p.support_ticketagreements)
                .HasForeignKey(d => d.agreement_id)
                .HasConstraintName("FK__support_t__agree__0A338187");

            entity.HasOne(d => d.customer).WithMany(p => p.support_tickets)
                .HasForeignKey(d => d.customer_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__support_t__custo__084B3915");

            entity.HasOne(d => d.facility).WithMany(p => p.support_tickets)
                .HasForeignKey(d => d.facility_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__support_t__facil__093F5D4E");

            entity.HasOne(d => d.storage_unit).WithMany(p => p.support_ticketstorage_units)
                .HasForeignKey(d => d.storage_unit_id)
                .HasConstraintName("FK__support_t__stora__0B27A5C0");

            entity.HasOne(d => d.storage_unitNavigation).WithMany(p => p.support_ticketstorage_unitNavigations)
                .HasPrincipalKey(p => new { p.id, p.facility_id })
                .HasForeignKey(d => new { d.storage_unit_id, d.facility_id })
                .HasConstraintName("FK__support_tickets__13BCEBC1");

            entity.HasOne(d => d.rental_agreement).WithMany(p => p.support_ticketrental_agreements)
                .HasPrincipalKey(p => new { p.id, p.customer_id, p.facility_id })
                .HasForeignKey(d => new { d.agreement_id, d.customer_id, d.facility_id })
                .HasConstraintName("FK__support_tickets__12C8C788");
        });

        modelBuilder.Entity<ticket_assignment>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__ticket_a__3213E83FC037ACA4");

            entity.ToTable("ticket_assignments", "core", tb => tb.HasTrigger("trg_ticket_assignments_validate_scope"));

            entity.HasIndex(e => e.assigned_by, "ticket_assignments_assigned_by_idx").HasFilter("([assigned_by] IS NOT NULL)");

            entity.HasIndex(e => new { e.employee_id, e.ended_at }, "ticket_assignments_employee_id_idx");

            entity.HasIndex(e => e.ticket_id, "ticket_assignments_one_active_uidx")
                .IsUnique()
                .HasFilter("([ended_at] IS NULL)");

            entity.HasIndex(e => new { e.ticket_id, e.assigned_at }, "ticket_assignments_ticket_history_idx").IsDescending(false, true);

            entity.Property(e => e.assigned_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.end_reason).HasMaxLength(255);

            entity.HasOne(d => d.assigned_byNavigation).WithMany(p => p.ticket_assignments)
                .HasForeignKey(d => d.assigned_by)
                .HasConstraintName("FK__ticket_as__assig__1B5E0D89");

            entity.HasOne(d => d.employee).WithMany(p => p.ticket_assignments)
                .HasForeignKey(d => d.employee_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ticket_as__emplo__1A69E950");

            entity.HasOne(d => d.ticket).WithOne(p => p.ticket_assignment)
                .HasForeignKey<ticket_assignment>(d => d.ticket_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ticket_as__ticke__1975C517");
        });

        modelBuilder.Entity<ticket_attachment>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__ticket_a__3213E83F4C61A4E3");

            entity.ToTable("ticket_attachments", "core");

            entity.HasIndex(e => e.message_id, "ticket_attachments_message_id_idx").HasFilter("([message_id] IS NOT NULL)");

            entity.HasIndex(e => new { e.ticket_id, e.created_at }, "ticket_attachments_ticket_id_idx");

            entity.HasIndex(e => e.uploaded_by, "ticket_attachments_uploaded_by_idx");

            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.file_name).HasMaxLength(255);
            entity.Property(e => e.mime_type).HasMaxLength(255);
            entity.Property(e => e.object_url).HasMaxLength(255);
            entity.Property(e => e.sha256).HasMaxLength(255);

            entity.HasOne(d => d.message).WithMany(p => p.ticket_attachmentmessages)
                .HasForeignKey(d => d.message_id)
                .HasConstraintName("FK__ticket_at__messa__28B808A7");

            entity.HasOne(d => d.ticket).WithMany(p => p.ticket_attachments)
                .HasForeignKey(d => d.ticket_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ticket_at__ticke__27C3E46E");

            entity.HasOne(d => d.uploaded_byNavigation).WithMany(p => p.ticket_attachments)
                .HasForeignKey(d => d.uploaded_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ticket_at__uploa__29AC2CE0");

            entity.HasOne(d => d.ticket_message).WithMany(p => p.ticket_attachmentticket_messages)
                .HasPrincipalKey(p => new { p.id, p.ticket_id })
                .HasForeignKey(d => new { d.message_id, d.ticket_id })
                .HasConstraintName("FK__ticket_attachmen__2C88998B");
        });

        modelBuilder.Entity<ticket_charge_approval>(entity =>
        {
            entity.HasKey(e => e.proposal_id).HasName("PK__ticket_c__A7BC641C6BE9156E");

            entity.ToTable("ticket_charge_approvals", "core");

            entity.HasIndex(e => e.decided_by, "ticket_charge_approvals_decided_by_idx");

            entity.Property(e => e.proposal_id).ValueGeneratedNever();
            entity.Property(e => e.decided_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.decision).HasMaxLength(255);
            entity.Property(e => e.reason).HasMaxLength(255);

            entity.HasOne(d => d.decided_byNavigation).WithMany(p => p.ticket_charge_approvals)
                .HasForeignKey(d => d.decided_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ticket_ch__decid__3CBF0154");

            entity.HasOne(d => d.proposal).WithOne(p => p.ticket_charge_approval)
                .HasForeignKey<ticket_charge_approval>(d => d.proposal_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ticket_ch__propo__3AD6B8E2");
        });

        modelBuilder.Entity<ticket_charge_proposal>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__ticket_c__3213E83F745696E8");

            entity.ToTable("ticket_charge_proposals", "core");

            entity.HasIndex(e => e.proposed_by, "ticket_charge_proposals_proposed_by_idx");

            entity.HasIndex(e => new { e.ticket_id, e.status, e.created_at }, "ticket_charge_proposals_ticket_idx").IsDescending(false, false, true);

            entity.Property(e => e.amount).HasColumnType("numeric(14, 2)");
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.description).HasMaxLength(255);
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("pending");
            entity.Property(e => e.updated_at).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.proposed_byNavigation).WithMany(p => p.ticket_charge_proposals)
                .HasForeignKey(d => d.proposed_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ticket_ch__propo__3335971A");

            entity.HasOne(d => d.ticket).WithMany(p => p.ticket_charge_proposals)
                .HasForeignKey(d => d.ticket_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ticket_ch__ticke__324172E1");
        });

        modelBuilder.Entity<ticket_message>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__ticket_m__3213E83F61595A8A");

            entity.ToTable("ticket_messages", "core");

            entity.HasIndex(e => new { e.id, e.ticket_id }, "UQ__ticket_m__9F4A87A8C2D2BAFE").IsUnique();

            entity.HasIndex(e => e.author_user_id, "ticket_messages_author_user_id_idx").HasFilter("([author_user_id] IS NOT NULL)");

            entity.HasIndex(e => new { e.ticket_id, e.created_at }, "ticket_messages_ticket_created_idx");

            entity.Property(e => e.body).HasMaxLength(255);
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.author_user).WithMany(p => p.ticket_messages)
                .HasForeignKey(d => d.author_user_id)
                .HasConstraintName("FK__ticket_me__autho__220B0B18");

            entity.HasOne(d => d.ticket).WithMany(p => p.ticket_messages)
                .HasForeignKey(d => d.ticket_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ticket_me__ticke__2116E6DF");
        });

        modelBuilder.Entity<unit_allocation>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__unit_all__3213E83F85AE932A");

            entity.ToTable("unit_allocations", "core", tb =>
                {
                    tb.HasTrigger("trg_unit_allocation_no_overlap");
                    tb.HasTrigger("trg_unit_allocations_validate_scope");
                });

            entity.HasIndex(e => new { e.agreement_id, e.allocation_start_date, e.allocation_end_date }, "unit_allocations_agreement_period_idx").HasFilter("([agreement_id] IS NOT NULL)");

            entity.HasIndex(e => e.assigned_by, "unit_allocations_assigned_by_idx").HasFilter("([assigned_by] IS NOT NULL)");

            entity.HasIndex(e => e.reservation_id, "unit_allocations_one_active_reservation_uidx")
                .IsUnique()
                .HasFilter("([status]='active' AND [reservation_id] IS NOT NULL)");

            entity.HasIndex(e => new { e.storage_unit_id, e.allocation_start_date, e.allocation_end_date }, "unit_allocations_unit_period_idx");

            entity.Property(e => e.allocation_kind).HasMaxLength(255);
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.reason).HasMaxLength(255);
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("active");

            entity.HasOne(d => d.agreement).WithMany(p => p.unit_allocations)
                .HasForeignKey(d => d.agreement_id)
                .HasConstraintName("FK__unit_allo__agree__16CE6296");

            entity.HasOne(d => d.assigned_byNavigation).WithMany(p => p.unit_allocations)
                .HasForeignKey(d => d.assigned_by)
                .HasConstraintName("FK__unit_allo__assig__1A9EF37A");

            entity.HasOne(d => d.reservation).WithOne(p => p.unit_allocation)
                .HasForeignKey<unit_allocation>(d => d.reservation_id)
                .HasConstraintName("FK__unit_allo__reser__15DA3E5D");

            entity.HasOne(d => d.storage_unit).WithMany(p => p.unit_allocations)
                .HasForeignKey(d => d.storage_unit_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__unit_allo__stora__14E61A24");
        });

        modelBuilder.Entity<unit_map_position>(entity =>
        {
            entity.HasKey(e => e.unit_id).HasName("PK__unit_map__D3AF5BD781724FD7");

            entity.ToTable("unit_map_positions", "core", tb => tb.HasTrigger("trg_unit_map_positions_validate_scope"));

            entity.HasIndex(e => e.area_id, "unit_map_positions_area_id_idx");

            entity.Property(e => e.unit_id).ValueGeneratedNever();
            entity.Property(e => e.height).HasColumnType("numeric(10, 2)");
            entity.Property(e => e.metadata).HasDefaultValue("{}");
            entity.Property(e => e.rotation_degrees).HasColumnType("numeric(6, 2)");
            entity.Property(e => e.width).HasColumnType("numeric(10, 2)");
            entity.Property(e => e.x).HasColumnType("numeric(10, 2)");
            entity.Property(e => e.y).HasColumnType("numeric(10, 2)");

            entity.HasOne(d => d.area).WithMany(p => p.unit_map_positions)
                .HasForeignKey(d => d.area_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__unit_map___area___236943A5");

            entity.HasOne(d => d.unit).WithOne(p => p.unit_map_position)
                .HasForeignKey<unit_map_position>(d => d.unit_id)
                .HasConstraintName("FK__unit_map___unit___22751F6C");
        });

        modelBuilder.Entity<unit_status_history>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__unit_sta__3213E83F89D3D707");

            entity.ToTable("unit_status_history", "core");

            entity.HasIndex(e => e.changed_by, "unit_status_history_changed_by_idx").HasFilter("([changed_by] IS NOT NULL)");

            entity.HasIndex(e => new { e.storage_unit_id, e.changed_at }, "unit_status_history_unit_changed_idx").IsDescending(false, true);

            entity.Property(e => e.changed_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.new_status).HasMaxLength(255);
            entity.Property(e => e.old_status).HasMaxLength(255);
            entity.Property(e => e.reason).HasMaxLength(255);

            entity.HasOne(d => d.changed_byNavigation).WithMany(p => p.unit_status_histories)
                .HasForeignKey(d => d.changed_by)
                .HasConstraintName("FK__unit_stat__chang__2BFE89A6");

            entity.HasOne(d => d.storage_unit).WithMany(p => p.unit_status_histories)
                .HasForeignKey(d => d.storage_unit_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__unit_stat__stora__2B0A656D");
        });

        modelBuilder.Entity<unit_transfer_request>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__unit_tra__3213E83F6C13DFA9");

            entity.ToTable("unit_transfer_requests", "core");

            entity.HasIndex(e => new { e.agreement_id, e.status, e.created_at }, "unit_transfer_requests_agreement_idx").IsDescending(false, false, true);

            entity.HasIndex(e => e.from_unit_id, "unit_transfer_requests_from_unit_id_idx");

            entity.HasIndex(e => e.agreement_id, "unit_transfer_requests_one_open_uidx")
                .IsUnique()
                .HasFilter("([status] IN ('pending', 'approved', 'scheduled'))");

            entity.HasIndex(e => e.requested_by, "unit_transfer_requests_requested_by_idx");

            entity.HasIndex(e => e.requested_unit_type_id, "unit_transfer_requests_requested_unit_type_id_idx");

            entity.HasIndex(e => e.reviewed_by, "unit_transfer_requests_reviewed_by_idx").HasFilter("([reviewed_by] IS NOT NULL)");

            entity.HasIndex(e => e.to_unit_id, "unit_transfer_requests_to_unit_id_idx").HasFilter("([to_unit_id] IS NOT NULL)");

            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.reason).HasMaxLength(255);
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("pending");

            entity.HasOne(d => d.agreement).WithOne(p => p.unit_transfer_request)
                .HasForeignKey<unit_transfer_request>(d => d.agreement_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__unit_tran__agree__6319B466");

            entity.HasOne(d => d.from_unit).WithMany(p => p.unit_transfer_requestfrom_units)
                .HasForeignKey(d => d.from_unit_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__unit_tran__from___6501FCD8");

            entity.HasOne(d => d.requested_byNavigation).WithMany(p => p.unit_transfer_requestrequested_byNavigations)
                .HasForeignKey(d => d.requested_by)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__unit_tran__reque__68D28DBC");

            entity.HasOne(d => d.requested_unit_type).WithMany(p => p.unit_transfer_requests)
                .HasForeignKey(d => d.requested_unit_type_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__unit_tran__reque__640DD89F");

            entity.HasOne(d => d.reviewed_byNavigation).WithMany(p => p.unit_transfer_requestreviewed_byNavigations)
                .HasForeignKey(d => d.reviewed_by)
                .HasConstraintName("FK__unit_tran__revie__69C6B1F5");

            entity.HasOne(d => d.to_unit).WithMany(p => p.unit_transfer_requestto_units)
                .HasForeignKey(d => d.to_unit_id)
                .HasConstraintName("FK__unit_tran__to_un__65F62111");
        });

        modelBuilder.Entity<unit_type>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__unit_typ__3213E83F5F08DF15");

            entity.ToTable("unit_types", "core");

            entity.HasIndex(e => e.code, "UQ__unit_typ__357D4CF9099C73CE").IsUnique();

            entity.Property(e => e.area_m2)
                .HasComputedColumnSql("(CONVERT([numeric](10,2),[width_m]*[length_m]))", true)
                .HasColumnType("numeric(10, 2)");
            entity.Property(e => e.code).HasMaxLength(255);
            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.description).HasMaxLength(255);
            entity.Property(e => e.height_m).HasColumnType("numeric(8, 2)");
            entity.Property(e => e.is_active).HasDefaultValue(true);
            entity.Property(e => e.length_m).HasColumnType("numeric(8, 2)");
            entity.Property(e => e.max_weight_kg).HasColumnType("numeric(12, 2)");
            entity.Property(e => e.name).HasMaxLength(255);
            entity.Property(e => e.updated_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.volume_m3)
                .HasComputedColumnSql("(CONVERT([numeric](12,2),([width_m]*[length_m])*[height_m]))", true)
                .HasColumnType("numeric(12, 2)");
            entity.Property(e => e.width_m).HasColumnType("numeric(8, 2)");
        });

        modelBuilder.Entity<user>(entity =>
        {
            entity.HasKey(e => e.id).HasName("PK__users__3213E83FBA1F0084");

            entity.ToTable("users", "core");

            entity.HasIndex(e => e.email, "users_email_unique").IsUnique();

            entity.Property(e => e.created_at).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.email).HasMaxLength(255);
            entity.Property(e => e.password_hash).HasMaxLength(255);
            entity.Property(e => e.phone_number).HasMaxLength(255);
            entity.Property(e => e.status)
                .HasMaxLength(255)
                .HasDefaultValue("active");
            entity.Property(e => e.updated_at).HasDefaultValueSql("(sysutcdatetime())");
        });

        modelBuilder.Entity<user_role>(entity =>
        {
            entity.HasKey(e => new { e.user_id, e.role_id }).HasName("PK__user_rol__6EDEA153BF3BD8B9");

            entity.ToTable("user_roles", "core");

            entity.HasIndex(e => e.granted_by, "user_roles_granted_by_idx").HasFilter("([granted_by] IS NOT NULL)");

            entity.HasIndex(e => new { e.role_id, e.user_id }, "user_roles_role_id_idx");

            entity.Property(e => e.granted_at).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.granted_byNavigation).WithMany(p => p.user_rolegranted_byNavigations)
                .HasForeignKey(d => d.granted_by)
                .HasConstraintName("FK__user_role__grant__5CD6CB2B");

            entity.HasOne(d => d.role).WithMany(p => p.user_roles)
                .HasForeignKey(d => d.role_id)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__user_role__role___5BE2A6F2");

            entity.HasOne(d => d.user).WithMany(p => p.user_roleusers)
                .HasForeignKey(d => d.user_id)
                .HasConstraintName("FK__user_role__user___5AEE82B9");
        });
        modelBuilder.HasSequence("agreement_no_seq", "core").StartsAt(1001L);
        modelBuilder.HasSequence("invoice_no_seq", "core").StartsAt(1001L);
        modelBuilder.HasSequence("reservation_code_seq", "core").StartsAt(1001L);
        modelBuilder.HasSequence("ticket_no_seq", "core").StartsAt(1001L);
        modelBuilder.HasSequence("work_order_no_seq", "core").StartsAt(1001L);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
