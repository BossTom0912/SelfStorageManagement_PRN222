# Database Schema Report — Self-Storage Facility Rental and Management System

**Database:** `SelfStoragePRN222`  
**Engine:** Microsoft SQL Server 2019+  
**Application Schema:** `core`  
**Architecture:** 3-Layer Architecture (.NET 8, ASP.NET Core Web API, EF Core 8 Database First)  
**Audit Date:** 2026-09-25  

---

## 1. Overview
- **Database Name:** `SelfStoragePRN222`
- **Default Application Schema:** `core`
- **Total Base Tables:** 57 tables [PROVEN BY DATABASE]
- **Total SQL Views:** 2 views (`core.UnitTypeHaTDT`, `core.StorageUnitHaTDT`) [PROVEN BY DATABASE]
- **Total Foreign Keys:** 139 foreign keys [PROVEN BY DATABASE]
- **Total Check Constraints:** 178 constraints [verified against the live database during the final review]
- **Total Active Triggers:** 35 triggers [PROVEN BY DATABASE]
- **Reverse Engineering Target:** EF Core 8 Database First generated `SelfStorageDbContext` and 59 entity classes under `src/SelfStorageManagementSystem.DataAccess`.

---

## 2. Tables and Views
All tables reside under the `core` schema.

### Base Tables (57)
1. `access_credentials` (13 cols)
2. `access_events` (8 cols)
3. `access_points` (7 cols)
4. `appointments` (12 cols)
5. `audit_logs` (12 cols)
6. `authorized_access_members` (10 cols)
7. `customer_profiles` (9 cols)
8. `delinquency_actions` (6 cols)
9. `delinquency_cases` (9 cols)
10. `employee_profiles` (7 cols)
11. `facilities` (15 cols)
12. `facility_areas` (9 cols)
13. `facility_rates` (10 cols)
14. `fee_rules` (14 cols)
15. `handover_records` (12 cols)
16. `identity_verifications` (8 cols)
17. `inspection_items` (7 cols)
18. `inspections` (12 cols)
19. `integration_events` (9 cols)
20. `invoice_lines` (8 cols)
21. `invoices` (20 cols)
22. `login_history` (7 cols)
23. `maintenance_work_orders` (20 cols)
24. `move_out_requests` (10 cols)
25. `notifications` (13 cols)
26. `payment_allocations` (4 cols)
27. `payments` (15 cols)
28. `policy_versions` (8 cols)
29. `price_ranges` (8 cols)
30. `promotion_redemptions` (8 cols)
31. `promotion_rules` (5 cols)
32. `promotions` (14 cols)
33. `refund_approvals` (5 cols)
34. `refunds` (14 cols)
35. `rental_agreements` (18 cols)
36. `rental_renewals` (12 cols)
37. `reservations` (20 cols)
38. `roles` (4 cols)
39. `service_ratings` (5 cols)
40. `shift_assignments` (6 cols)
41. `staff_facility_assignments` (8 cols)
42. `staff_shifts` (8 cols)
43. `staff_tasks` (14 cols)
44. `storage_units` (12 cols)
45. `support_tickets` (15 cols)
46. `ticket_assignments` (7 cols)
47. `ticket_attachments` (10 cols)
48. `ticket_charge_approvals` (5 cols)
49. `ticket_charge_proposals` (8 cols)
50. `ticket_messages` (6 cols)
51. `unit_allocations` (12 cols)
52. `unit_map_positions` (8 cols)
53. `unit_status_history` (7 cols)
54. `unit_transfer_requests` (13 cols)
55. `unit_types` (14 cols)
56. `user_roles` (4 cols)
57. `users` (8 cols)

### Views (2)
1. `core.UnitTypeHaTDT` (14 cols) [PROVEN BY DATABASE]
2. `core.StorageUnitHaTDT` (12 cols) [PROVEN BY DATABASE]

---

