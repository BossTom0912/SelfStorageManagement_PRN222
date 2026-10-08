/* ============================================================================
   SelfStoragePRN222 - SQL Server 2019+
   Consolidated & corrected SQL Server script
   Source reviewed against:
     - SelfStorageSystem_PhanTichHeThong
     - Business_Rules_SelfStorageSystem
     - State Chart explanation
     - PostgreSQL/PostgREST source database

   Important corrections:
     1) PostgreSQL/PostgREST-specific syntax is not included in this file.
     2) Unrelated dbo.[System.UserAccount] accounting template is removed.
     3) Reservation hold <= 15 minutes; rental term 1..12 months.
     4) One active voucher per reservation/invoice.
     5) Overdue starts N+1; access is suspended from N+1; default after >30 days.
     6) Late-fee seed represents 150% of listed daily rent (calculation completed
        by service/scheduler using fee_rules.conditions base=daily_rent).
     7) Missing cross-table validation triggers from PostgreSQL are restored.

   Run this whole file in SSMS in a normal query window.
   Recommended: SQL Server 2019+.
============================================================================ */

/* ===== 00_CreateDatabase.sql ===== */
/*
  Self Storage PRN222 - SQL Server CLEAN INSTALL bootstrap.
  Runs in a normal SSMS query window. SQLCMD Mode is NOT required.

  WARNING: this installer recreates ONLY the database [SelfStoragePRN222].
  Any existing data in that database will be deleted.
*/
USE [master];
GO

IF DB_ID(N'SelfStoragePRN222') IS NOT NULL
BEGIN
    ALTER DATABASE [SelfStoragePRN222] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [SelfStoragePRN222];
END;
GO

CREATE DATABASE [SelfStoragePRN222];
GO

USE [SelfStoragePRN222];
GO

IF DB_NAME() <> N'SelfStoragePRN222'
    THROW 51000, 'Installer is not running in SelfStoragePRN222.', 1;
GO

IF SCHEMA_ID(N'core') IS NULL EXEC(N'CREATE SCHEMA [core];');
IF SCHEMA_ID(N'api') IS NULL EXEC(N'CREATE SCHEMA [api];');
IF SCHEMA_ID(N'auth_private') IS NULL EXEC(N'CREATE SCHEMA [auth_private];');
GO


/* ===== 01_Schema_Corrected.sql ===== */
/* Generated from the PostgreSQL schema. Range/exclusion rules are in 02_BusinessRules.sql. */
USE [SelfStoragePRN222];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO
CREATE SEQUENCE [core].[reservation_code_seq] AS bigint START WITH 1001 INCREMENT BY 1;
CREATE SEQUENCE [core].[agreement_no_seq] AS bigint START WITH 1001 INCREMENT BY 1;
CREATE SEQUENCE [core].[invoice_no_seq] AS bigint START WITH 1001 INCREMENT BY 1;
CREATE SEQUENCE [core].[ticket_no_seq] AS bigint START WITH 1001 INCREMENT BY 1;
CREATE SEQUENCE [core].[work_order_no_seq] AS bigint START WITH 1001 INCREMENT BY 1;

CREATE TABLE [core].users (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    email nvarchar(255) NOT NULL,
    phone_number nvarchar(255),
    password_hash nvarchar(255) NOT NULL,
    status nvarchar(255) NOT NULL DEFAULT 'active'
        CHECK (status IN ('active', 'locked', 'disabled')),
    last_login_at datetimeoffset(7),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT users_email_normalized CHECK (email = lower(LTRIM(RTRIM(email)))),
    CONSTRAINT users_email_unique UNIQUE (email)
);

CREATE TABLE [core].roles (
    id smallint IDENTITY(1,1) PRIMARY KEY,
    code nvarchar(255) NOT NULL UNIQUE,
    display_name nvarchar(255) NOT NULL,
    description nvarchar(255),
    CHECK (code IN (
        'storage_customer',
        'facility_staff',
        'facility_manager',
        'business_operations_manager',
        'system_administrator'
    ))
);

CREATE TABLE [core].user_roles (
    user_id bigint NOT NULL REFERENCES [core].users(id) ON DELETE CASCADE,
    role_id smallint NOT NULL REFERENCES [core].roles(id) ON DELETE NO ACTION,
    granted_by bigint REFERENCES [core].users(id) ON DELETE NO ACTION,
    granted_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    PRIMARY KEY (user_id, role_id)
);

CREATE INDEX user_roles_role_id_idx ON [core].user_roles (role_id, user_id);
CREATE INDEX user_roles_granted_by_idx ON [core].user_roles (granted_by)
    WHERE granted_by IS NOT NULL;

CREATE TABLE [core].customer_profiles (
    user_id bigint PRIMARY KEY REFERENCES [core].users(id) ON DELETE NO ACTION,
    full_name nvarchar(255) NOT NULL,
    identity_number nvarchar(255),
    date_of_birth date,
    address nvarchar(255),
    emergency_contact_name nvarchar(255),
    emergency_contact_phone nvarchar(255),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE UNIQUE INDEX customer_profiles_identity_number_uidx
    ON [core].customer_profiles (identity_number)
    WHERE identity_number IS NOT NULL;

CREATE TABLE [core].employee_profiles (
    user_id bigint PRIMARY KEY REFERENCES [core].users(id) ON DELETE NO ACTION,
    employee_code nvarchar(255) NOT NULL UNIQUE,
    full_name nvarchar(255) NOT NULL,
    hire_date date NOT NULL,
    employment_status nvarchar(255) NOT NULL DEFAULT 'active'
        CHECK (employment_status IN ('active', 'leave', 'terminated')),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE [core].facilities (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    code nvarchar(255) NOT NULL UNIQUE,
    name nvarchar(255) NOT NULL,
    address_line nvarchar(255) NOT NULL,
    ward nvarchar(255),
    district nvarchar(255),
    city nvarchar(255) NOT NULL,
    latitude numeric(9, 6),
    longitude numeric(9, 6),
    timezone nvarchar(255) NOT NULL DEFAULT 'Asia/Ho_Chi_Minh',
    opening_time time,
    closing_time time,
    status nvarchar(255) NOT NULL DEFAULT 'active'
        CHECK (status IN ('draft', 'active', 'temporarily_closed', 'closed')),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (latitude IS NULL OR latitude BETWEEN -90 AND 90),
    CHECK (longitude IS NULL OR longitude BETWEEN -180 AND 180),
    CHECK (closing_time IS NULL OR opening_time IS NULL OR closing_time > opening_time)
);

CREATE INDEX facilities_city_status_idx ON [core].facilities (city, status);

CREATE TABLE [core].staff_facility_assignments (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    employee_id bigint NOT NULL REFERENCES [core].employee_profiles(user_id) ON DELETE NO ACTION,
    facility_id bigint NOT NULL REFERENCES [core].facilities(id) ON DELETE NO ACTION,
    assignment_role nvarchar(255) NOT NULL
        CHECK (assignment_role IN ('facility_staff', 'facility_manager')),
    starts_at datetimeoffset(7) NOT NULL,
    ends_at datetimeoffset(7),
    assigned_by bigint REFERENCES [core].users(id) ON DELETE NO ACTION,
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (ends_at IS NULL OR ends_at > starts_at)
);

CREATE INDEX staff_facility_assignments_facility_idx
    ON [core].staff_facility_assignments (facility_id, assignment_role, starts_at, ends_at);
CREATE INDEX staff_facility_assignments_assigned_by_idx
    ON [core].staff_facility_assignments (assigned_by)
    WHERE assigned_by IS NOT NULL;

CREATE TABLE [core].facility_areas (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    facility_id bigint NOT NULL REFERENCES [core].facilities(id) ON DELETE NO ACTION,
    parent_area_id bigint REFERENCES [core].facility_areas(id) ON DELETE NO ACTION,
    code nvarchar(255) NOT NULL,
    name nvarchar(255) NOT NULL,
    area_type nvarchar(255) NOT NULL CHECK (area_type IN ('building', 'floor', 'zone', 'section')),
    display_order int NOT NULL DEFAULT 0,
    map_metadata nvarchar(max) NOT NULL DEFAULT N'{}'
        CHECK (ISJSON(map_metadata) = 1),
    is_active bit NOT NULL DEFAULT 1,
    UNIQUE (facility_id, code),
    UNIQUE (id, facility_id),
    CHECK (parent_area_id IS NULL OR parent_area_id <> id),
    FOREIGN KEY (parent_area_id, facility_id)
        REFERENCES [core].facility_areas(id, facility_id) ON DELETE NO ACTION
);

CREATE INDEX facility_areas_parent_area_id_idx ON [core].facility_areas (parent_area_id)
    WHERE parent_area_id IS NOT NULL;

CREATE TABLE [core].unit_types (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    code nvarchar(255) NOT NULL UNIQUE,
    name nvarchar(255) NOT NULL,
    width_m numeric(8, 2) NOT NULL CHECK (width_m > 0),
    length_m numeric(8, 2) NOT NULL CHECK (length_m > 0),
    height_m numeric(8, 2) NOT NULL CHECK (height_m > 0),
    area_m2 AS (CONVERT(numeric(10, 2), width_m * length_m)) PERSISTED,
    volume_m3 AS (CONVERT(numeric(12, 2), width_m * length_m * height_m)) PERSISTED,
    climate_controlled bit NOT NULL DEFAULT 0,
    max_weight_kg numeric(12, 2) CHECK (max_weight_kg IS NULL OR max_weight_kg > 0),
    description nvarchar(255),
    is_active bit NOT NULL DEFAULT 1,
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE [core].storage_units (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    facility_id bigint NOT NULL REFERENCES [core].facilities(id) ON DELETE NO ACTION,
    unit_type_id bigint NOT NULL REFERENCES [core].unit_types(id) ON DELETE NO ACTION,
    area_id bigint REFERENCES [core].facility_areas(id) ON DELETE NO ACTION,
    unit_code nvarchar(255) NOT NULL,
    floor_label nvarchar(255),
    zone_label nvarchar(255),
    physical_status nvarchar(255) NOT NULL DEFAULT 'available'
        CHECK (physical_status IN (
            'available', 'reserved', 'occupied', 'pending_inspection',
            'maintenance', 'out_of_service'
        )),
    is_listed bit NOT NULL DEFAULT 1,
    notes nvarchar(255),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    UNIQUE (facility_id, unit_code),
    UNIQUE (id, facility_id),
    UNIQUE (id, facility_id, unit_type_id)
);

CREATE INDEX storage_units_unit_type_id_idx ON [core].storage_units (unit_type_id);
CREATE INDEX storage_units_area_id_idx ON [core].storage_units (area_id)
    WHERE area_id IS NOT NULL;
CREATE INDEX storage_units_catalog_idx
    ON [core].storage_units (facility_id, unit_type_id, physical_status)
    WHERE is_listed = 1;

CREATE TABLE [core].unit_map_positions (
    unit_id bigint PRIMARY KEY REFERENCES [core].storage_units(id) ON DELETE CASCADE,
    area_id bigint NOT NULL REFERENCES [core].facility_areas(id) ON DELETE NO ACTION,
    x numeric(10, 2) NOT NULL,
    y numeric(10, 2) NOT NULL,
    width numeric(10, 2) NOT NULL CHECK (width > 0),
    height numeric(10, 2) NOT NULL CHECK (height > 0),
    rotation_degrees numeric(6, 2) NOT NULL DEFAULT 0,
    metadata nvarchar(max) NOT NULL DEFAULT N'{}' CHECK (ISJSON(metadata) = 1)
);

CREATE INDEX unit_map_positions_area_id_idx ON [core].unit_map_positions (area_id);

CREATE TABLE [core].unit_status_history (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    storage_unit_id bigint NOT NULL REFERENCES [core].storage_units(id) ON DELETE NO ACTION,
    old_status nvarchar(255),
    new_status nvarchar(255) NOT NULL,
    reason nvarchar(255),
    changed_by bigint REFERENCES [core].users(id) ON DELETE NO ACTION,
    changed_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX unit_status_history_unit_changed_idx
    ON [core].unit_status_history (storage_unit_id, changed_at DESC);
CREATE INDEX unit_status_history_changed_by_idx
    ON [core].unit_status_history (changed_by)
    WHERE changed_by IS NOT NULL;

CREATE TABLE [core].price_ranges (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    unit_type_id bigint NOT NULL REFERENCES [core].unit_types(id) ON DELETE NO ACTION,
    min_monthly_rate numeric(14, 2) NOT NULL CHECK (min_monthly_rate >= 0),
    max_monthly_rate numeric(14, 2) NOT NULL,
    valid_from date NOT NULL,
    valid_to date,
    created_by bigint REFERENCES [core].users(id) ON DELETE NO ACTION,
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT price_ranges_max_rate_ck CHECK (max_monthly_rate >= min_monthly_rate),
    CHECK (valid_to IS NULL OR valid_to > valid_from)
);

CREATE INDEX price_ranges_created_by_idx ON [core].price_ranges (created_by)
    WHERE created_by IS NOT NULL;

CREATE TABLE [core].facility_rates (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    facility_id bigint NOT NULL REFERENCES [core].facilities(id) ON DELETE NO ACTION,
    unit_type_id bigint NOT NULL REFERENCES [core].unit_types(id) ON DELETE NO ACTION,
    monthly_rate numeric(14, 2) NOT NULL CHECK (monthly_rate >= 0),
    deposit_amount numeric(14, 2) NOT NULL CHECK (deposit_amount >= 0),
    booking_fee numeric(14, 2) NOT NULL DEFAULT 0 CHECK (booking_fee >= 0),
    valid_from date NOT NULL,
    valid_to date,
    created_by bigint REFERENCES [core].users(id) ON DELETE NO ACTION,
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (valid_to IS NULL OR valid_to > valid_from)
);

CREATE INDEX facility_rates_unit_type_id_idx ON [core].facility_rates (unit_type_id);
CREATE INDEX facility_rates_created_by_idx ON [core].facility_rates (created_by)
    WHERE created_by IS NOT NULL;

CREATE TABLE [core].policy_versions (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    policy_type nvarchar(255) NOT NULL CHECK (policy_type IN (
        'rental_terms', 'deposit', 'cancellation', 'renewal', 'move_out', 'overdue'
    )),
    version nvarchar(255) NOT NULL,
    content nvarchar(max) NOT NULL CHECK (ISJSON(content) = 1),
    valid_from date NOT NULL,
    valid_to date,
    created_by bigint REFERENCES [core].users(id) ON DELETE NO ACTION,
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (valid_to IS NULL OR valid_to > valid_from),
    UNIQUE (policy_type, version)
);

CREATE INDEX policy_versions_created_by_idx ON [core].policy_versions (created_by)
    WHERE created_by IS NOT NULL;

CREATE TABLE [core].fee_rules (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    facility_id bigint REFERENCES [core].facilities(id) ON DELETE NO ACTION,
    code nvarchar(255) NOT NULL,
    fee_type nvarchar(255) NOT NULL CHECK (fee_type IN (
        'late_fee', 'damage_fee', 'lost_key_fee', 'maintenance_fee', 'other'
    )),
    calculation_method nvarchar(255) NOT NULL CHECK (calculation_method IN ('flat', 'percentage', 'per_day')),
    amount numeric(14, 2) CHECK (amount IS NULL OR amount >= 0),
    rate_percent numeric(7, 4) CHECK (rate_percent IS NULL OR (rate_percent > 0 AND rate_percent <= 1000)),
    grace_days int NOT NULL DEFAULT 0 CHECK (grace_days >= 0),
    conditions nvarchar(max) NOT NULL DEFAULT N'{}' CHECK (ISJSON(conditions) = 1),
    valid_from date NOT NULL,
    valid_to date,
    is_active bit NOT NULL DEFAULT 1,
    created_by bigint REFERENCES [core].users(id) ON DELETE NO ACTION,
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (valid_to IS NULL OR valid_to > valid_from),
    CHECK (
        (calculation_method IN ('flat', 'per_day') AND amount IS NOT NULL)
        OR (calculation_method = 'percentage' AND rate_percent IS NOT NULL)
    )
);

CREATE UNIQUE INDEX fee_rules_scope_code_start_uidx
    ON [core].fee_rules (facility_id, code, valid_from);
CREATE INDEX fee_rules_facility_id_idx ON [core].fee_rules (facility_id)
    WHERE facility_id IS NOT NULL;
CREATE INDEX fee_rules_created_by_idx ON [core].fee_rules (created_by)
    WHERE created_by IS NOT NULL;

CREATE TABLE [core].promotions (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    code nvarchar(255) NOT NULL UNIQUE,
    name nvarchar(255) NOT NULL,
    description nvarchar(255),
    discount_type nvarchar(255) NOT NULL CHECK (discount_type IN ('fixed', 'percentage', 'free_days')),
    discount_value numeric(14, 2) NOT NULL CHECK (discount_value > 0),
    max_discount_amount numeric(14, 2) CHECK (max_discount_amount IS NULL OR max_discount_amount > 0),
    usage_limit int CHECK (usage_limit IS NULL OR usage_limit > 0),
    per_customer_limit int CHECK (per_customer_limit IS NULL OR per_customer_limit > 0),
    valid_from datetimeoffset(7) NOT NULL,
    valid_to datetimeoffset(7) NOT NULL,
    is_active bit NOT NULL DEFAULT 1,
    created_by bigint REFERENCES [core].users(id) ON DELETE NO ACTION,
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (valid_to > valid_from),
    CHECK (discount_type <> 'percentage' OR discount_value <= 100)
);

CREATE INDEX promotions_active_period_idx ON [core].promotions (valid_from, valid_to)
    WHERE is_active = 1;
CREATE INDEX promotions_created_by_idx ON [core].promotions (created_by)
    WHERE created_by IS NOT NULL;

CREATE TABLE [core].promotion_rules (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    promotion_id bigint NOT NULL REFERENCES [core].promotions(id) ON DELETE CASCADE,
    rule_type nvarchar(255) NOT NULL CHECK (rule_type IN (
        'minimum_months', 'minimum_amount', 'facility', 'unit_type', 'new_customer'
    )),
    operator nvarchar(255) NOT NULL DEFAULT 'eq' CHECK (operator IN ('eq', 'gte', 'lte', 'in')),
    rule_value nvarchar(max) NOT NULL,
    UNIQUE (promotion_id, rule_type)
);

CREATE INDEX promotion_rules_promotion_id_idx ON [core].promotion_rules (promotion_id);

CREATE TABLE [core].reservations (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    reservation_code nvarchar(255) NOT NULL UNIQUE,
    customer_id bigint NOT NULL REFERENCES [core].customer_profiles(user_id) ON DELETE NO ACTION,
    facility_id bigint NOT NULL REFERENCES [core].facilities(id) ON DELETE NO ACTION,
    unit_type_id bigint NOT NULL REFERENCES [core].unit_types(id) ON DELETE NO ACTION,
    facility_rate_id bigint NOT NULL REFERENCES [core].facility_rates(id) ON DELETE NO ACTION,
    start_date date NOT NULL,
    end_date date NOT NULL,
    monthly_rate_snapshot numeric(14, 2) NOT NULL CHECK (monthly_rate_snapshot >= 0),
    deposit_snapshot numeric(14, 2) NOT NULL CHECK (deposit_snapshot >= 0),
    booking_fee_snapshot numeric(14, 2) NOT NULL DEFAULT 0 CHECK (booking_fee_snapshot >= 0),
    discount_snapshot numeric(14, 2) NOT NULL DEFAULT 0 CHECK (discount_snapshot >= 0),
    quoted_total numeric(14, 2) NOT NULL CHECK (quoted_total >= 0),
    hold_until datetimeoffset(7) NOT NULL,
    status nvarchar(255) NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending', 'awaiting_deposit', 'confirmed', 'checked_in', 'converted', 'completed', 'no_show', 'cancelled', 'expired')),
    confirmed_at datetimeoffset(7),
    cancelled_at datetimeoffset(7),
    cancellation_reason nvarchar(255),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (end_date > start_date),
    CONSTRAINT reservations_rental_term_ck CHECK (
        end_date >= DATEADD(month, 1, start_date)
        AND end_date <= DATEADD(month, 12, start_date)
    ),
    CHECK (hold_until > created_at),
    CONSTRAINT reservations_hold_max_15m_ck CHECK (hold_until <= DATEADD(minute, 15, created_at)),
    CHECK ((status = 'cancelled' AND cancelled_at IS NOT NULL) OR status <> 'cancelled'),
    UNIQUE (id, customer_id, facility_id, unit_type_id)
);

CREATE INDEX reservations_customer_created_idx ON [core].reservations (customer_id, created_at DESC);
CREATE INDEX reservations_facility_status_start_idx ON [core].reservations (facility_id, status, start_date);
CREATE INDEX reservations_unit_type_id_idx ON [core].reservations (unit_type_id);
CREATE INDEX reservations_facility_rate_id_idx ON [core].reservations (facility_rate_id);
CREATE INDEX reservations_open_hold_idx ON [core].reservations (hold_until)
    WHERE status IN ('pending', 'awaiting_deposit');

CREATE TABLE [core].rental_agreements (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    agreement_no nvarchar(255) NOT NULL UNIQUE,
    reservation_id bigint NOT NULL UNIQUE REFERENCES [core].reservations(id) ON DELETE NO ACTION,
    customer_id bigint NOT NULL REFERENCES [core].customer_profiles(user_id) ON DELETE NO ACTION,
    facility_id bigint NOT NULL REFERENCES [core].facilities(id) ON DELETE NO ACTION,
    policy_version_id bigint NOT NULL REFERENCES [core].policy_versions(id) ON DELETE NO ACTION,
    start_date date NOT NULL,
    end_date date NOT NULL,
    monthly_rate_snapshot numeric(14, 2) NOT NULL CHECK (monthly_rate_snapshot >= 0),
    deposit_snapshot numeric(14, 2) NOT NULL CHECK (deposit_snapshot >= 0),
    deposit_balance numeric(14, 2) NOT NULL CHECK (deposit_balance >= 0),
    status nvarchar(255) NOT NULL DEFAULT 'draft'
        CHECK (status IN (
            'draft', 'scheduled', 'active', 'extended', 'overdue', 'defaulted',
            'move_out_scheduled', 'checkout_pending', 'expired', 'terminated',
            'completed', 'closed', 'cancelled'
        )),
    signed_at datetimeoffset(7),
    checked_in_at datetimeoffset(7),
    checked_out_at datetimeoffset(7),
    actual_end_date date,
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (end_date > start_date),
    CHECK (checked_out_at IS NULL OR checked_in_at IS NOT NULL),
    CHECK (checked_out_at IS NULL OR checked_out_at >= checked_in_at),
    UNIQUE (id, customer_id, facility_id)
);

CREATE INDEX rental_agreements_customer_status_idx
    ON [core].rental_agreements (customer_id, status, end_date);
CREATE INDEX rental_agreements_facility_status_idx
    ON [core].rental_agreements (facility_id, status, end_date);
CREATE INDEX rental_agreements_policy_version_id_idx
    ON [core].rental_agreements (policy_version_id);

CREATE TABLE [core].unit_allocations (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    storage_unit_id bigint NOT NULL REFERENCES [core].storage_units(id) ON DELETE NO ACTION,
    reservation_id bigint REFERENCES [core].reservations(id) ON DELETE NO ACTION,
    agreement_id bigint REFERENCES [core].rental_agreements(id) ON DELETE NO ACTION,
    allocation_kind nvarchar(255) NOT NULL CHECK (allocation_kind IN ('reservation_hold', 'rental', 'transfer')),
    allocation_start_date date NOT NULL,

    allocation_end_date date NOT NULL,
    status nvarchar(255) NOT NULL DEFAULT 'active'
        CHECK (status IN ('active', 'consumed', 'released', 'expired')),
    reason nvarchar(255),
    assigned_by bigint REFERENCES [core].users(id) ON DELETE NO ACTION,
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    ended_at datetimeoffset(7),
    CHECK ((CASE WHEN reservation_id IS NULL THEN 0 ELSE 1 END + CASE WHEN agreement_id IS NULL THEN 0 ELSE 1 END) = 1),
    CHECK (allocation_end_date > allocation_start_date),
    CHECK (
        (status = 'active' AND ended_at IS NULL)
        OR (status <> 'active' AND ended_at IS NOT NULL)
    )
);

CREATE UNIQUE INDEX unit_allocations_one_active_reservation_uidx
    ON [core].unit_allocations (reservation_id)
    WHERE status = 'active' AND reservation_id IS NOT NULL;
CREATE INDEX unit_allocations_agreement_period_idx
    ON [core].unit_allocations (agreement_id, allocation_start_date, allocation_end_date)
    WHERE agreement_id IS NOT NULL;
CREATE INDEX unit_allocations_unit_period_idx
    ON [core].unit_allocations (storage_unit_id, allocation_start_date, allocation_end_date);
CREATE INDEX unit_allocations_assigned_by_idx ON [core].unit_allocations (assigned_by)
    WHERE assigned_by IS NOT NULL;

CREATE TABLE [core].appointments (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    facility_id bigint NOT NULL REFERENCES [core].facilities(id) ON DELETE NO ACTION,
    reservation_id bigint REFERENCES [core].reservations(id) ON DELETE NO ACTION,
    agreement_id bigint REFERENCES [core].rental_agreements(id) ON DELETE NO ACTION,
    assigned_staff_id bigint REFERENCES [core].employee_profiles(user_id) ON DELETE NO ACTION,
    appointment_type nvarchar(255) NOT NULL CHECK (appointment_type IN ('check_in', 'check_out', 'transfer', 'support', 'inspection')),
    starts_at datetimeoffset(7) NOT NULL,
    ends_at datetimeoffset(7) NOT NULL,
    status nvarchar(255) NOT NULL DEFAULT 'scheduled'
        CHECK (status IN ('scheduled', 'confirmed', 'in_progress', 'completed', 'cancelled', 'no_show')),
    notes nvarchar(255),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK ((CASE WHEN reservation_id IS NULL THEN 0 ELSE 1 END + CASE WHEN agreement_id IS NULL THEN 0 ELSE 1 END) = 1),
    CHECK (ends_at > starts_at)
);

CREATE INDEX appointments_facility_schedule_idx ON [core].appointments (facility_id, starts_at, status);
CREATE INDEX appointments_reservation_id_idx ON [core].appointments (reservation_id)
    WHERE reservation_id IS NOT NULL;
CREATE INDEX appointments_agreement_id_idx ON [core].appointments (agreement_id)
    WHERE agreement_id IS NOT NULL;
CREATE INDEX appointments_assigned_staff_id_idx ON [core].appointments (assigned_staff_id)
    WHERE assigned_staff_id IS NOT NULL;

CREATE TABLE [core].identity_verifications (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    reservation_id bigint NOT NULL REFERENCES [core].reservations(id) ON DELETE NO ACTION,
    verified_by bigint NOT NULL REFERENCES [core].employee_profiles(user_id) ON DELETE NO ACTION,
    verification_method nvarchar(255) NOT NULL CHECK (verification_method IN ('government_id', 'passport', 'digital_identity', 'manual')),
    document_fingerprint nvarchar(255),
    result nvarchar(255) NOT NULL CHECK (result IN ('verified', 'rejected')),
    notes nvarchar(255),
    verified_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX identity_verifications_reservation_idx
    ON [core].identity_verifications (reservation_id, verified_at DESC);
CREATE INDEX identity_verifications_verified_by_idx ON [core].identity_verifications (verified_by);

CREATE TABLE [core].inspections (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    facility_id bigint NOT NULL REFERENCES [core].facilities(id) ON DELETE NO ACTION,
    storage_unit_id bigint REFERENCES [core].storage_units(id) ON DELETE NO ACTION,
    reservation_id bigint REFERENCES [core].reservations(id) ON DELETE NO ACTION,
    agreement_id bigint REFERENCES [core].rental_agreements(id) ON DELETE NO ACTION,
    inspected_by bigint NOT NULL REFERENCES [core].employee_profiles(user_id) ON DELETE NO ACTION,
    inspection_type nvarchar(255) NOT NULL CHECK (inspection_type IN (
        'check_in', 'check_out', 'transfer', 'security', 'fire_safety', 'routine', 'incident'
    )),
    status nvarchar(255) NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'completed', 'failed')),
    overall_condition nvarchar(255) CHECK (overall_condition IN ('good', 'acceptable', 'damaged', 'unsafe')),
    summary nvarchar(255),
    inspected_at datetimeoffset(7),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (
        inspection_type NOT IN ('check_in', 'check_out', 'transfer')
        OR storage_unit_id IS NOT NULL
    ),
    CHECK ((status = 'draft' AND inspected_at IS NULL) OR (status <> 'draft' AND inspected_at IS NOT NULL))
);

CREATE INDEX inspections_facility_type_date_idx
    ON [core].inspections (facility_id, inspection_type, created_at DESC);
CREATE INDEX inspections_storage_unit_id_idx ON [core].inspections (storage_unit_id)
    WHERE storage_unit_id IS NOT NULL;
CREATE INDEX inspections_reservation_id_idx ON [core].inspections (reservation_id)
    WHERE reservation_id IS NOT NULL;
CREATE INDEX inspections_agreement_id_idx ON [core].inspections (agreement_id)
    WHERE agreement_id IS NOT NULL;
CREATE INDEX inspections_inspected_by_idx ON [core].inspections (inspected_by);

CREATE TABLE [core].inspection_items (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    inspection_id bigint NOT NULL REFERENCES [core].inspections(id) ON DELETE NO ACTION,
    item_name nvarchar(255) NOT NULL,
    condition nvarchar(255) NOT NULL CHECK (condition IN ('good', 'acceptable', 'damaged', 'missing', 'not_applicable')),
    notes nvarchar(255),
    photo_url nvarchar(255),
    charge_amount numeric(14, 2) NOT NULL DEFAULT 0 CHECK (charge_amount >= 0)
);

CREATE INDEX inspection_items_inspection_id_idx ON [core].inspection_items (inspection_id);

CREATE TABLE [core].handover_records (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    agreement_id bigint NOT NULL REFERENCES [core].rental_agreements(id) ON DELETE NO ACTION,
    unit_allocation_id bigint NOT NULL REFERENCES [core].unit_allocations(id) ON DELETE NO ACTION,
    inspection_id bigint NOT NULL REFERENCES [core].inspections(id) ON DELETE NO ACTION,
    handled_by bigint NOT NULL REFERENCES [core].employee_profiles(user_id) ON DELETE NO ACTION,
    handover_type nvarchar(255) NOT NULL CHECK (handover_type IN ('check_in', 'check_out', 'transfer')),
    customer_signature_ref nvarchar(255),
    staff_signature_ref nvarchar(255),
    customer_signed_at datetimeoffset(7),
    staff_signed_at datetimeoffset(7),
    notes nvarchar(255),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX handover_records_agreement_idx
    ON [core].handover_records (agreement_id, created_at DESC);
CREATE INDEX handover_records_unit_allocation_id_idx ON [core].handover_records (unit_allocation_id);
CREATE INDEX handover_records_inspection_id_idx ON [core].handover_records (inspection_id);
CREATE INDEX handover_records_handled_by_idx ON [core].handover_records (handled_by);

CREATE TABLE [core].authorized_access_members (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    agreement_id bigint NOT NULL REFERENCES [core].rental_agreements(id) ON DELETE NO ACTION,
    full_name nvarchar(255) NOT NULL,
    identity_fingerprint nvarchar(255),
    relationship_to_customer nvarchar(255),
    valid_from datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    valid_to datetimeoffset(7),
    status nvarchar(255) NOT NULL DEFAULT 'active' CHECK (status IN ('pending', 'active', 'revoked', 'expired')),
    created_by bigint NOT NULL REFERENCES [core].users(id) ON DELETE NO ACTION,
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (valid_to IS NULL OR valid_to > valid_from)
);

CREATE INDEX authorized_access_members_agreement_idx
    ON [core].authorized_access_members (agreement_id, status);
CREATE INDEX authorized_access_members_created_by_idx ON [core].authorized_access_members (created_by);

CREATE TABLE [core].rental_renewals (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    agreement_id bigint NOT NULL REFERENCES [core].rental_agreements(id) ON DELETE NO ACTION,
    requested_by bigint NOT NULL REFERENCES [core].users(id) ON DELETE NO ACTION,
    old_end_date date NOT NULL,
    requested_end_date date NOT NULL,
    approved_end_date date,
    old_monthly_rate numeric(14, 2) NOT NULL CHECK (old_monthly_rate >= 0),
    new_monthly_rate numeric(14, 2) CHECK (new_monthly_rate IS NULL OR new_monthly_rate >= 0),
    status nvarchar(255) NOT NULL DEFAULT 'pending_payment'
        CHECK (status IN ('pending_payment', 'paid', 'approved', 'rejected', 'cancelled')),
    reviewed_by bigint REFERENCES [core].users(id) ON DELETE NO ACTION,
    reviewed_at datetimeoffset(7),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (requested_end_date > old_end_date),
    CHECK (approved_end_date IS NULL OR approved_end_date > old_end_date)
);

CREATE INDEX rental_renewals_agreement_idx ON [core].rental_renewals (agreement_id, created_at DESC);
CREATE INDEX rental_renewals_requested_by_idx ON [core].rental_renewals (requested_by);
CREATE INDEX rental_renewals_reviewed_by_idx ON [core].rental_renewals (reviewed_by)
    WHERE reviewed_by IS NOT NULL;
CREATE UNIQUE INDEX rental_renewals_one_open_uidx ON [core].rental_renewals (agreement_id)
    WHERE status IN ('pending_payment', 'paid');

CREATE TABLE [core].unit_transfer_requests (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    agreement_id bigint NOT NULL REFERENCES [core].rental_agreements(id) ON DELETE NO ACTION,
    requested_unit_type_id bigint NOT NULL REFERENCES [core].unit_types(id) ON DELETE NO ACTION,
    from_unit_id bigint NOT NULL REFERENCES [core].storage_units(id) ON DELETE NO ACTION,
    to_unit_id bigint REFERENCES [core].storage_units(id) ON DELETE NO ACTION,
    requested_effective_date date NOT NULL,
    reason nvarchar(255) NOT NULL,
    status nvarchar(255) NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending', 'approved', 'scheduled', 'completed', 'rejected', 'cancelled')),
    requested_by bigint NOT NULL REFERENCES [core].users(id) ON DELETE NO ACTION,
    reviewed_by bigint REFERENCES [core].users(id) ON DELETE NO ACTION,
    reviewed_at datetimeoffset(7),
    completed_at datetimeoffset(7),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX unit_transfer_requests_agreement_idx
    ON [core].unit_transfer_requests (agreement_id, status, created_at DESC);
CREATE INDEX unit_transfer_requests_requested_unit_type_id_idx
    ON [core].unit_transfer_requests (requested_unit_type_id);
CREATE INDEX unit_transfer_requests_from_unit_id_idx ON [core].unit_transfer_requests (from_unit_id);
CREATE INDEX unit_transfer_requests_to_unit_id_idx ON [core].unit_transfer_requests (to_unit_id)
    WHERE to_unit_id IS NOT NULL;
CREATE INDEX unit_transfer_requests_requested_by_idx ON [core].unit_transfer_requests (requested_by);
CREATE INDEX unit_transfer_requests_reviewed_by_idx ON [core].unit_transfer_requests (reviewed_by)
    WHERE reviewed_by IS NOT NULL;
CREATE UNIQUE INDEX unit_transfer_requests_one_open_uidx ON [core].unit_transfer_requests (agreement_id)
    WHERE status IN ('pending', 'approved', 'scheduled');

CREATE TABLE [core].move_out_requests (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    agreement_id bigint NOT NULL REFERENCES [core].rental_agreements(id) ON DELETE NO ACTION,
    requested_by bigint NOT NULL REFERENCES [core].users(id) ON DELETE NO ACTION,
    requested_move_out_date date NOT NULL,
    appointment_id bigint REFERENCES [core].appointments(id) ON DELETE NO ACTION,
    status nvarchar(255) NOT NULL DEFAULT 'requested'
        CHECK (status IN (
            'requested', 'scheduled', 'in_progress', 'inspection_pending',
            'settlement_pending', 'completed', 'cancelled'
        )),
    reason nvarchar(255),
    finalized_at datetimeoffset(7),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (
        (status IN ('completed', 'cancelled') AND finalized_at IS NOT NULL)
        OR (status NOT IN ('completed', 'cancelled') AND finalized_at IS NULL)
    )
);

CREATE INDEX move_out_requests_agreement_idx
    ON [core].move_out_requests (agreement_id, status, created_at DESC);
CREATE INDEX move_out_requests_requested_by_idx ON [core].move_out_requests (requested_by);
CREATE INDEX move_out_requests_appointment_id_idx ON [core].move_out_requests (appointment_id)
    WHERE appointment_id IS NOT NULL;
CREATE UNIQUE INDEX move_out_requests_one_open_uidx ON [core].move_out_requests (agreement_id)
    WHERE status <> 'completed' AND status <> 'cancelled';

CREATE TABLE [core].staff_shifts (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    facility_id bigint NOT NULL REFERENCES [core].facilities(id) ON DELETE NO ACTION,
    shift_name nvarchar(255) NOT NULL,
    starts_at datetimeoffset(7) NOT NULL,
    ends_at datetimeoffset(7) NOT NULL,
    status nvarchar(255) NOT NULL DEFAULT 'planned' CHECK (status IN ('planned', 'open', 'completed', 'cancelled')),
    created_by bigint NOT NULL REFERENCES [core].users(id) ON DELETE NO ACTION,
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (ends_at > starts_at)
);

CREATE INDEX staff_shifts_facility_schedule_idx
    ON [core].staff_shifts (facility_id, starts_at, ends_at, status);
CREATE INDEX staff_shifts_created_by_idx ON [core].staff_shifts (created_by);

CREATE TABLE [core].shift_assignments (
    shift_id bigint NOT NULL REFERENCES [core].staff_shifts(id) ON DELETE CASCADE,
    employee_id bigint NOT NULL REFERENCES [core].employee_profiles(user_id) ON DELETE NO ACTION,
    duty_role nvarchar(255) NOT NULL CHECK (duty_role IN ('staff', 'shift_lead', 'manager_on_call')),
    check_in_at datetimeoffset(7),
    check_out_at datetimeoffset(7),
    status nvarchar(255) NOT NULL DEFAULT 'scheduled' CHECK (status IN ('scheduled', 'checked_in', 'checked_out', 'absent')),
    PRIMARY KEY (shift_id, employee_id),
    CHECK (check_out_at IS NULL OR check_in_at IS NOT NULL),
    CHECK (check_out_at IS NULL OR check_out_at >= check_in_at)
);

CREATE INDEX shift_assignments_employee_id_idx ON [core].shift_assignments (employee_id, shift_id);

CREATE TABLE [core].support_tickets (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    ticket_no nvarchar(255) NOT NULL UNIQUE,
    customer_id bigint NOT NULL REFERENCES [core].customer_profiles(user_id) ON DELETE NO ACTION,
    facility_id bigint NOT NULL REFERENCES [core].facilities(id) ON DELETE NO ACTION,
    agreement_id bigint REFERENCES [core].rental_agreements(id) ON DELETE NO ACTION,
    storage_unit_id bigint REFERENCES [core].storage_units(id) ON DELETE NO ACTION,
    category nvarchar(255) NOT NULL CHECK (category IN (
        'unit', 'access', 'payment', 'stored_item', 'maintenance', 'other'
    )),
    priority nvarchar(255) NOT NULL DEFAULT 'normal' CHECK (priority IN ('low', 'normal', 'high', 'urgent')),
    subject nvarchar(255) NOT NULL,
    description nvarchar(255) NOT NULL,
    status nvarchar(255) NOT NULL DEFAULT 'open' CHECK (status IN (
        'open', 'in_progress', 'waiting_for_customer', 'waiting_for_maintenance',
        'resolved', 'closed', 'cancelled'
    )),
    resolution nvarchar(255),
    resolved_at datetimeoffset(7),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    FOREIGN KEY (agreement_id, customer_id, facility_id)
        REFERENCES [core].rental_agreements(id, customer_id, facility_id) ON DELETE NO ACTION,
    FOREIGN KEY (storage_unit_id, facility_id)
        REFERENCES [core].storage_units(id, facility_id) ON DELETE NO ACTION,
    CHECK (LTRIM(RTRIM(subject)) <> ''),
    CHECK (LTRIM(RTRIM(description)) <> ''),
    CHECK (
        (status IN ('resolved', 'closed') AND resolved_at IS NOT NULL AND resolution IS NOT NULL)
        OR status NOT IN ('resolved', 'closed')
    )
);

CREATE INDEX support_tickets_customer_created_idx
    ON [core].support_tickets (customer_id, created_at DESC);
CREATE INDEX support_tickets_facility_queue_idx
    ON [core].support_tickets (facility_id, status, priority, created_at);
CREATE INDEX support_tickets_agreement_id_idx ON [core].support_tickets (agreement_id)
    WHERE agreement_id IS NOT NULL;
CREATE INDEX support_tickets_storage_unit_id_idx ON [core].support_tickets (storage_unit_id)
    WHERE storage_unit_id IS NOT NULL;

CREATE TABLE [core].ticket_assignments (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    ticket_id bigint NOT NULL REFERENCES [core].support_tickets(id) ON DELETE NO ACTION,
    employee_id bigint NOT NULL REFERENCES [core].employee_profiles(user_id) ON DELETE NO ACTION,
    assigned_by bigint REFERENCES [core].users(id) ON DELETE NO ACTION,
    assigned_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    ended_at datetimeoffset(7),
    end_reason nvarchar(255),
    CHECK (ended_at IS NULL OR ended_at >= assigned_at)
);

CREATE INDEX ticket_assignments_ticket_history_idx
    ON [core].ticket_assignments (ticket_id, assigned_at DESC);
CREATE INDEX ticket_assignments_employee_id_idx
    ON [core].ticket_assignments (employee_id, ended_at);
CREATE INDEX ticket_assignments_assigned_by_idx ON [core].ticket_assignments (assigned_by)
    WHERE assigned_by IS NOT NULL;
CREATE UNIQUE INDEX ticket_assignments_one_active_uidx ON [core].ticket_assignments (ticket_id)
    WHERE ended_at IS NULL;

CREATE TABLE [core].ticket_messages (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    ticket_id bigint NOT NULL REFERENCES [core].support_tickets(id) ON DELETE NO ACTION,
    author_user_id bigint REFERENCES [core].users(id) ON DELETE NO ACTION,
    body nvarchar(255) NOT NULL,
    is_internal bit NOT NULL DEFAULT 0,
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    UNIQUE (id, ticket_id),
    CHECK (LTRIM(RTRIM(body)) <> '')
);

CREATE INDEX ticket_messages_ticket_created_idx ON [core].ticket_messages (ticket_id, created_at);
CREATE INDEX ticket_messages_author_user_id_idx ON [core].ticket_messages (author_user_id)
    WHERE author_user_id IS NOT NULL;

CREATE TABLE [core].ticket_attachments (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    ticket_id bigint NOT NULL REFERENCES [core].support_tickets(id) ON DELETE NO ACTION,
    message_id bigint REFERENCES [core].ticket_messages(id) ON DELETE NO ACTION,
    uploaded_by bigint NOT NULL REFERENCES [core].users(id) ON DELETE NO ACTION,
    file_name nvarchar(255) NOT NULL,
    mime_type nvarchar(255) NOT NULL,
    file_size_bytes bigint NOT NULL CHECK (file_size_bytes > 0),
    object_url nvarchar(255) NOT NULL,
    sha256 nvarchar(255),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    FOREIGN KEY (message_id, ticket_id)
        REFERENCES [core].ticket_messages(id, ticket_id) ON DELETE NO ACTION,
    CHECK (LTRIM(RTRIM(file_name)) <> ''),
    CHECK (LTRIM(RTRIM(mime_type)) <> ''),
    CHECK (LTRIM(RTRIM(object_url)) <> '')
);

CREATE INDEX ticket_attachments_ticket_id_idx ON [core].ticket_attachments (ticket_id, created_at);
CREATE INDEX ticket_attachments_message_id_idx ON [core].ticket_attachments (message_id)
    WHERE message_id IS NOT NULL;
CREATE INDEX ticket_attachments_uploaded_by_idx ON [core].ticket_attachments (uploaded_by);

CREATE TABLE [core].ticket_charge_proposals (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    ticket_id bigint NOT NULL REFERENCES [core].support_tickets(id) ON DELETE NO ACTION,
    proposed_by bigint NOT NULL REFERENCES [core].employee_profiles(user_id) ON DELETE NO ACTION,
    description nvarchar(255) NOT NULL,
    amount numeric(14, 2) NOT NULL CHECK (amount > 0),
    status nvarchar(255) NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending', 'approved', 'rejected', 'invoiced', 'cancelled')),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ticket_charge_proposals_ticket_idx
    ON [core].ticket_charge_proposals (ticket_id, status, created_at DESC);
CREATE INDEX ticket_charge_proposals_proposed_by_idx ON [core].ticket_charge_proposals (proposed_by);

CREATE TABLE [core].ticket_charge_approvals (
    proposal_id bigint PRIMARY KEY REFERENCES [core].ticket_charge_proposals(id) ON DELETE NO ACTION,
    decision nvarchar(255) NOT NULL CHECK (decision IN ('approved', 'rejected')),
    decided_by bigint NOT NULL REFERENCES [core].employee_profiles(user_id) ON DELETE NO ACTION,
    reason nvarchar(255),
    decided_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX ticket_charge_approvals_decided_by_idx ON [core].ticket_charge_approvals (decided_by);

CREATE TABLE [core].service_ratings (
    ticket_id bigint PRIMARY KEY REFERENCES [core].support_tickets(id) ON DELETE NO ACTION,
    customer_id bigint NOT NULL REFERENCES [core].customer_profiles(user_id) ON DELETE NO ACTION,
    score smallint NOT NULL CHECK (score BETWEEN 1 AND 5),
    comment nvarchar(255),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX service_ratings_customer_id_idx ON [core].service_ratings (customer_id);

CREATE TABLE [core].invoices (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    invoice_no nvarchar(255) NOT NULL UNIQUE,
    customer_id bigint NOT NULL REFERENCES [core].customer_profiles(user_id) ON DELETE NO ACTION,
    reservation_id bigint REFERENCES [core].reservations(id) ON DELETE NO ACTION,
    agreement_id bigint REFERENCES [core].rental_agreements(id) ON DELETE NO ACTION,
    ticket_charge_proposal_id bigint UNIQUE REFERENCES [core].ticket_charge_proposals(id) ON DELETE NO ACTION,
    billing_period nvarchar(64),
    issue_date date NOT NULL,
    due_date date NOT NULL,
    currency char(3) NOT NULL DEFAULT 'VND',
    subtotal_amount numeric(14, 2) NOT NULL DEFAULT 0 CHECK (subtotal_amount >= 0),
    discount_amount numeric(14, 2) NOT NULL DEFAULT 0 CHECK (discount_amount >= 0),
    tax_amount numeric(14, 2) NOT NULL DEFAULT 0 CHECK (tax_amount >= 0),
    total_amount numeric(14, 2) NOT NULL DEFAULT 0 CHECK (total_amount >= 0),
    paid_amount numeric(14, 2) NOT NULL DEFAULT 0 CHECK (paid_amount >= 0),
    status nvarchar(255) NOT NULL DEFAULT 'draft'
        CHECK (status IN ('draft', 'open', 'partially_paid', 'paid', 'overdue', 'voided')),
    opened_at datetimeoffset(7),
    voided_at datetimeoffset(7),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (due_date >= issue_date),
    CHECK (currency = upper(currency)),
    CHECK ((CASE WHEN reservation_id IS NULL THEN 0 ELSE 1 END + CASE WHEN agreement_id IS NULL THEN 0 ELSE 1 END + CASE WHEN ticket_charge_proposal_id IS NULL THEN 0 ELSE 1 END) >= 1),
    CHECK (paid_amount <= total_amount)
);

CREATE INDEX invoices_customer_status_due_idx ON [core].invoices (customer_id, status, due_date);
CREATE INDEX invoices_reservation_id_idx ON [core].invoices (reservation_id)
    WHERE reservation_id IS NOT NULL;
CREATE INDEX invoices_agreement_id_idx ON [core].invoices (agreement_id)
    WHERE agreement_id IS NOT NULL;
CREATE UNIQUE INDEX invoices_agreement_billing_period_uidx
    ON [core].invoices (agreement_id, billing_period)
    WHERE agreement_id IS NOT NULL AND billing_period IS NOT NULL AND status <> 'voided';
CREATE INDEX invoices_open_due_idx ON [core].invoices (due_date)
    WHERE status IN ('open', 'partially_paid', 'overdue');

CREATE TABLE [core].invoice_lines (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    invoice_id bigint NOT NULL REFERENCES [core].invoices(id) ON DELETE NO ACTION,
    line_type nvarchar(255) NOT NULL CHECK (line_type IN (
        'deposit', 'rent', 'renewal', 'late_fee', 'damage', 'maintenance',
        'booking_fee', 'discount', 'other'
    )),
    description nvarchar(255) NOT NULL,
    quantity numeric(12, 3) NOT NULL DEFAULT 1 CHECK (quantity > 0),
    unit_price numeric(14, 2) NOT NULL,
    line_amount AS (CONVERT(numeric(14, 2), ROUND(quantity * unit_price, 2))) PERSISTED,
    metadata nvarchar(max) NOT NULL DEFAULT N'{}' CHECK (ISJSON(metadata) = 1),
    CHECK ((line_type = 'discount' AND unit_price <= 0) OR (line_type <> 'discount' AND unit_price >= 0))
);

CREATE INDEX invoice_lines_invoice_id_idx ON [core].invoice_lines (invoice_id);

CREATE TABLE [core].payments (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    customer_id bigint NOT NULL REFERENCES [core].customer_profiles(user_id) ON DELETE NO ACTION,
    target_invoice_id bigint NOT NULL REFERENCES [core].invoices(id) ON DELETE NO ACTION,
    amount numeric(14, 2) NOT NULL CHECK (amount > 0),
    currency char(3) NOT NULL DEFAULT 'VND',
    method nvarchar(255) NOT NULL CHECK (method IN ('cash', 'bank_transfer', 'vnpay', 'payos', 'stripe', 'other')),
    provider nvarchar(255) NOT NULL,
    provider_transaction_id nvarchar(255),
    idempotency_key nvarchar(255) NOT NULL UNIQUE,
    status nvarchar(255) NOT NULL DEFAULT 'initiated'
        CHECK (status IN (
            'initiated', 'pending', 'succeeded', 'failed', 'cancelled',
            'partially_refunded', 'refunded'
        )),
    paid_at datetimeoffset(7),
    failure_reason nvarchar(255),
    metadata nvarchar(max) NOT NULL DEFAULT N'{}' CHECK (ISJSON(metadata) = 1),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (currency = upper(currency)),
    CHECK ((status = 'succeeded' AND paid_at IS NOT NULL) OR status <> 'succeeded')
);

CREATE UNIQUE INDEX payments_provider_transaction_uidx
    ON [core].payments (provider, provider_transaction_id)
    WHERE provider_transaction_id IS NOT NULL;
CREATE INDEX payments_customer_created_idx ON [core].payments (customer_id, created_at DESC);
CREATE INDEX payments_target_invoice_id_idx ON [core].payments (target_invoice_id);

CREATE TABLE [core].payment_allocations (
    payment_id bigint NOT NULL REFERENCES [core].payments(id) ON DELETE NO ACTION,
    invoice_id bigint NOT NULL REFERENCES [core].invoices(id) ON DELETE NO ACTION,
    allocated_amount numeric(14, 2) NOT NULL CHECK (allocated_amount > 0),
    allocated_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    PRIMARY KEY (payment_id, invoice_id)
);

CREATE INDEX payment_allocations_invoice_id_idx
    ON [core].payment_allocations (invoice_id, payment_id);

CREATE TABLE [core].refunds (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    payment_id bigint NOT NULL REFERENCES [core].payments(id) ON DELETE NO ACTION,
    agreement_id bigint REFERENCES [core].rental_agreements(id) ON DELETE NO ACTION,
    amount numeric(14, 2) NOT NULL CHECK (amount > 0),
    currency char(3) NOT NULL DEFAULT 'VND',
    reason nvarchar(255) NOT NULL,
    provider nvarchar(255) NOT NULL,
    provider_refund_id nvarchar(255),
    idempotency_key nvarchar(255) NOT NULL UNIQUE,
    status nvarchar(255) NOT NULL DEFAULT 'requested'
        CHECK (status IN ('requested', 'approved', 'processing', 'succeeded', 'failed', 'rejected')),
    requested_by bigint REFERENCES [core].users(id) ON DELETE NO ACTION,
    refunded_at datetimeoffset(7),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (currency = upper(currency)),
    CHECK ((status = 'succeeded' AND refunded_at IS NOT NULL) OR status <> 'succeeded')
);

CREATE UNIQUE INDEX refunds_provider_refund_uidx
    ON [core].refunds (provider, provider_refund_id)
    WHERE provider_refund_id IS NOT NULL;
CREATE INDEX refunds_payment_id_idx ON [core].refunds (payment_id);
CREATE INDEX refunds_agreement_id_idx ON [core].refunds (agreement_id)
    WHERE agreement_id IS NOT NULL;
CREATE INDEX refunds_requested_by_idx ON [core].refunds (requested_by);

CREATE TABLE [core].refund_approvals (
    refund_id bigint PRIMARY KEY REFERENCES [core].refunds(id) ON DELETE NO ACTION,
    decision nvarchar(255) NOT NULL CHECK (decision IN ('approved', 'rejected')),
    decided_by bigint NOT NULL CONSTRAINT FK_refund_approvals_decided_by_users REFERENCES [core].users(id) ON DELETE NO ACTION,
    reason nvarchar(255),
    decided_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX refund_approvals_decided_by_idx ON [core].refund_approvals (decided_by);

CREATE TABLE [core].promotion_redemptions (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    promotion_id bigint NOT NULL REFERENCES [core].promotions(id) ON DELETE NO ACTION,
    customer_id bigint NOT NULL REFERENCES [core].customer_profiles(user_id) ON DELETE NO ACTION,
    reservation_id bigint REFERENCES [core].reservations(id) ON DELETE NO ACTION,
    invoice_id bigint REFERENCES [core].invoices(id) ON DELETE NO ACTION,
    discount_amount numeric(14, 2) NOT NULL CHECK (discount_amount >= 0),
    status nvarchar(255) NOT NULL DEFAULT 'reserved' CHECK (status IN ('reserved', 'applied', 'released')),
    redeemed_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK ((CASE WHEN reservation_id IS NULL THEN 0 ELSE 1 END + CASE WHEN invoice_id IS NULL THEN 0 ELSE 1 END) >= 1)
);

CREATE INDEX promotion_redemptions_promotion_idx
    ON [core].promotion_redemptions (promotion_id, status, redeemed_at);
CREATE INDEX promotion_redemptions_customer_idx
    ON [core].promotion_redemptions (customer_id, promotion_id, status);
CREATE INDEX promotion_redemptions_reservation_id_idx ON [core].promotion_redemptions (reservation_id)
    WHERE reservation_id IS NOT NULL;
CREATE INDEX promotion_redemptions_invoice_id_idx ON [core].promotion_redemptions (invoice_id)
    WHERE invoice_id IS NOT NULL;
CREATE UNIQUE INDEX promotion_redemptions_reservation_uidx
    ON [core].promotion_redemptions (promotion_id, reservation_id)
    WHERE reservation_id IS NOT NULL AND status <> 'released';
CREATE UNIQUE INDEX promotion_redemptions_one_per_reservation_uidx
    ON [core].promotion_redemptions (reservation_id)
    WHERE reservation_id IS NOT NULL AND status <> 'released';
CREATE UNIQUE INDEX promotion_redemptions_one_per_invoice_uidx
    ON [core].promotion_redemptions (invoice_id)
    WHERE invoice_id IS NOT NULL AND status <> 'released';

CREATE TABLE [core].maintenance_work_orders (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    work_order_no nvarchar(255) NOT NULL UNIQUE,
    facility_id bigint NOT NULL REFERENCES [core].facilities(id) ON DELETE NO ACTION,
    storage_unit_id bigint REFERENCES [core].storage_units(id) ON DELETE NO ACTION,
    source_ticket_id bigint REFERENCES [core].support_tickets(id) ON DELETE NO ACTION,
    source_inspection_id bigint REFERENCES [core].inspections(id) ON DELETE NO ACTION,
    assigned_employee_id bigint REFERENCES [core].employee_profiles(user_id) ON DELETE NO ACTION,
    title nvarchar(255) NOT NULL,
    description nvarchar(255) NOT NULL,
    priority nvarchar(255) NOT NULL DEFAULT 'normal' CHECK (priority IN ('low', 'normal', 'high', 'urgent')),
    blocks_booking bit NOT NULL DEFAULT 1,
    status nvarchar(255) NOT NULL DEFAULT 'open' CHECK (status IN (
        'open', 'assigned', 'in_progress', 'on_hold', 'awaiting_verification', 'completed', 'cancelled'
    )),
    estimated_cost numeric(14, 2) CHECK (estimated_cost IS NULL OR estimated_cost >= 0),
    actual_cost numeric(14, 2) CHECK (actual_cost IS NULL OR actual_cost >= 0),
    opened_by bigint NOT NULL REFERENCES [core].users(id) ON DELETE NO ACTION,
    verified_by bigint REFERENCES [core].employee_profiles(user_id) ON DELETE NO ACTION,
    started_at datetimeoffset(7),
    completed_at datetimeoffset(7),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (completed_at IS NULL OR started_at IS NOT NULL),
    CHECK (completed_at IS NULL OR completed_at >= started_at),
    CHECK ((status = 'completed' AND completed_at IS NOT NULL) OR status <> 'completed')
);

CREATE INDEX maintenance_work_orders_facility_queue_idx
    ON [core].maintenance_work_orders (facility_id, status, priority, created_at);
CREATE INDEX maintenance_work_orders_storage_unit_id_idx
    ON [core].maintenance_work_orders (storage_unit_id, status)
    WHERE storage_unit_id IS NOT NULL;
CREATE INDEX maintenance_work_orders_source_ticket_id_idx ON [core].maintenance_work_orders (source_ticket_id)
    WHERE source_ticket_id IS NOT NULL;
CREATE INDEX maintenance_work_orders_source_inspection_id_idx ON [core].maintenance_work_orders (source_inspection_id)
    WHERE source_inspection_id IS NOT NULL;
CREATE INDEX maintenance_work_orders_assigned_employee_id_idx ON [core].maintenance_work_orders (assigned_employee_id)
    WHERE assigned_employee_id IS NOT NULL;
CREATE INDEX maintenance_work_orders_opened_by_idx ON [core].maintenance_work_orders (opened_by);
CREATE INDEX maintenance_work_orders_verified_by_idx ON [core].maintenance_work_orders (verified_by)
    WHERE verified_by IS NOT NULL;

CREATE TABLE [core].staff_tasks (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    facility_id bigint NOT NULL REFERENCES [core].facilities(id) ON DELETE NO ACTION,
    shift_id bigint REFERENCES [core].staff_shifts(id) ON DELETE NO ACTION,
    assigned_employee_id bigint REFERENCES [core].employee_profiles(user_id) ON DELETE NO ACTION,
    ticket_id bigint REFERENCES [core].support_tickets(id) ON DELETE NO ACTION,
    maintenance_work_order_id bigint REFERENCES [core].maintenance_work_orders(id) ON DELETE NO ACTION,
    task_type nvarchar(255) NOT NULL CHECK (task_type IN ('check_in', 'check_out', 'inspection', 'maintenance', 'support', 'other')),
    title nvarchar(255) NOT NULL,
    due_at datetimeoffset(7),
    status nvarchar(255) NOT NULL DEFAULT 'todo' CHECK (status IN ('todo', 'in_progress', 'blocked', 'done', 'cancelled')),
    progress_percent smallint NOT NULL DEFAULT 0 CHECK (progress_percent BETWEEN 0 AND 100),
    created_by bigint NOT NULL REFERENCES [core].users(id) ON DELETE NO ACTION,
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK ((status = 'done' AND progress_percent = 100) OR status <> 'done')
);

CREATE INDEX staff_tasks_facility_queue_idx
    ON [core].staff_tasks (facility_id, status, due_at);
CREATE INDEX staff_tasks_shift_id_idx ON [core].staff_tasks (shift_id)
    WHERE shift_id IS NOT NULL;
CREATE INDEX staff_tasks_assigned_employee_id_idx
    ON [core].staff_tasks (assigned_employee_id, status, due_at)
    WHERE assigned_employee_id IS NOT NULL;
CREATE INDEX staff_tasks_ticket_id_idx ON [core].staff_tasks (ticket_id)
    WHERE ticket_id IS NOT NULL;
CREATE INDEX staff_tasks_maintenance_id_idx ON [core].staff_tasks (maintenance_work_order_id)
    WHERE maintenance_work_order_id IS NOT NULL;
CREATE INDEX staff_tasks_created_by_idx ON [core].staff_tasks (created_by);

CREATE TABLE [core].access_points (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    facility_id bigint NOT NULL REFERENCES [core].facilities(id) ON DELETE NO ACTION,
    code nvarchar(255) NOT NULL,
    name nvarchar(255) NOT NULL,
    access_point_type nvarchar(255) NOT NULL CHECK (access_point_type IN ('gate', 'building_door', 'floor_door', 'unit_door')),
    external_device_code nvarchar(255),
    status nvarchar(255) NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'offline', 'maintenance', 'retired')),
    UNIQUE (facility_id, code)
);

CREATE INDEX access_points_facility_id_idx ON [core].access_points (facility_id, status);

CREATE TABLE [core].access_credentials (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    agreement_id bigint NOT NULL REFERENCES [core].rental_agreements(id) ON DELETE NO ACTION,
    authorized_member_id bigint REFERENCES [core].authorized_access_members(id) ON DELETE NO ACTION,
    credential_type nvarchar(255) NOT NULL CHECK (credential_type IN ('pin', 'card', 'qr', 'key', 'mobile')),
    external_secret_ref nvarchar(255),
    secret_digest nvarchar(255),
    display_hint nvarchar(255),
    issued_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    expires_at datetimeoffset(7),
    status nvarchar(255) NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'active', 'suspended', 'revoked', 'expired')),
    issued_by bigint REFERENCES [core].employee_profiles(user_id) ON DELETE NO ACTION,
    revoked_at datetimeoffset(7),
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (expires_at IS NULL OR expires_at > issued_at),
    CHECK ((status = 'revoked' AND revoked_at IS NOT NULL) OR status <> 'revoked')
);

CREATE INDEX access_credentials_agreement_idx
    ON [core].access_credentials (agreement_id, status);
CREATE INDEX access_credentials_authorized_member_id_idx ON [core].access_credentials (authorized_member_id)
    WHERE authorized_member_id IS NOT NULL;
CREATE INDEX access_credentials_issued_by_idx ON [core].access_credentials (issued_by)
    WHERE issued_by IS NOT NULL;

CREATE TABLE [core].access_events (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    credential_id bigint REFERENCES [core].access_credentials(id) ON DELETE NO ACTION,
    access_point_id bigint NOT NULL REFERENCES [core].access_points(id) ON DELETE NO ACTION,
    occurred_at datetimeoffset(7) NOT NULL,
    result nvarchar(255) NOT NULL CHECK (result IN ('granted', 'denied', 'error')),
    reason nvarchar(255),
    external_event_id nvarchar(255) UNIQUE,
    metadata nvarchar(max) NOT NULL DEFAULT N'{}' CHECK (ISJSON(metadata) = 1)
);

CREATE INDEX access_events_credential_occurred_idx
    ON [core].access_events (credential_id, occurred_at DESC)
    WHERE credential_id IS NOT NULL;
CREATE INDEX access_events_point_occurred_idx
    ON [core].access_events (access_point_id, occurred_at DESC);

CREATE TABLE [core].delinquency_cases (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    agreement_id bigint NOT NULL REFERENCES [core].rental_agreements(id) ON DELETE NO ACTION,
    invoice_id bigint NOT NULL REFERENCES [core].invoices(id) ON DELETE NO ACTION,
    status nvarchar(255) NOT NULL DEFAULT 'grace'
        CHECK (status IN ('grace', 'delinquent', 'cured', 'waived', 'termination_started')),
    opened_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    grace_ends_at datetimeoffset(7) NOT NULL,
    outstanding_snapshot numeric(14, 2) NOT NULL CHECK (outstanding_snapshot > 0),
    resolved_at datetimeoffset(7),
    notes nvarchar(255),
    CHECK (grace_ends_at >= opened_at),
    CHECK (resolved_at IS NULL OR resolved_at >= opened_at)
);

CREATE INDEX delinquency_cases_agreement_idx
    ON [core].delinquency_cases (agreement_id, status, opened_at DESC);
CREATE INDEX delinquency_cases_invoice_id_idx ON [core].delinquency_cases (invoice_id);
CREATE UNIQUE INDEX delinquency_cases_one_open_invoice_uidx
    ON [core].delinquency_cases (invoice_id)
    WHERE resolved_at IS NULL;

CREATE TABLE [core].delinquency_actions (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    delinquency_case_id bigint NOT NULL REFERENCES [core].delinquency_cases(id) ON DELETE NO ACTION,
    action_type nvarchar(255) NOT NULL CHECK (action_type IN (
        'reminder_sent', 'late_fee_added', 'credential_suspended',
        'customer_contacted', 'payment_plan', 'waived', 'termination_started', 'note'
    )),
    performed_by bigint REFERENCES [core].users(id) ON DELETE NO ACTION,
    details nvarchar(max) NOT NULL DEFAULT N'{}' CHECK (ISJSON(details) = 1),
    occurred_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX delinquency_actions_case_occurred_idx
    ON [core].delinquency_actions (delinquency_case_id, occurred_at);
CREATE INDEX delinquency_actions_performed_by_idx ON [core].delinquency_actions (performed_by)
    WHERE performed_by IS NOT NULL;

CREATE TABLE [core].notifications (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    user_id bigint NOT NULL REFERENCES [core].users(id) ON DELETE NO ACTION,
    channel nvarchar(255) NOT NULL CHECK (channel IN ('in_app', 'email', 'sms', 'push')),
    template_code nvarchar(255) NOT NULL,
    subject nvarchar(255),
    payload nvarchar(max) NOT NULL DEFAULT N'{}' CHECK (ISJSON(payload) = 1),
    status nvarchar(255) NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'processing', 'sent', 'failed', 'cancelled')),
    scheduled_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    sent_at datetimeoffset(7),
    attempts smallint NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    last_error nvarchar(255),
    deduplication_key nvarchar(255) UNIQUE,
    created_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX notifications_delivery_queue_idx
    ON [core].notifications (scheduled_at, id)
    WHERE status IN ('pending', 'failed');
CREATE INDEX notifications_user_created_idx ON [core].notifications (user_id, created_at DESC);

CREATE TABLE [core].audit_logs (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    actor_user_id bigint REFERENCES [core].users(id) ON DELETE NO ACTION,
    actor_role nvarchar(255),
    entity_schema nvarchar(255) NOT NULL DEFAULT 'core',
    entity_type nvarchar(255) NOT NULL,
    entity_id nvarchar(255) NOT NULL,
    action nvarchar(255) NOT NULL,
    old_values nvarchar(max),
    new_values nvarchar(max),
    request_id nvarchar(255),
    ip_address nvarchar(45),
    occurred_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX audit_logs_entity_idx
    ON [core].audit_logs (entity_type, entity_id, occurred_at DESC);
CREATE INDEX audit_logs_actor_idx
    ON [core].audit_logs (actor_user_id, occurred_at DESC)
    WHERE actor_user_id IS NOT NULL;
CREATE INDEX audit_logs_request_id_idx ON [core].audit_logs (request_id)
    WHERE request_id IS NOT NULL;

CREATE TABLE [core].login_history (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    user_id bigint REFERENCES [core].users(id) ON DELETE NO ACTION,
    attempted_email nvarchar(255),
    result nvarchar(255) NOT NULL CHECK (result IN ('succeeded', 'failed', 'locked', 'blocked')),
    ip_address nvarchar(45),
    user_agent nvarchar(255),
    occurred_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE INDEX login_history_user_occurred_idx
    ON [core].login_history (user_id, occurred_at DESC)
    WHERE user_id IS NOT NULL;
CREATE INDEX login_history_email_occurred_idx
    ON [core].login_history (attempted_email, occurred_at DESC)
    WHERE attempted_email IS NOT NULL;

CREATE TABLE [core].integration_events (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    source nvarchar(255) NOT NULL,
    external_event_id nvarchar(255) NOT NULL,
    event_type nvarchar(255) NOT NULL,
    payload nvarchar(max) NOT NULL CHECK (ISJSON(payload) = 1),
    status nvarchar(255) NOT NULL DEFAULT 'received' CHECK (status IN ('received', 'processed', 'failed', 'ignored')),
    error_message nvarchar(255),
    received_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    processed_at datetimeoffset(7),
    UNIQUE (source, external_event_id)
);

CREATE INDEX integration_events_processing_queue_idx
    ON [core].integration_events (received_at, id)
    WHERE status IN ('received', 'failed');




/* ===== 02_BusinessRules_Corrected.sql =====
   SQL Server equivalents for PostgreSQL validation/exclusion rules + SRS rules.
   The API/service layer still owns authorization/JWT; the database enforces
   cross-table integrity and financial invariants.
*/
USE [SelfStoragePRN222];
GO

/* ---------- Non-overlap rules ---------- */
CREATE OR ALTER TRIGGER [core].[trg_staff_facility_assignment_no_overlap]
ON [core].[staff_facility_assignments]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM inserted i
        JOIN [core].[staff_facility_assignments] x WITH (UPDLOCK, HOLDLOCK)
          ON x.id <> i.id
         AND x.employee_id = i.employee_id
         AND x.facility_id = i.facility_id
         AND x.assignment_role = i.assignment_role
         AND i.starts_at < COALESCE(x.ends_at, CONVERT(datetimeoffset(7), '9999-12-31 23:59:59 +00:00'))
         AND COALESCE(i.ends_at, CONVERT(datetimeoffset(7), '9999-12-31 23:59:59 +00:00')) > x.starts_at
    )
    BEGIN
        THROW 51001, 'An employee cannot have overlapping assignments for the same facility and role.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_price_range_no_overlap]
ON [core].[price_ranges]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        JOIN [core].[price_ranges] x WITH (UPDLOCK, HOLDLOCK)
          ON x.id <> i.id AND x.unit_type_id = i.unit_type_id
         AND i.valid_from < COALESCE(x.valid_to, CONVERT(date, '9999-12-31'))
         AND COALESCE(i.valid_to, CONVERT(date, '9999-12-31')) > x.valid_from
    )
    BEGIN
        THROW 51002, 'Price ranges for a unit type may not overlap.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_facility_rate_no_overlap]
ON [core].[facility_rates]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        JOIN [core].[facility_rates] x WITH (UPDLOCK, HOLDLOCK)
          ON x.id <> i.id AND x.facility_id = i.facility_id AND x.unit_type_id = i.unit_type_id
         AND i.valid_from < COALESCE(x.valid_to, CONVERT(date, '9999-12-31'))
         AND COALESCE(i.valid_to, CONVERT(date, '9999-12-31')) > x.valid_from
    )
    BEGIN
        THROW 51003, 'Facility rates may not overlap for a facility/unit type.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_policy_version_no_overlap]
ON [core].[policy_versions]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        JOIN [core].[policy_versions] x WITH (UPDLOCK, HOLDLOCK)
          ON x.id <> i.id AND x.policy_type = i.policy_type
         AND i.valid_from < COALESCE(x.valid_to, CONVERT(date, '9999-12-31'))
         AND COALESCE(i.valid_to, CONVERT(date, '9999-12-31')) > x.valid_from
    )
    BEGIN
        THROW 51004, 'Policy versions of the same type may not overlap.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_fee_rule_no_overlap]
ON [core].[fee_rules]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        JOIN [core].[fee_rules] x WITH (UPDLOCK, HOLDLOCK)
          ON x.id <> i.id
         AND ISNULL(x.facility_id, 0) = ISNULL(i.facility_id, 0)
         AND x.code = i.code
         AND i.valid_from < COALESCE(x.valid_to, CONVERT(date, '9999-12-31'))
         AND COALESCE(i.valid_to, CONVERT(date, '9999-12-31')) > x.valid_from
    )
    BEGIN
        THROW 51005, 'Fee-rule versions may not overlap for the same scope and code.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_unit_allocation_no_overlap]
ON [core].[unit_allocations]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        JOIN [core].[unit_allocations] x WITH (UPDLOCK, HOLDLOCK)
          ON x.id <> i.id
         AND i.status = 'active' AND x.status = 'active'
         AND x.storage_unit_id = i.storage_unit_id
         AND i.allocation_start_date < x.allocation_end_date
         AND i.allocation_end_date > x.allocation_start_date
    )
    BEGIN
        THROW 51006, 'An active storage-unit allocation may not overlap another active allocation.', 1;
    END;

    IF EXISTS (
        SELECT 1 FROM inserted i
        JOIN [core].[unit_allocations] x WITH (UPDLOCK, HOLDLOCK)
          ON x.id <> i.id
         AND i.status = 'active' AND x.status = 'active'
         AND i.agreement_id IS NOT NULL AND x.agreement_id = i.agreement_id
         AND i.allocation_start_date < x.allocation_end_date
         AND i.allocation_end_date > x.allocation_start_date
    )
    BEGIN
        THROW 51007, 'An agreement may not have overlapping active unit allocations.', 1;
    END
END;
GO

/* ---------- Cross-table scope/integrity ---------- */
CREATE OR ALTER TRIGGER [core].[trg_storage_units_validate_area]
ON [core].[storage_units]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM inserted i
        LEFT JOIN [core].[facility_areas] a ON a.id = i.area_id AND a.facility_id = i.facility_id
        WHERE i.area_id IS NOT NULL AND a.id IS NULL
    )
    BEGIN
        THROW 51020, 'Unit area must belong to the same facility.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_unit_map_positions_validate_scope]