## 3. Primary Keys
- **Surrogate Identity PKs (47 tables):** Column `id` of type `bigint IDENTITY(1,1)` (e.g., `users`, `facilities`, `storage_units`, `reservations`, `rental_agreements`, `invoices`, `payments`).
- **Smallint Identity PK (1 table):** `roles.id` of type `smallint IDENTITY(1,1)`.
- **One-to-One Non-Identity PKs (6 tables):**
  - `customer_profiles.user_id` (`bigint`, PK & FK referencing `users.id`)
  - `employee_profiles.user_id` (`bigint`, PK & FK referencing `users.id`)
  - `refund_approvals.refund_id` (`bigint`, PK & FK referencing `refunds.id`)
  - `service_ratings.ticket_id` (`bigint`, PK & FK referencing `support_tickets.id`)
  - `ticket_charge_approvals.proposal_id` (`bigint`, PK & FK referencing `ticket_charge_proposals.id`)
  - `unit_map_positions.unit_id` (`bigint`, PK & FK referencing `storage_units.id`)
- **Keyless Entities (2 views):** `UnitTypeHaTDT`, `StorageUnitHaTDT` (mapped with `HasNoKey()`).

---

## 4. Composite Keys
The database contains exactly 3 tables with composite primary keys [PROVEN BY DATABASE]:
1. `payment_allocations`:
   - Primary Key: `(payment_id, invoice_id)`
   - Meaning: Junction entity tracking how an inbound payment is allocated across invoices.
2. `shift_assignments`:
   - Primary Key: `(shift_id, employee_id)`
   - Meaning: Assigns a specific employee profile to a specific work shift.
3. `user_roles`:
   - Primary Key: `(user_id, role_id)`
   - Meaning: Many-to-many relationship linking a user account with a security role.

---

## 5. Foreign Keys
- Total Foreign Keys in SQL Server: 139 [PROVEN BY DATABASE].
- Total `HasForeignKey` in `SelfStorageDbContext.cs`: 139 [PROVEN BY DATABASE].
- Referential Delete Action in SQL Server:
  - `CASCADE`: 4 foreign keys (`promotion_rules -> promotions`, `shift_assignments -> staff_shifts`, `unit_map_positions -> storage_units`, `user_roles -> users`).
  - `NO_ACTION`: 135 foreign keys (to prevent cascade deletion paths and preserve transactional/historical integrity).
- EF Core ClientSetNull mapping: 80 FK relationships have `DeleteBehavior.ClientSetNull` configured by EF Core scaffold; this does not imply that all 80 foreign keys are nullable.

---

## 6. Main Relationships
### Composite Foreign Keys (Cross-entity Scoping & Multi-Tenancy)
1. `facility_areas (parent_area_id, facility_id)` references `facility_areas (id, facility_id)`.
2. `support_tickets (storage_unit_id, facility_id)` references `storage_units (id, facility_id)`.
3. `support_tickets (agreement_id, customer_id, facility_id)` references `rental_agreements (id, customer_id, facility_id)`.
4. `ticket_attachments (message_id, ticket_id)` references `ticket_messages (id, ticket_id)`.

### Core Lifecycle Relationship Chains
- `users` (1) ── (1) `customer_profiles` (1) ── (N) `reservations` (1) ── (0..1) `rental_agreements`.
- `storage_units` (N) ── (1) `unit_types`.
- `reservations` (1) ── (0..1) `unit_allocations` (allocation_kind = 'reservation_hold') ── (1) `storage_units`.
- `rental_agreements` (1) ── (0..1) `unit_allocations` (allocation_kind = 'rental') ── (1) `storage_units`.
- `rental_agreements` (1) ── (N) `invoices` (1) ── (N) `invoice_lines`.
- `invoices` (1) ── (N) `payment_allocations` (N) ── (1) `payments`.

---