ON [core].[unit_map_positions]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM inserted i
        JOIN [core].[storage_units] su ON su.id = i.unit_id
        JOIN [core].[facility_areas] fa ON fa.id = i.area_id
        WHERE su.facility_id <> fa.facility_id
    )
    BEGIN
        THROW 51021, 'Map area and storage unit must belong to the same facility.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_facility_rates_validate_price_range]
ON [core].[facility_rates]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM inserted i
        WHERE NOT EXISTS (
            SELECT 1 FROM [core].[price_ranges] pr
            WHERE pr.unit_type_id = i.unit_type_id
              AND pr.valid_from <= i.valid_from
              AND (pr.valid_to IS NULL OR i.valid_from < pr.valid_to)
              AND i.monthly_rate BETWEEN pr.min_monthly_rate AND pr.max_monthly_rate
        )
    )
    BEGIN
        THROW 51022, 'Facility monthly rate is outside the active corporate price range.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_reservations_validate_rate]
ON [core].[reservations]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM inserted i
        LEFT JOIN [core].[facility_rates] fr ON fr.id = i.facility_rate_id
        WHERE fr.id IS NULL
           OR fr.facility_id <> i.facility_id
           OR fr.unit_type_id <> i.unit_type_id
           OR fr.valid_from > i.start_date
           OR (fr.valid_to IS NOT NULL AND i.start_date >= fr.valid_to)
           OR fr.monthly_rate <> i.monthly_rate_snapshot
           OR fr.deposit_amount <> i.deposit_snapshot
           OR fr.booking_fee <> i.booking_fee_snapshot
    )
    BEGIN
        THROW 51023, 'Reservation rate/snapshots must match facility, unit type and start date.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_rental_agreements_validate_reservation]