## 7. Data Types
Analysis across all 598 columns in schema `core`:
- `bigint` (187 columns) ➔ mapped to `long` / `long?` in C#.
- `nvarchar` (193 columns) ➔ mapped to `string` / `string?` in C# with appropriate length limits.
- `datetimeoffset` (114 columns) ➔ mapped to `DateTimeOffset` / `DateTimeOffset?` in C#.
- `numeric` (55 columns) ➔ mapped to `decimal` / `decimal?` in C# (e.g. `numeric(14,2)` for financial fields, `numeric(10,2)` for dimensions/coordinates).
- `date` (24 columns) ➔ mapped to `DateOnly` / `DateOnly?` in C# (e.g. `reservations.start_date`, `rental_agreements.end_date`).
- `bit` (11 columns) ➔ mapped to `bool` in C# (e.g. `is_active`, `is_listed`, `climate_controlled`).
- `smallint` (5 columns) ➔ mapped to `short` / `short?` in C# (e.g. `roles.id`, `user_roles.role_id`).
- `int` (4 columns) ➔ mapped to `int` / `int?` in C# (e.g. `failed_attempts`, `progress_percent`).
- `char` (3 columns) ➔ mapped to `string` in C# (e.g. `currency char(3)`).
- `time` (2 columns) ➔ mapped to `TimeOnly` in C# (e.g. facility open/close hours).

---

## 8. Nullability
- SQL `NOT NULL` columns strictly map to non-nullable types in C# (`long`, `decimal`, `DateTimeOffset`, `DateOnly`, `string = null!`).
- Optional SQL columns correctly map to nullable C# types (`long?`, `decimal?`, `DateTimeOffset?`, `DateOnly?`, `string?`).
- Key optional foreign keys:
  - `reservations.facility_rate_id`: NOT NULL (every reservation requires an active rate snapshot).
  - `rental_agreements.reservation_id`: UNIQUE, NULLABLE (allows walk-in direct rental agreements if needed, though typically converted from reservations).
  - `invoices.reservation_id` / `invoices.agreement_id` / `invoices.ticket_charge_proposal_id`: All optional individually, but guarded by a CHECK constraint requiring at least one target origin.

---

## 9. Identity / Generated Values
- Identity columns: 47 tables use `bigint IDENTITY(1,1)` and 1 table (`roles`) uses `smallint IDENTITY(1,1)`.
- Default constraints:
  - `created_at`: `DEFAULT (sysdatetimeoffset())` or `(sysutcdatetime())`
  - `updated_at`: `DEFAULT (sysdatetimeoffset())` or `(sysutcdatetime())`
  - `failed_attempts`: `DEFAULT (0)`
  - `is_active`: `DEFAULT (1)`
  - `status`: specific default values per domain (e.g. `pending`, `draft`, `active`).
- Business codes: `reservation_code`, `agreement_no`, `invoice_no`, `ticket_no`, `work_order_no` are populated by application logic or database sequences/triggers.

---

## 10. Unique Constraints and Indexes
Unique constraints confirmed in SQL Server [PROVEN BY DATABASE]:
- `users`: `email` (`users_email_unique`)
- `roles`: `code`
- `facilities`: `code`
- `facility_areas`: `(facility_id, code)`, `(id, facility_id)`
- `storage_units`: `(facility_id, unit_code)`, `(id, facility_id)`
- `unit_types`: `code`
- `reservations`: `reservation_code`, `(id, customer_id, facility_id, unit_type_id)`
- `rental_agreements`: `agreement_no`, `reservation_id`, `(id, customer_id, facility_id)`
- `invoices`: `invoice_no`, `ticket_charge_proposal_id`
- `payments`: `idempotency_key`
- `refunds`: `idempotency_key`
- `promotions`: `code`
- `promotion_rules`: `(promotion_id, rule_type)`
- `maintenance_work_orders`: `work_order_no`
- `support_tickets`: `ticket_no`
- `ticket_messages`: `(id, ticket_id)`
- `notifications`: `deduplication_key`
- `employee_profiles`: `employee_code`
- `policy_versions`: `(policy_type, version)`

---

## 11. Status Fields
All status-like fields are constrained by explicit SQL CHECK constraints [PROVEN BY DATABASE]:
- `users.status`: `'active'`, `'locked'`, `'disabled'`
- `employee_profiles.employment_status`: `'active'`, `'leave'`, `'terminated'`
- `facilities.status`: `'draft'`, `'active'`, `'temporarily_closed'`, `'closed'`
- `facility_areas.area_type`: `'building'`, `'floor'`, `'zone'`, `'section'`
- `storage_units.physical_status`: `'available'`, `'reserved'`, `'occupied'`, `'pending_inspection'`, `'maintenance'`, `'out_of_service'`
- `reservations.status`: `'pending'`, `'awaiting_deposit'`, `'confirmed'`, `'checked_in'`, `'converted'`, `'completed'`, `'no_show'`, `'cancelled'`, `'expired'`
- `rental_agreements.status`: `'draft'`, `'scheduled'`, `'active'`, `'extended'`, `'overdue'`, `'defaulted'`, `'move_out_scheduled'`, `'checkout_pending'`, `'expired'`, `'terminated'`, `'completed'`, `'closed'`, `'cancelled'`
- `rental_renewals.status`: `'pending_payment'`, `'paid'`, `'approved'`, `'rejected'`, `'cancelled'`
- `unit_allocations.allocation_kind`: `'reservation_hold'`, `'rental'`, `'transfer'`
- `unit_allocations.status`: `'active'`, `'consumed'`, `'released'`, `'expired'`
- `invoices.status`: `'draft'`, `'open'`, `'partially_paid'`, `'paid'`, `'overdue'`, `'voided'`
- `payments.status`: `'initiated'`, `'pending'`, `'succeeded'`, `'failed'`, `'cancelled'`, `'partially_refunded'`, `'refunded'`
- `payments.method`: `'cash'`, `'bank_transfer'`, `'vnpay'`, `'payos'`, `'stripe'`, `'other'`
- `refunds.status`: `'requested'`, `'approved'`, `'processing'`, `'succeeded'`, `'failed'`, `'rejected'`
- `support_tickets.status`: `'open'`, `'in_progress'`, `'waiting_for_customer'`, `'waiting_for_maintenance'`, `'resolved'`, `'closed'`, `'cancelled'`
- `support_tickets.priority`: `'low'`, `'normal'`, `'high'`, `'urgent'`
- `support_tickets.category`: `'unit'`, `'access'`, `'payment'`, `'stored_item'`, `'maintenance'`, `'other'`
- `delinquency_cases.status`: `'grace'`, `'delinquent'`, `'cured'`, `'waived'`, `'termination_started'`
- `staff_shifts.status`: `'planned'`, `'open'`, `'completed'`, `'cancelled'`
- `shift_assignments.duty_role`: `'staff'`, `'shift_lead'`, `'manager_on_call'`
- `shift_assignments.status`: `'scheduled'`, `'checked_in'`, `'checked_out'`, `'absent'`
- `staff_facility_assignments.assignment_role`: `'facility_manager'`, `'facility_staff'`
- `staff_tasks.status`: `'todo'`, `'in_progress'`, `'blocked'`, `'done'`, `'cancelled'`
- `staff_tasks.task_type`: `'check_in'`, `'check_out'`, `'inspection'`, `'maintenance'`, `'support'`, `'other'`

---

## 12. Users and Roles
- Table `roles` contains 5 standard roles seeded:
  1. `system_administrator`
  2. `business_operations_manager`
  3. `facility_manager`
  4. `facility_staff`
  5. `storage_customer`
- Profiles:
  - `customer_profiles`: holds phone, address, tax code, notes.
  - `employee_profiles`: holds employee code, phone, title, employment status.
- Relationship: User accounts authenticate via `users` (`email`, `password_hash`), and are assigned one or more roles via `user_roles`.

---

## 13. Facilities
- Tables: `facilities`, `facility_areas`, `access_points`.
- Multi-area hierarchy: `facility_areas` supports parent-child zoning via `parent_area_id` with composite FK `(parent_area_id, facility_id)` to prevent cross-facility nesting.
- Access points track physical barriers (doors, gates, elevators) with credentials and access event logs.

---

## 14. Storage Units
- Tables: `unit_types`, `storage_units`, `unit_allocations`, `unit_map_positions`, `unit_status_history`, `unit_transfer_requests`.
- Physical vs Logical allocation:
  - `storage_units.physical_status` reflects physical reality on site.
  - `unit_allocations` manages time-bounded exclusivity (`allocation_start_date` to `allocation_end_date`) linked to either `reservation_id` or `agreement_id`.
  - Trigger `trg_unit_allocation_no_overlap` prevents simultaneous overlapping active allocations on the same unit.