ON [core].[rental_agreements]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM inserted i
        LEFT JOIN [core].[reservations] r ON r.id = i.reservation_id
        WHERE r.id IS NULL
           OR r.customer_id <> i.customer_id
           OR r.facility_id <> i.facility_id
           OR r.start_date <> i.start_date
           OR r.monthly_rate_snapshot <> i.monthly_rate_snapshot
           OR r.deposit_snapshot <> i.deposit_snapshot
    )
    BEGIN
        THROW 51024, 'Agreement identity and price snapshots must match its reservation.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_unit_allocations_validate_scope]
ON [core].[unit_allocations]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        JOIN [core].[storage_units] su ON su.id = i.storage_unit_id
        WHERE i.status = 'active'
          AND (su.is_listed = 0 OR su.physical_status IN ('occupied','maintenance','out_of_service','pending_inspection'))
    )
    BEGIN
        THROW 51025, 'Storage unit is not bookable.', 1;
    END;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        JOIN [core].[storage_units] su ON su.id = i.storage_unit_id
        JOIN [core].[reservations] r ON r.id = i.reservation_id
        WHERE i.status = 'active' AND i.reservation_id IS NOT NULL
          AND (su.facility_id <> r.facility_id
               OR su.unit_type_id <> r.unit_type_id
               OR i.allocation_start_date <> r.start_date
               OR i.allocation_end_date <> r.end_date)
    )
    BEGIN
        THROW 51026, 'Reservation allocation must match facility, unit type and rental period.', 1;
    END;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        JOIN [core].[storage_units] su ON su.id = i.storage_unit_id
        JOIN [core].[rental_agreements] a ON a.id = i.agreement_id
        JOIN [core].[reservations] r ON r.id = a.reservation_id
        WHERE i.status = 'active' AND i.agreement_id IS NOT NULL
          AND (su.facility_id <> a.facility_id
               OR i.allocation_start_date < a.start_date
               OR i.allocation_end_date > a.end_date
               OR (i.allocation_kind = 'rental' AND su.unit_type_id <> r.unit_type_id))
    )
    BEGIN
        THROW 51027, 'Agreement allocation must match facility, allowed unit type and agreement period.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_appointments_validate_scope]
ON [core].[appointments]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        LEFT JOIN [core].[reservations] r ON r.id = i.reservation_id
        LEFT JOIN [core].[rental_agreements] a ON a.id = i.agreement_id
        WHERE COALESCE(r.facility_id, a.facility_id) <> i.facility_id
    )
    BEGIN
        THROW 51028, 'Appointment must use its reservation/agreement facility.', 1;
    END;

    IF EXISTS (
        SELECT 1 FROM inserted i
        WHERE i.assigned_staff_id IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM [core].[staff_facility_assignments] sfa
              WHERE sfa.employee_id = i.assigned_staff_id
                AND sfa.facility_id = i.facility_id
                AND sfa.starts_at <= i.starts_at
                AND (sfa.ends_at IS NULL OR i.starts_at < sfa.ends_at)
          )
    )
    BEGIN
        THROW 51029, 'Assigned staff is not assigned to this facility at appointment time.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_inspections_validate_scope]
ON [core].[inspections]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [core].[storage_units] su ON su.id=i.storage_unit_id WHERE i.storage_unit_id IS NOT NULL AND su.facility_id<>i.facility_id)
       OR EXISTS (SELECT 1 FROM inserted i JOIN [core].[reservations] r ON r.id=i.reservation_id WHERE i.reservation_id IS NOT NULL AND r.facility_id<>i.facility_id)
       OR EXISTS (SELECT 1 FROM inserted i JOIN [core].[rental_agreements] a ON a.id=i.agreement_id WHERE i.agreement_id IS NOT NULL AND a.facility_id<>i.facility_id)
    BEGIN
        THROW 51030, 'Inspection references must belong to the same facility.', 1;
    END;

    IF EXISTS (
        SELECT 1 FROM inserted i
        WHERE NOT EXISTS (
            SELECT 1 FROM [core].[staff_facility_assignments] sfa
            WHERE sfa.employee_id=i.inspected_by AND sfa.facility_id=i.facility_id
              AND sfa.starts_at <= COALESCE(i.inspected_at,i.created_at)
              AND (sfa.ends_at IS NULL OR COALESCE(i.inspected_at,i.created_at) < sfa.ends_at)
        )
    )
    BEGIN
        THROW 51031, 'Inspector is not assigned to this facility.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_handover_records_validate_scope]