---

## 15. Reservations
- Tables: `reservations`, `appointments`.
- Proven Business Rules:
  - Check constraint `reservations_hold_max_15m_ck`: `hold_until <= DATEADD(minute, 15, created_at)`.
  - Check constraint `reservations_rental_term_ck`: `end_date >= DATEADD(month, 1, start_date)` and `<= DATEADD(month, 12, start_date)` (terms strictly 1..12 months).
  - Price snapshots: `monthly_rate_snapshot`, `deposit_snapshot`, `booking_fee_snapshot`, `discount_snapshot`, `quoted_total` frozen at booking.
  - Cancellation: `cancelled_at` must be NOT NULL when `status = 'cancelled'`.

---

## 16. Rentals
- Tables: `rental_agreements`, `rental_renewals`, `move_out_requests`, `policy_versions`.
- Agreement life cycle: Created upon reservation check-in/conversion, retains frozen snapshots (`monthly_rate_snapshot`, `deposit_snapshot`, `deposit_balance`), tracks `checked_in_at` and `checked_out_at`.
- Renewal: `rental_renewals` tracks term extension proposals with `requested_end_date > old_end_date` and status progression.
- Move out: `move_out_requests` tracks move out scheduling, inspection pending, and deposit settlement.

---

## 17. Payments and Refunds
- Tables: `invoices`, `invoice_lines`, `payments`, `payment_allocations`, `refunds`, `refund_approvals`.
- Financial precision: `numeric(14,2)` for all monetary values.
- Invoices: Total calculated from invoice lines via trigger `trg_invoice_lines_refresh_totals`.
- Payments: Idempotent (`idempotency_key`), tracks provider transaction reference, linked to invoices via `payment_allocations`. Trigger `trg_payments_refresh_invoices` updates `paid_amount` on `invoices`.
- Refunds: Initiated against a payment and agreement, requiring employee approval via `refund_approvals`.

---

## 18. Handover and Inspection
- Tables: `handover_records`, `inspections`, `inspection_items`.
- `handover_records`: Digital record of unit key/credential handover for `check_in`, `check_out`, or `transfer`, capturing customer and staff signatures.
- `inspections`: Evaluates unit condition (`check_in`, `check_out`, `routine`, `incident`). `inspection_items` details specific fixtures and any damage compensation charges.

---

## 19. Staff Operations
- Tables: `staff_facility_assignments`, `staff_shifts`, `shift_assignments`, `staff_tasks`.
- Facility Scope: `staff_facility_assignments` defines which employee manages or works at which facility during an active date range without overlap (`trg_staff_facility_assignment_no_overlap`).
- Shift Management: Shifts assigned via `shift_assignments` (`duty_role = 'staff' | 'shift_lead' | 'manager_on_call'`).
- Task Execution: `staff_tasks` coordinates operational duties (check-in, check-out, inspection, maintenance, support).

---

## 20. Support Tickets
- Tables: `support_tickets`, `ticket_assignments`, `ticket_messages`, `ticket_attachments`, `ticket_charge_proposals`, `ticket_charge_approvals`, `service_ratings`.
- Tickets: Tied to customer, facility, agreement, and storage unit with strict composite FKs.
- Internal/External communication: `ticket_messages.is_internal` distinguishes customer vs internal staff notes.
- Surcharge workflow: When repair/special service is required, staff submits `ticket_charge_proposals`, manager approves via `ticket_charge_approvals`, which then converts to an `invoice`.
- Feedback: Customers rate closed tickets (1..5 stars) in `service_ratings`.

---

## 21. Access Control and Audit
- Tables: `access_credentials`, `authorized_access_members`, `access_events`, `audit_logs`, `login_history`, `notifications`.
- Access Credentials: PIN, QR, card, mobile credentials with cryptographic hash, expiration, and revocation dates.
- Audit Logging: `audit_logs` captures JSON diffs of `old_values` and `new_values` along with IP address and user ID for compliance.