ON [core].[handover_records]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        WHERE NOT EXISTS (
            SELECT 1
            FROM [core].[unit_allocations] ua
            JOIN [core].[inspections] ins ON ins.id=i.inspection_id
            WHERE ua.id=i.unit_allocation_id
              AND ua.agreement_id=i.agreement_id
              AND ins.agreement_id=i.agreement_id
              AND ins.storage_unit_id=ua.storage_unit_id
        )
    )
    BEGIN
        THROW 51032, 'Handover allocation and inspection must belong to the same agreement/unit.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_support_tickets_validate_scope]
ON [core].[support_tickets]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        WHERE i.storage_unit_id IS NOT NULL AND i.agreement_id IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM [core].[unit_allocations] ua
              WHERE ua.agreement_id=i.agreement_id AND ua.storage_unit_id=i.storage_unit_id
          )
    )
    BEGIN
        THROW 51033, 'Ticket unit must match the ticket agreement.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_ticket_assignments_validate_scope]
ON [core].[ticket_assignments]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        JOIN [core].[support_tickets] t ON t.id=i.ticket_id
        WHERE NOT EXISTS (
            SELECT 1 FROM [core].[staff_facility_assignments] sfa
            WHERE sfa.employee_id=i.employee_id AND sfa.facility_id=t.facility_id
              AND sfa.starts_at <= i.assigned_at
              AND (sfa.ends_at IS NULL OR i.assigned_at < sfa.ends_at)
        )
    )
    BEGIN
        THROW 51034, 'Ticket assignee is not assigned to the ticket facility.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_shift_assignments_validate_scope]
ON [core].[shift_assignments]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        JOIN [core].[staff_shifts] s ON s.id=i.shift_id
        JOIN [core].[employee_profiles] ep ON ep.user_id=i.employee_id
        WHERE ep.employment_status <> 'active'
           OR NOT EXISTS (
                SELECT 1 FROM [core].[staff_facility_assignments] sfa
                WHERE sfa.employee_id=i.employee_id AND sfa.facility_id=s.facility_id
                  AND sfa.starts_at <= s.starts_at
                  AND (sfa.ends_at IS NULL OR s.starts_at < sfa.ends_at)
           )
    )
    BEGIN
        THROW 51035, 'Shift assignee must be active and assigned to the shift facility.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_staff_tasks_validate_scope]
ON [core].[staff_tasks]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [core].[staff_shifts] s ON s.id=i.shift_id WHERE i.shift_id IS NOT NULL AND s.facility_id<>i.facility_id)
       OR EXISTS (SELECT 1 FROM inserted i JOIN [core].[support_tickets] t ON t.id=i.ticket_id WHERE i.ticket_id IS NOT NULL AND t.facility_id<>i.facility_id)
       OR EXISTS (SELECT 1 FROM inserted i JOIN [core].[maintenance_work_orders] m ON m.id=i.maintenance_work_order_id WHERE i.maintenance_work_order_id IS NOT NULL AND m.facility_id<>i.facility_id)
    BEGIN
        THROW 51036, 'Task references must belong to the task facility.', 1;
    END;

    IF EXISTS (
        SELECT 1 FROM inserted i
        WHERE i.assigned_employee_id IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM [core].[staff_facility_assignments] sfa
              WHERE sfa.employee_id=i.assigned_employee_id AND sfa.facility_id=i.facility_id
                AND sfa.starts_at <= i.created_at
                AND (sfa.ends_at IS NULL OR i.created_at < sfa.ends_at)
          )
    )
    BEGIN
        THROW 51037, 'Task employee must be assigned to the task facility.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_maintenance_work_orders_validate_scope]
ON [core].[maintenance_work_orders]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [core].[storage_units] su ON su.id=i.storage_unit_id WHERE i.storage_unit_id IS NOT NULL AND su.facility_id<>i.facility_id)
       OR EXISTS (SELECT 1 FROM inserted i JOIN [core].[support_tickets] t ON t.id=i.source_ticket_id WHERE i.source_ticket_id IS NOT NULL AND t.facility_id<>i.facility_id)
       OR EXISTS (SELECT 1 FROM inserted i JOIN [core].[inspections] ins ON ins.id=i.source_inspection_id WHERE i.source_inspection_id IS NOT NULL AND ins.facility_id<>i.facility_id)
    BEGIN
        THROW 51038, 'Maintenance references must belong to the maintenance facility.', 1;
    END;

    IF EXISTS (
        SELECT 1 FROM inserted i
        WHERE i.assigned_employee_id IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM [core].[staff_facility_assignments] sfa
              JOIN [core].[employee_profiles] ep ON ep.user_id=sfa.employee_id AND ep.employment_status='active'
              WHERE sfa.employee_id=i.assigned_employee_id AND sfa.facility_id=i.facility_id
                AND sfa.starts_at <= i.created_at
                AND (sfa.ends_at IS NULL OR i.created_at < sfa.ends_at)
          )
    )
    BEGIN
        THROW 51039, 'Maintenance assignee is not assigned to this facility.', 1;
    END;

    IF EXISTS (
        SELECT 1 FROM inserted i
        WHERE i.verified_by IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM [core].[staff_facility_assignments] sfa
              WHERE sfa.employee_id=i.verified_by AND sfa.facility_id=i.facility_id
                AND sfa.assignment_role='facility_manager'
                AND sfa.starts_at <= COALESCE(i.completed_at,SYSUTCDATETIME())
                AND (sfa.ends_at IS NULL OR COALESCE(i.completed_at,SYSUTCDATETIME()) < sfa.ends_at)
          )
    )
    BEGIN
        THROW 51040, 'Maintenance verifier must be a manager assigned to this facility.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_access_credentials_validate_scope]
ON [core].[access_credentials]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        JOIN [core].[authorized_access_members] m ON m.id=i.authorized_member_id
        WHERE i.authorized_member_id IS NOT NULL AND m.agreement_id<>i.agreement_id
    )
    BEGIN
        THROW 51041, 'Credential member must belong to the same agreement.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_service_ratings_validate_scope]
ON [core].[service_ratings]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        LEFT JOIN [core].[support_tickets] t ON t.id=i.ticket_id
        WHERE t.id IS NULL OR t.customer_id<>i.customer_id OR t.status NOT IN ('resolved','closed')
    )
    BEGIN
        THROW 51042, 'Only the ticket owner can rate a resolved or closed ticket.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_invoices_validate_scope]
ON [core].[invoices]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [core].[reservations] r ON r.id=i.reservation_id WHERE i.reservation_id IS NOT NULL AND r.customer_id<>i.customer_id)
       OR EXISTS (
            SELECT 1 FROM inserted i JOIN [core].[rental_agreements] a ON a.id=i.agreement_id
            WHERE i.agreement_id IS NOT NULL
              AND (a.customer_id<>i.customer_id OR (i.reservation_id IS NOT NULL AND a.reservation_id<>i.reservation_id))
       )
       OR EXISTS (
            SELECT 1 FROM inserted i
            JOIN [core].[ticket_charge_proposals] p ON p.id=i.ticket_charge_proposal_id
            JOIN [core].[support_tickets] t ON t.id=p.ticket_id
            WHERE i.ticket_charge_proposal_id IS NOT NULL AND t.customer_id<>i.customer_id
       )
    BEGIN
        THROW 51043, 'Invoice references must belong to the invoice customer.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_payments_validate_target]
ON [core].[payments]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        JOIN [core].[invoices] inv ON inv.id=i.target_invoice_id
        WHERE inv.customer_id<>i.customer_id
    )
    BEGIN
        THROW 51044, 'Payment target invoice must belong to payment customer.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_refunds_guard]
ON [core].[refunds]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        JOIN [core].[payments] p ON p.id=i.payment_id
        WHERE p.status NOT IN ('succeeded','partially_refunded','refunded')
    )
    BEGIN
        THROW 51045, 'Only settled payments can be refunded.', 1;
    END;

    IF EXISTS (
        SELECT 1 FROM inserted i
        WHERE i.agreement_id IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM [core].[payments] p
              JOIN [core].[invoices] inv ON inv.id=p.target_invoice_id
              JOIN [core].[rental_agreements] a ON a.id=i.agreement_id
              WHERE p.id=i.payment_id AND (inv.agreement_id=a.id OR inv.reservation_id=a.reservation_id)
          )
    )
    BEGIN
        THROW 51046, 'Refund agreement must match the source payment invoice.', 1;
    END;

    IF EXISTS (
        SELECT p.id
        FROM [core].[payments] p
        JOIN (SELECT DISTINCT payment_id FROM inserted) a ON a.payment_id=p.id
        CROSS APPLY (
            SELECT COALESCE(SUM(r.amount),0) total_refunds
            FROM [core].[refunds] r
            WHERE r.payment_id=p.id AND r.status NOT IN ('failed','rejected')
        ) x
        WHERE x.total_refunds > p.amount
    )
    BEGIN
        THROW 51047, 'Refunds exceed original payment amount.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_delinquency_cases_validate_scope]
ON [core].[delinquency_cases]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        JOIN [core].[invoices] inv ON inv.id=i.invoice_id
        WHERE inv.agreement_id<>i.agreement_id
    )
    BEGIN
        THROW 51048, 'Delinquency invoice must belong to the delinquent agreement.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_promotion_redemptions_validate_scope]
ON [core].[promotion_redemptions]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        JOIN [core].[reservations] r ON r.id=i.reservation_id
        WHERE i.reservation_id IS NOT NULL AND r.customer_id<>i.customer_id
    )
       OR EXISTS (
        SELECT 1 FROM inserted i
        JOIN [core].[invoices] inv ON inv.id=i.invoice_id
        WHERE i.invoice_id IS NOT NULL
          AND (inv.customer_id<>i.customer_id OR (i.reservation_id IS NOT NULL AND inv.reservation_id<>i.reservation_id))
    )
    BEGIN
        THROW 51049, 'Promotion redemption must match customer/reservation/invoice scope.', 1;
    END
END;
GO

/* ---------- State transitions ---------- */
CREATE OR ALTER TRIGGER [core].[trg_storage_unit_status_transition]
ON [core].[storage_units]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(physical_status) AND EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id=i.id
        WHERE i.physical_status<>d.physical_status
          AND NOT (
              (d.physical_status='available' AND i.physical_status IN ('reserved','maintenance','out_of_service')) OR
              (d.physical_status='reserved' AND i.physical_status IN ('available','occupied','maintenance','out_of_service')) OR
              (d.physical_status='occupied' AND i.physical_status IN ('pending_inspection','maintenance')) OR
              (d.physical_status='pending_inspection' AND i.physical_status IN ('available','maintenance','out_of_service')) OR
              (d.physical_status='maintenance' AND i.physical_status IN ('available','out_of_service')) OR
              (d.physical_status='out_of_service' AND i.physical_status IN ('maintenance','available'))
          )
    )
    BEGIN
        THROW 51050, 'Invalid storage-unit status transition.', 1;
    END

    INSERT INTO [core].[unit_status_history](storage_unit_id, old_status, new_status, reason, changed_by)
    SELECT i.id,d.physical_status,i.physical_status,N'status_transition',NULL
    FROM inserted i JOIN deleted d ON d.id=i.id
    WHERE i.physical_status<>d.physical_status;
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_reservation_status_transition]
ON [core].[reservations]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(status) AND EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id=i.id
        WHERE i.status<>d.status
          AND NOT (
              (d.status='pending' AND i.status IN ('awaiting_deposit','confirmed','cancelled','expired')) OR
              (d.status='awaiting_deposit' AND i.status IN ('confirmed','cancelled','expired')) OR
              (d.status='confirmed' AND i.status IN ('checked_in','converted','cancelled','expired','no_show')) OR
              (d.status='checked_in' AND i.status IN ('converted','completed')) OR
              (d.status='converted' AND i.status='completed')
          )
    )
    BEGIN
        THROW 51051, 'Invalid reservation status transition.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_agreement_status_transition]
ON [core].[rental_agreements]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(status) AND EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id=i.id
        WHERE i.status<>d.status
          AND NOT (
              (d.status='draft' AND i.status IN ('scheduled','cancelled')) OR
              (d.status='scheduled' AND i.status IN ('active','cancelled')) OR
              (d.status='active' AND i.status IN ('extended','overdue','expired','terminated','move_out_scheduled','cancelled')) OR
              (d.status='extended' AND i.status IN ('active','overdue','expired','move_out_scheduled')) OR
              (d.status='overdue' AND i.status IN ('active','defaulted','terminated','move_out_scheduled')) OR
              (d.status='defaulted' AND i.status IN ('terminated','completed','closed')) OR
              (d.status='move_out_scheduled' AND i.status IN ('checkout_pending','active')) OR
              (d.status='checkout_pending' AND i.status IN ('active','completed','closed')) OR
              (d.status='expired' AND i.status IN ('extended','completed','closed')) OR
              (d.status='terminated' AND i.status IN ('completed','closed')) OR
              (d.status='completed' AND i.status='closed')
          )
    )
    BEGIN
        THROW 51052, 'Invalid rental-agreement status transition.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_invoice_status_transition]
ON [core].[invoices]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(status) AND EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id=i.id
        WHERE i.status<>d.status
          AND NOT (
              (d.status='draft' AND i.status IN ('open','voided')) OR
              (d.status='open' AND i.status IN ('partially_paid','paid','overdue','voided')) OR
              (d.status='partially_paid' AND i.status IN ('open','paid','overdue','voided')) OR
              (d.status='overdue' AND i.status IN ('partially_paid','paid','voided'))
          )
    )
    BEGIN
        THROW 51053, 'Invalid invoice status transition.', 1;
    END
END;
GO

/* ---------- Invoice/payment invariants ---------- */
CREATE OR ALTER TRIGGER [core].[trg_invoice_lines_guard]
ON [core].[invoice_lines]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM (
            SELECT invoice_id FROM inserted
            UNION
            SELECT invoice_id FROM deleted
        ) a
        JOIN [core].[invoices] inv ON inv.id=a.invoice_id
        WHERE (inv.status NOT IN ('draft','open') OR inv.paid_amount>0)
          AND NOT (
              inv.status IN ('open','partially_paid','overdue')
              AND NOT EXISTS (SELECT 1 FROM deleted d WHERE d.invoice_id=inv.id)
              AND NOT EXISTS (SELECT 1 FROM inserted x WHERE x.invoice_id=inv.id AND x.line_type<>'late_fee')
          )
    )
    BEGIN
        THROW 51054, 'Paid/voided invoice lines are immutable; overdue invoices accept late-fee inserts only.', 1;
    END
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_invoice_lines_refresh_totals]
ON [core].[invoice_lines]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    ;WITH affected AS (
        SELECT invoice_id FROM inserted
        UNION
        SELECT invoice_id FROM deleted
    ), totals AS (
        SELECT l.invoice_id,
               SUM(CASE WHEN l.line_type<>'discount' THEN l.line_amount ELSE 0 END) subtotal,
               SUM(CASE WHEN l.line_type='discount' THEN ABS(l.line_amount) ELSE 0 END) discount
        FROM [core].[invoice_lines] l
        JOIN affected a ON a.invoice_id=l.invoice_id
        GROUP BY l.invoice_id
    )
    UPDATE inv
       SET subtotal_amount=COALESCE(t.subtotal,0),
           discount_amount=COALESCE(t.discount,0),
           total_amount=CASE WHEN COALESCE(t.subtotal,0)-COALESCE(t.discount,0)+inv.tax_amount<0
                             THEN 0 ELSE COALESCE(t.subtotal,0)-COALESCE(t.discount,0)+inv.tax_amount END,
           updated_at=SYSUTCDATETIME()
    FROM [core].[invoices] inv
    JOIN affected a ON a.invoice_id=inv.id
    LEFT JOIN totals t ON t.invoice_id=inv.id;
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_payment_allocation_guard]
ON [core].[payment_allocations]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1 FROM inserted i
        JOIN [core].[payments] p ON p.id=i.payment_id
        JOIN [core].[invoices] inv ON inv.id=i.invoice_id
        WHERE p.status<>'succeeded'
           OR inv.status IN ('draft','voided','paid')
           OR p.customer_id<>inv.customer_id
    )
    BEGIN
        THROW 51055, 'Allocation requires a succeeded payment, open invoice and matching customer.', 1;
    END;

    IF EXISTS (
        SELECT p.id
        FROM [core].[payments] p
        JOIN (SELECT payment_id FROM inserted UNION SELECT payment_id FROM deleted) a ON a.payment_id=p.id
        CROSS APPLY (SELECT COALESCE(SUM(pa.allocated_amount),0) n FROM [core].[payment_allocations] pa WHERE pa.payment_id=p.id) x
        WHERE x.n>p.amount
    )
    BEGIN
        THROW 51056, 'Payment allocations exceed payment amount.', 1;
    END;

    IF EXISTS (
        SELECT inv.id
        FROM [core].[invoices] inv
        JOIN (SELECT invoice_id FROM inserted UNION SELECT invoice_id FROM deleted) a ON a.invoice_id=inv.id
        CROSS APPLY (
            SELECT COALESCE(SUM(pa.allocated_amount),0) n
            FROM [core].[payment_allocations] pa
            JOIN [core].[payments] p ON p.id=pa.payment_id
            WHERE pa.invoice_id=inv.id AND p.status IN ('succeeded','partially_refunded','refunded')
        ) x
        WHERE x.n>inv.total_amount
    )
    BEGIN
        THROW 51057, 'Payment allocations exceed invoice total.', 1;
    END;

    ;WITH affected AS (
        SELECT invoice_id FROM inserted UNION SELECT invoice_id FROM deleted
    ), paid AS (
        SELECT pa.invoice_id,SUM(pa.allocated_amount) amount
        FROM [core].[payment_allocations] pa
        JOIN [core].[payments] p ON p.id=pa.payment_id
        JOIN affected a ON a.invoice_id=pa.invoice_id
        WHERE p.status IN ('succeeded','partially_refunded','refunded')
        GROUP BY pa.invoice_id
    )
    UPDATE inv
       SET paid_amount=CASE WHEN COALESCE(p.amount,0)>inv.total_amount THEN inv.total_amount ELSE COALESCE(p.amount,0) END,
           status=CASE
                    WHEN COALESCE(p.amount,0)>=inv.total_amount THEN 'paid'
                    WHEN COALESCE(p.amount,0)>0 THEN 'partially_paid'
                    WHEN inv.due_date<CONVERT(date,SYSUTCDATETIME()) THEN 'overdue'
                    ELSE 'open'
                  END,
           updated_at=SYSUTCDATETIME()
    FROM [core].[invoices] inv
    JOIN affected a ON a.invoice_id=inv.id
    LEFT JOIN paid p ON p.invoice_id=inv.id
    WHERE inv.status NOT IN ('draft','voided');
END;
GO

CREATE OR ALTER TRIGGER [core].[trg_payments_refresh_invoices]
ON [core].[payments]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(status) RETURN;

    ;WITH affected AS (
        SELECT DISTINCT pa.invoice_id
        FROM inserted i
        JOIN deleted d ON d.id=i.id AND d.status<>i.status
        JOIN [core].[payment_allocations] pa ON pa.payment_id=i.id
    ), paid AS (
        SELECT pa.invoice_id,SUM(pa.allocated_amount) amount
        FROM [core].[payment_allocations] pa
        JOIN [core].[payments] p ON p.id=pa.payment_id
        JOIN affected a ON a.invoice_id=pa.invoice_id
        WHERE p.status IN ('succeeded','partially_refunded','refunded')
        GROUP BY pa.invoice_id
    )
    UPDATE inv
       SET paid_amount=CASE WHEN COALESCE(p.amount,0)>inv.total_amount THEN inv.total_amount ELSE COALESCE(p.amount,0) END,
           status=CASE
                    WHEN COALESCE(p.amount,0)>=inv.total_amount THEN 'paid'
                    WHEN COALESCE(p.amount,0)>0 THEN 'partially_paid'
                    WHEN inv.due_date<CONVERT(date,SYSUTCDATETIME()) THEN 'overdue'
                    ELSE 'open'
                  END,
           updated_at=SYSUTCDATETIME()
    FROM [core].[invoices] inv
    JOIN affected a ON a.invoice_id=inv.id
    LEFT JOIN paid p ON p.invoice_id=inv.id
    WHERE inv.status NOT IN ('draft','voided');
END;
GO

/* ---------- Operational procedures called by .NET scheduler ---------- */
CREATE OR ALTER PROCEDURE [core].[sp_expire_reservation_holds]
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;

    DECLARE @expired TABLE(id bigint PRIMARY KEY);
    INSERT INTO @expired(id)
    SELECT id FROM [core].[reservations] WITH (UPDLOCK, READPAST)
    WHERE status IN ('pending','awaiting_deposit') AND hold_until<=SYSUTCDATETIME();

    UPDATE ua SET status='expired',ended_at=SYSUTCDATETIME(),reason='reservation_hold_timeout'
    FROM [core].[unit_allocations] ua JOIN @expired e ON e.id=ua.reservation_id
    WHERE ua.status='active';

    UPDATE inv SET status='voided',voided_at=SYSUTCDATETIME(),updated_at=SYSUTCDATETIME()
    FROM [core].[invoices] inv JOIN @expired e ON e.id=inv.reservation_id
    WHERE inv.status IN ('draft','open') AND inv.paid_amount=0;

    UPDATE pr SET status='released'
    FROM [core].[promotion_redemptions] pr JOIN @expired e ON e.id=pr.reservation_id
    WHERE pr.status='reserved';

    UPDATE r SET status='expired',updated_at=SYSUTCDATETIME()
    FROM [core].[reservations] r JOIN @expired e ON e.id=r.id;

    UPDATE su SET physical_status='available',updated_at=SYSUTCDATETIME()
    FROM [core].[storage_units] su
    WHERE su.physical_status='reserved'
      AND NOT EXISTS (
          SELECT 1 FROM [core].[unit_allocations] ua
          WHERE ua.storage_unit_id=su.id AND ua.status='active'
            AND CONVERT(date,SYSUTCDATETIME())>=ua.allocation_start_date
            AND CONVERT(date,SYSUTCDATETIME())<ua.allocation_end_date
      )
      AND NOT EXISTS (
          SELECT 1 FROM [core].[maintenance_work_orders] m
          WHERE m.storage_unit_id=su.id AND m.blocks_booking=1
            AND m.status NOT IN ('completed','cancelled')
      );

    COMMIT;
    SELECT COUNT(*) AS expired_count FROM @expired;
END;
GO

CREATE OR ALTER PROCEDURE [core].[sp_generate_renewal_invoices_and_reminders]
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;

    DECLARE @today date=CONVERT(date, SYSDATETIMEOFFSET() AT TIME ZONE 'SE Asia Standard Time');
    DECLARE @newInvoices TABLE(id bigint PRIMARY KEY, agreement_id bigint NOT NULL);

    -- BR-REN-01: create the next renewal invoice when the agreement reaches T-7.
    INSERT INTO [core].[invoices](
        invoice_no,customer_id,agreement_id,billing_period,issue_date,due_date,
        currency,status,opened_at
    )
    OUTPUT inserted.id,inserted.agreement_id INTO @newInvoices(id,agreement_id)
    SELECT
        CONCAT('INV-',CONVERT(char(8),@today,112),'-',
               RIGHT(REPLICATE('0',7)+CONVERT(varchar(20),NEXT VALUE FOR [core].[invoice_no_seq]),7)),
        a.customer_id,a.id,
        CONCAT(CONVERT(char(10),a.end_date,23),'/',CONVERT(char(10),DATEADD(month,1,a.end_date),23)),
        @today,a.end_date,'VND','draft',NULL
    FROM [core].[rental_agreements] a
    WHERE a.status IN ('active','extended')
      AND DATEDIFF(day,@today,a.end_date) BETWEEN 0 AND 7
      AND NOT EXISTS (
          SELECT 1 FROM [core].[invoices] i
          WHERE i.agreement_id=a.id
            AND i.billing_period=CONCAT(CONVERT(char(10),a.end_date,23),'/',CONVERT(char(10),DATEADD(month,1,a.end_date),23))
            AND i.status<>'voided'
      );

    INSERT INTO [core].[invoice_lines](invoice_id,line_type,description,quantity,unit_price,metadata)
    SELECT n.id,'renewal',N'One-month storage renewal',1,a.monthly_rate_snapshot,
           CONCAT(N'{"agreement_id":',a.id,N',"period_start":"',CONVERT(nvarchar(10),a.end_date,23),
                  N'","period_end":"',CONVERT(nvarchar(10),DATEADD(month,1,a.end_date),23),N'"}')
    FROM @newInvoices n
    JOIN [core].[rental_agreements] a ON a.id=n.agreement_id;

    UPDATE i SET status='open',opened_at=SYSUTCDATETIME(),updated_at=SYSUTCDATETIME()
    FROM [core].[invoices] i JOIN @newInvoices n ON n.id=i.id;

    -- Reminders exactly at T-7, T-3 and T-1. deduplication_key makes retries safe.
    INSERT INTO [core].[notifications](user_id,channel,template_code,payload,status,scheduled_at,deduplication_key)
    SELECT a.customer_id,'in_app','renewal_payment_reminder',
           CONCAT(N'{"agreement_id":',a.id,N',"days_remaining":',x.days_remaining,
                  N',"end_date":"',CONVERT(nvarchar(10),a.end_date,23),N'"}'),
           'pending',SYSUTCDATETIME(),
           CONCAT('renewal-reminder:',a.id,':',x.days_remaining)
    FROM [core].[rental_agreements] a
    CROSS APPLY (SELECT DATEDIFF(day,@today,a.end_date) days_remaining) x
    WHERE a.status IN ('active','extended')
      AND x.days_remaining IN (7,3,1)
      AND NOT EXISTS (
          SELECT 1 FROM [core].[notifications] n
          WHERE n.deduplication_key=CONCAT('renewal-reminder:',a.id,':',x.days_remaining)
      );

    COMMIT;
    SELECT COUNT(*) AS invoices_created FROM @newInvoices;
END;
GO

CREATE OR ALTER PROCEDURE [core].[sp_process_overdue_accounts]
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;

    DECLARE @today date=CONVERT(date, SYSDATETIMEOFFSET() AT TIME ZONE 'SE Asia Standard Time');

    -- BR-REN-02: one late-fee line per overdue calendar day.
    -- Daily listed rent is represented by monthly_rate_snapshot / 30.
    INSERT INTO [core].[invoice_lines](invoice_id,line_type,description,quantity,unit_price,metadata)
    SELECT i.id,'late_fee',N'Late fee '+CONVERT(nvarchar(10),@today,23),1,
           ROUND(a.monthly_rate_snapshot/30.0*1.5,2),
           N'{"rule":"BR-REN-02","rate":1.5,"base":"daily_rent"}'
    FROM [core].[invoices] i
    JOIN [core].[rental_agreements] a ON a.id=i.agreement_id
    WHERE i.status IN ('open','partially_paid','overdue')
      AND i.due_date<@today AND i.paid_amount<i.total_amount
      AND NOT EXISTS (
          SELECT 1 FROM [core].[invoice_lines] il
          WHERE il.invoice_id=i.id AND il.line_type='late_fee'
            AND il.description=N'Late fee '+CONVERT(nvarchar(10),@today,23)
      );

    UPDATE [core].[invoices]
       SET status='overdue',updated_at=SYSUTCDATETIME()
    WHERE status IN ('open','partially_paid')
      AND due_date<@today AND paid_amount<total_amount;

    INSERT INTO [core].[delinquency_cases](agreement_id,invoice_id,status,opened_at,grace_ends_at,outstanding_snapshot,notes)
    SELECT i.agreement_id,i.id,'delinquent',SYSUTCDATETIME(),SYSUTCDATETIME(),i.total_amount-i.paid_amount,
           N'BR-REN-02/03: overdue from N+1'
    FROM [core].[invoices] i
    WHERE i.status='overdue' AND i.agreement_id IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM [core].[delinquency_cases] d WHERE d.invoice_id=i.id AND d.resolved_at IS NULL);

    UPDATE a SET status='overdue',updated_at=SYSUTCDATETIME()
    FROM [core].[rental_agreements] a
    WHERE a.status IN ('active','extended')
      AND EXISTS (SELECT 1 FROM [core].[invoices] i WHERE i.agreement_id=a.id AND i.status='overdue' AND i.paid_amount<i.total_amount);

    -- BR-REN-03: suspend access from the first overdue day (N+1).
    UPDATE c SET status='suspended'
    FROM [core].[access_credentials] c
    JOIN [core].[rental_agreements] a ON a.id=c.agreement_id
    WHERE a.status='overdue' AND c.status='active';

    -- BR-REN-04: default after more than 30 overdue days.
    UPDATE a SET status='defaulted',updated_at=SYSUTCDATETIME()
    FROM [core].[rental_agreements] a
    WHERE a.status='overdue'
      AND EXISTS (
          SELECT 1 FROM [core].[invoices] i
          WHERE i.agreement_id=a.id AND i.status='overdue'
            AND DATEDIFF(day,i.due_date,@today)>30 AND i.paid_amount<i.total_amount
      );

    COMMIT;