---

## 22. Views / Keyless Entities
1. `core.UnitTypeHaTDT`: Compatibility view over `core.unit_types`.
2. `core.StorageUnitHaTDT`: Compatibility view over `core.storage_units`.
- Scaffold Mapping: Configured with `entity.HasNoKey().ToView(...)`.
- Usage: Must be treated as read-only projections. All inserts, updates, and deletes must target the base tables `unit_types` and `storage_units`.

---

## 23. Triggers
The database contains 35 active triggers enforcing core business invariants [PROVEN BY DATABASE]:
1. **Overlap Prevention (6):** `facility_rates`, `fee_rules`, `policy_versions`, `price_ranges`, `staff_facility_assignments`, `unit_allocations`.
2. **State Machine / Lifecycle Transitions (4):** `invoices`, `rental_agreements`, `reservations`, `storage_units`.
3. **Automated Calculation & Sync (2):** `invoice_lines.trg_invoice_lines_refresh_totals`, `payments.trg_payments_refresh_invoices`.
4. **Scope & Cross-Tenant Validation (23):** Ensures entities referencing agreements, units, shifts, and tickets strictly share the same facility and customer scope.

---

## 24. EF Core Scaffold Observations
- Scaffold command executed with `--schema core --no-onconfiguring --use-database-names --force`.
- Output: 59 entity files and `SelfStorageDbContext` with 139 foreign key relationships.
- All SQL types were accurately converted to modern .NET 8 types (`long`, `decimal`, `DateTimeOffset`, `DateOnly`, `TimeOnly`).
- No connection string or credentials were baked into the DbContext.

---

## 25. Risks / Important Notes
1. **Trigger Rejections:** Predictable, verified constraint/trigger failures may be translated at an appropriate persistence/business boundary into application exceptions. Presentation maps those application exceptions to HTTP. Unknown SQL, connection, timeout, permission, and trigger failures must remain unexpected errors and produce a generic HTTP 500; do not translate all SQL errors into business errors.
2. **Keyless Views:** `UnitTypeHaTDT` and `StorageUnitHaTDT` cannot be queried with `Find(id)` and cannot be tracked for updates.
3. **Composite Keys:** The current repository uses `GetByIdAsync(object[] keyValues, CancellationToken)` for both single and composite keys. Supply values in the configured primary-key order and with the corresponding CLR types.
4. **Precision Loss Prevention:** Financial amounts must remain strictly `decimal` in C# DTOs and calculations.

---

## 26. Repository Design Implications (for PROMPT 04)
1. **Generic Repository Strategy:**
   - Standard `IRepository<T>` where `T : class` should support predicate-based queries (`FirstOrDefaultAsync(Expression<Func<T, bool>>)`), `AnyAsync`, `FindAsync`, `AddAsync`, `Update`, `Delete`.
   - The current `GetByIdAsync(object[] keyValues, CancellationToken)` supports all 54 single-column PK entities and the three composite PK entities (`user_roles`, `payment_allocations`, `shift_assignments`). No type-specific overloads are needed.
2. **Read-Only / NoTracking Support:**
   - Query operations in repositories should support local `AsNoTracking()` via optional parameters or specialized query methods to optimize read performance without disabling global tracking.
3. **Specific Repositories Candidates:**
   - `IReservationRepository`: Complex availability checks, 15-minute hold validation, unit allocation checks.
   - `IStorageUnitRepository`: Filtering by facility, area, unit type, and physical/allocation status.
   - `IRentalAgreementRepository`: Contract lifecycle, renewal checks, move-out settlement.
   - `IInvoiceRepository` / `IPaymentRepository`: Invoice generation, line management, allocation tracking.
   - `IAccessControlRepository` / `IStaffRepository`: Facility scoping and active credential lookups.
4. **Transaction Management:**
   - Critical operations (e.g. converting reservation to rental agreement, allocating storage unit, processing payment allocation) require explicit database transactions to maintain consistency across tables and triggers.