END;
GO


/* ===== 03_Seed_Corrected.sql ===== */
/* Demo data converted from the PostgreSQL seed. */
USE [SelfStoragePRN222];
GO
SET IDENTITY_INSERT [core].[roles] ON;

INSERT INTO [core].roles (id, code, display_name, description)

VALUES
    (1, 'storage_customer', 'Storage Customer', 'Customer who rents storage units'),
    (2, 'facility_staff', 'Facility Staff', 'On-site check-in, checkout and support staff'),
    (3, 'facility_manager', 'Facility Manager', 'Manager scoped to assigned facilities'),
    (4, 'business_operations_manager', 'Business Operations Manager', 'Cross-facility pricing, policy and reporting'),
    (5, 'system_administrator', 'System Administrator', 'Accounts, authorization and audit administration');

SET IDENTITY_INSERT [core].[roles] OFF;

SET IDENTITY_INSERT [core].[users] ON;

INSERT INTO [core].users (
    id, email, phone_number, password_hash, status
)

VALUES
    (1, 'customer.one@example.test', '0900000001', 'external-auth-demo-only', 'active'),
    (2, 'customer.two@example.test', '0900000002', 'external-auth-demo-only', 'active'),
    (3, 'staff.hcm@example.test', '0900000003', 'external-auth-demo-only', 'active'),
    (4, 'manager.hcm@example.test', '0900000004', 'external-auth-demo-only', 'active'),
    (5, 'operations@example.test', '0900000005', 'external-auth-demo-only', 'active'),
    (6, 'administrator@example.test', '0900000006', 'external-auth-demo-only', 'active');

SET IDENTITY_INSERT [core].[users] OFF;

INSERT INTO [core].user_roles (user_id, role_id)
VALUES
    (1, 1),
    (2, 1),
    (3, 2),
    (4, 3),
    (5, 4),
    (6, 5);

INSERT INTO [core].customer_profiles (
    user_id, full_name, identity_number, date_of_birth, address
)
VALUES
    (1, 'Nguyen Minh Anh', 'DEMO-CUSTOMER-001', CONVERT(date, '1998-04-12', 23), 'Thu Duc City, Ho Chi Minh City'),
    (2, 'Tran Gia Binh', 'DEMO-CUSTOMER-002', CONVERT(date, '1995-09-20', 23), 'District 7, Ho Chi Minh City');

INSERT INTO [core].employee_profiles (
    user_id, employee_code, full_name, hire_date, employment_status
)
VALUES
    (3, 'EMP-HCM-001', 'Le Hoang Staff', CONVERT(date, '2025-01-10', 23), 'active'),
    (4, 'MGR-HCM-001', 'Pham Thu Manager', CONVERT(date, '2024-03-01', 23), 'active'),
    (5, 'OPS-001', 'Do Quang Operations', CONVERT(date, '2023-08-15', 23), 'active');

SET IDENTITY_INSERT [core].[facilities] ON;

INSERT INTO [core].facilities (
    id, code, name, address_line, district, city,
    latitude, longitude, opening_time, closing_time, status
)

VALUES
    (1, 'HCM-TD', 'Thu Duc Self Storage', '01 Vo Van Ngan', 'Thu Duc City', 'Ho Chi Minh City',
     10.850700, 106.771900, CONVERT(time, '07:00'), CONVERT(time, '21:00'), 'active'),
    (2, 'HN-CG', 'Cau Giay Self Storage', '10 Tran Thai Tong', 'Cau Giay', 'Ha Noi',
     21.033300, 105.787500, CONVERT(time, '07:00'), CONVERT(time, '21:00'), 'active');

SET IDENTITY_INSERT [core].[facilities] OFF;

INSERT INTO [core].staff_facility_assignments (
    employee_id, facility_id, assignment_role, starts_at, assigned_by
)
VALUES
    (3, 1, 'facility_staff', CONVERT(datetimeoffset, '2026-01-01 00:00:00+07:00'), 4),
    (4, 1, 'facility_manager', CONVERT(datetimeoffset, '2026-01-01 00:00:00+07:00'), 5),
    (4, 2, 'facility_manager', CONVERT(datetimeoffset, '2026-01-01 00:00:00+07:00'), 5);

SET IDENTITY_INSERT [core].[facility_areas] ON;

INSERT INTO [core].facility_areas (
    id, facility_id, parent_area_id, code, name, area_type, display_order
)

VALUES
    (1, 1, NULL, 'BLDG-A', 'Building A', 'building', 1),
    (2, 1, 1, 'F1', 'Floor 1', 'floor', 1),
    (3, 1, 2, 'F1-ZA', 'Floor 1 - Zone A', 'zone', 1),
    (4, 2, NULL, 'BLDG-HN', 'Ha Noi Building', 'building', 1),
    (5, 2, 4, 'HN-F1', 'Floor 1', 'floor', 1);

SET IDENTITY_INSERT [core].[facility_areas] OFF;

SET IDENTITY_INSERT [core].[unit_types] ON;

INSERT INTO [core].unit_types (
    id, code, name, width_m, length_m, height_m,
    climate_controlled, max_weight_kg, description
)

VALUES
    (1, 'S', 'Small 3 m2', 1.50, 2.00, 2.50, 0, 600, 'Documents and small household items'),
    (2, 'M', 'Medium 6 m2', 2.00, 3.00, 2.50, 0, 1200, 'Apartment furniture and business stock'),
    (3, 'M-CC', 'Medium 6 m2 Climate Controlled', 2.00, 3.00, 2.50, 1, 1000, 'Humidity and temperature controlled');

SET IDENTITY_INSERT [core].[unit_types] OFF;

SET IDENTITY_INSERT [core].[storage_units] ON;

INSERT INTO [core].storage_units (
    id, facility_id, unit_type_id, area_id, unit_code,
    floor_label, zone_label, physical_status, is_listed
)

VALUES
    (1, 1, 1, 3, 'A-101', '1', 'A', 'available', 1),
    (2, 1, 1, 3, 'A-102', '1', 'A', 'available', 1),
    (3, 1, 2, 3, 'A-105', '1', 'A', 'available', 1),
    (4, 1, 3, 3, 'A-108', '1', 'A', 'available', 1),
    (5, 2, 1, 5, 'HN-101', '1', 'A', 'available', 1),
    (6, 2, 2, 5, 'HN-105', '1', 'A', 'available', 1);

SET IDENTITY_INSERT [core].[storage_units] OFF;

INSERT INTO [core].unit_map_positions (
    unit_id, area_id, x, y, width, height, rotation_degrees
)
VALUES
    (1, 3, 10, 10, 15, 20, 0),
    (2, 3, 30, 10, 15, 20, 0),
    (3, 3, 50, 10, 20, 30, 0),
    (4, 3, 75, 10, 20, 30, 0),
    (5, 5, 10, 10, 15, 20, 0),
    (6, 5, 30, 10, 20, 30, 0);

SET IDENTITY_INSERT [core].[price_ranges] ON;

INSERT INTO [core].price_ranges (
    id, unit_type_id, min_monthly_rate, max_monthly_rate,
    valid_from, valid_to, created_by
)

VALUES
    (1, 1, 700000, 1300000, CONVERT(date, '2026-01-01', 23), NULL, 5),
    (2, 2, 1200000, 2200000, CONVERT(date, '2026-01-01', 23), NULL, 5),
    (3, 3, 1700000, 3000000, CONVERT(date, '2026-01-01', 23), NULL, 5);

SET IDENTITY_INSERT [core].[price_ranges] OFF;

SET IDENTITY_INSERT [core].[facility_rates] ON;

INSERT INTO [core].facility_rates (
    id, facility_id, unit_type_id, monthly_rate, deposit_amount,
    booking_fee, valid_from, valid_to, created_by
)

VALUES
    (1, 1, 1, 900000, 900000, 50000, CONVERT(date, '2026-01-01', 23), NULL, 4),
    (2, 1, 2, 1500000, 1500000, 50000, CONVERT(date, '2026-01-01', 23), NULL, 4),
    (3, 1, 3, 2100000, 2100000, 75000, CONVERT(date, '2026-01-01', 23), NULL, 4),
    (4, 2, 1, 850000, 850000, 50000, CONVERT(date, '2026-01-01', 23), NULL, 4),
    (5, 2, 2, 1400000, 1400000, 50000, CONVERT(date, '2026-01-01', 23), NULL, 4);

SET IDENTITY_INSERT [core].[facility_rates] OFF;

SET IDENTITY_INSERT [core].[policy_versions] ON;

INSERT INTO [core].policy_versions (
    id, policy_type, version, content, valid_from, valid_to, created_by
)

VALUES
    (1, 'rental_terms', '2026.1', '{"minimum_months":1,"billing_cycle":"monthly"}', CONVERT(date, '2026-01-01', 23), NULL, 5),
    (2, 'deposit', '2026.1', '{"refund_after_checkout":1,"manager_approval":1}', CONVERT(date, '2026-01-01', 23), NULL, 5),
    (3, 'cancellation', '2026.1', '{"free_before_start_days":3}', CONVERT(date, '2026-01-01', 23), NULL, 5),
    (4, 'renewal', '2026.1', '{"invoice_days_before_expiry":7}', CONVERT(date, '2026-01-01', 23), NULL, 5),
    (5, 'move_out', '2026.1', '{"inspection_required":1}', CONVERT(date, '2026-01-01', 23), NULL, 5),
    (6, 'overdue', '2026.1', '{"grace_days":0,"late_fee_from_day":1,"suspend_access_from_day":1,"default_after_days":30}', CONVERT(date, '2026-01-01', 23), NULL, 5);

SET IDENTITY_INSERT [core].[policy_versions] OFF;

SET IDENTITY_INSERT [core].[fee_rules] ON;

INSERT INTO [core].fee_rules (
    id, facility_id, code, fee_type, calculation_method, amount, rate_percent,
    grace_days, conditions, valid_from, valid_to, created_by
)

VALUES
    -- BR-REN-02: from N+1, late fee = 150% of the listed DAILY rent.
    (1, NULL, 'LATE-150PCT-DAILY', 'late_fee', 'percentage', NULL, 150, 0,
     '{"base":"daily_rent","start_day":1,"default_after_days":30}', CONVERT(date, '2026-01-01', 23), NULL, 5),
    (2, 1, 'LOST-KEY-HCM', 'lost_key_fee', 'flat', 250000, NULL, 0,
     '{}', CONVERT(date, '2026-01-01', 23), NULL, 4);

SET IDENTITY_INSERT [core].[fee_rules] OFF;

SET IDENTITY_INSERT [core].[promotions] ON;

INSERT INTO [core].promotions (
    id, code, name, description, discount_type, discount_value,
    max_discount_amount, usage_limit, per_customer_limit,
    valid_from, valid_to, is_active, created_by
)

VALUES (
    1, 'WELCOME10', 'Welcome 10%', 'Ten percent off the initial amount for a new customer',
    'percentage', 10, 300000, 1000, 1,
    CONVERT(datetimeoffset, '2026-01-01 00:00:00+07:00'), CONVERT(datetimeoffset, '2030-01-01 00:00:00+07:00'),
    1, 5
);

SET IDENTITY_INSERT [core].[promotions] OFF;

INSERT INTO [core].promotion_rules (
    promotion_id, rule_type, operator, rule_value
)
VALUES
    (1, 'minimum_months', 'gte', N'1'),
    (1, 'new_customer', 'eq', N'1');

SET IDENTITY_INSERT [core].[staff_shifts] ON;

INSERT INTO [core].staff_shifts (
    id, facility_id, shift_name, starts_at, ends_at, status, created_by
)

VALUES (
    1, 1, 'Today full-day demo shift',
    CAST(CAST(SYSUTCDATETIME() AS date) AS datetimeoffset),
    DATEADD(day, 1, CAST(CAST(SYSUTCDATETIME() AS date) AS datetimeoffset)),
    'open', 4
);

SET IDENTITY_INSERT [core].[staff_shifts] OFF;

INSERT INTO [core].shift_assignments (
    shift_id, employee_id, duty_role, check_in_at, status
)
VALUES (1, 3, 'staff', SYSUTCDATETIME(), 'checked_in');

SET IDENTITY_INSERT [core].[access_points] ON;

INSERT INTO [core].access_points (
    id, facility_id, code, name, access_point_type, external_device_code, status
)

VALUES
    (1, 1, 'GATE-01', 'Main vehicle gate', 'gate', 'DEMO-GATE-HCM-01', 'active'),
    (2, 1, 'DOOR-A', 'Building A entrance', 'building_door', 'DEMO-DOOR-HCM-A', 'active');

SET IDENTITY_INSERT [core].[access_points] OFF;


/* ===== 04_CompatibilityViews.sql ===== */
/*
  The PostgreSQL project renamed these two base tables in migration 070 and
  retained old names as compatibility views. Here the normalized table names
  remain the SQL Server base tables; these views expose the required HaTDT
  contract without breaking all foreign keys and business-rule triggers.
*/
USE [SelfStoragePRN222];
GO

CREATE OR ALTER VIEW [core].[UnitTypeHaTDT]
AS
SELECT
    id AS UnitTypeHaTDTId,
    code, name, width_m, length_m, height_m, area_m2, volume_m3,
    climate_controlled, max_weight_kg, description, is_active,
    created_at, updated_at
FROM [core].[unit_types];
GO

CREATE OR ALTER VIEW [core].[StorageUnitHaTDT]
AS
SELECT
    id AS StorageUnitHaTDTId,
    facility_id,
    unit_type_id AS UnitTypeHaTDTId,
    area_id, unit_code, floor_label, zone_label, physical_status,
    is_listed, notes, created_at, updated_at
FROM [core].[storage_units];
GO


/* ===== Final installation validation ===== */
IF DB_NAME() <> N'SelfStoragePRN222'
    THROW 51001, 'Validation failed: wrong current database.', 1;

IF SCHEMA_ID(N'core') IS NULL OR SCHEMA_ID(N'api') IS NULL OR SCHEMA_ID(N'auth_private') IS NULL
    THROW 51002, 'Validation failed: one or more required schemas are missing.', 1;

IF (SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'core')) <> 57
    THROW 51003, 'Validation failed: expected 57 core tables.', 1;

IF (SELECT COUNT(*) FROM sys.sequences WHERE schema_id = SCHEMA_ID(N'core')) <> 5
    THROW 51004, 'Validation failed: expected 5 core sequences.', 1;

IF (SELECT COUNT(*) FROM sys.triggers WHERE parent_class_desc = N'OBJECT_OR_COLUMN' AND OBJECT_SCHEMA_NAME(parent_id) = N'core') <> 35
    THROW 51005, 'Validation failed: expected 35 core DML triggers.', 1;

IF (SELECT COUNT(*) FROM sys.procedures WHERE schema_id = SCHEMA_ID(N'core')) <> 3
    THROW 51006, 'Validation failed: expected 3 core stored procedures.', 1;

IF OBJECT_ID(N'core.UnitTypeHaTDT', N'V') IS NULL OR OBJECT_ID(N'core.StorageUnitHaTDT', N'V') IS NULL
    THROW 51007, 'Validation failed: required compatibility views are missing.', 1;

PRINT N'SelfStoragePRN222 installation completed successfully and passed validation.';
GO
