/* ============================================================================
   SelfStoragePRN222 - Function 4 database upgrade (SQL Server 2019+)

   Run this WHOLE file once in a normal SSMS query window against an EXISTING
   SelfStoragePRN222 database. SQLCMD Mode is not required. The script keeps
   all tables and business data. It is safe to rerun: a trusted, correct FK
   makes the migration a no-op.

   Function 4 schema change:
     core.refund_approvals.decided_by references core.users(id), so a system
     administrator without an employee_profile can review a refund.

   For a NEW database, run SelfStoragePRN222_SQLServer_CleanInstall.sql instead.
   That separate clean installer DROPS and recreates the database and its data.
   Do not run the clean installer on a database whose data must be preserved.
============================================================================ */

USE [SelfStoragePRN222];
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;

IF DB_NAME() <> N'SelfStoragePRN222'
    THROW 51080, 'Function 4 upgrade must run in SelfStoragePRN222.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @ApprovalsObjectId int = OBJECT_ID(N'[core].[refund_approvals]', N'U');
    DECLARE @UsersObjectId int = OBJECT_ID(N'[core].[users]', N'U');
    DECLARE @EmployeeObjectId int = OBJECT_ID(N'[core].[employee_profiles]', N'U');

    IF @ApprovalsObjectId IS NULL OR @UsersObjectId IS NULL
        THROW 51081, 'Required core.refund_approvals or core.users table is missing.', 1;

    DECLARE @DecidedByColumnId int = COLUMNPROPERTY(@ApprovalsObjectId, N'decided_by', 'ColumnId');
    DECLARE @UsersIdColumnId int = COLUMNPROPERTY(@UsersObjectId, N'id', 'ColumnId');
    DECLARE @EmployeeUserIdColumnId int = COLUMNPROPERTY(@EmployeeObjectId, N'user_id', 'ColumnId');

    IF @DecidedByColumnId IS NULL OR @UsersIdColumnId IS NULL
        THROW 51082, 'Required decided_by or users.id column is missing.', 1;

    -- Validate all existing approval rows before changing any constraint.
    IF EXISTS (
        SELECT 1
        FROM [core].[refund_approvals] AS ra
        LEFT JOIN [core].[users] AS u ON u.id = ra.decided_by
        WHERE u.id IS NULL
    )
        THROW 51083, 'An existing refund approval has no matching core.users row.', 1;

    -- Refuse an unexpected or composite FK rather than dropping it silently.
    IF EXISTS (
        SELECT 1
        FROM sys.foreign_keys AS fk
        JOIN sys.foreign_key_columns AS fkc
            ON fkc.constraint_object_id = fk.object_id
        WHERE fkc.parent_object_id = @ApprovalsObjectId
          AND fkc.parent_column_id = @DecidedByColumnId
          AND (
              (SELECT COUNT(*)
               FROM sys.foreign_key_columns AS all_columns
               WHERE all_columns.constraint_object_id = fk.object_id) <> 1
              OR NOT (
                  (fk.referenced_object_id = @UsersObjectId
                   AND fkc.referenced_column_id = @UsersIdColumnId)
                  OR (fk.referenced_object_id = @EmployeeObjectId
                      AND fkc.referenced_column_id = @EmployeeUserIdColumnId)
              )
          )
    )
        THROW 51084, 'Unexpected FK on core.refund_approvals.decided_by; inspect it manually.', 1;

    DECLARE @FkCount int;
    SELECT @FkCount = COUNT(*)
    FROM sys.foreign_keys AS fk
    JOIN sys.foreign_key_columns AS fkc
        ON fkc.constraint_object_id = fk.object_id
    WHERE fkc.parent_object_id = @ApprovalsObjectId
      AND fkc.parent_column_id = @DecidedByColumnId;

    -- The canonical, enabled and trusted FK is already the desired state.
    IF @FkCount <> 1 OR NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys AS fk
        JOIN sys.foreign_key_columns AS fkc
            ON fkc.constraint_object_id = fk.object_id
        WHERE fk.name = N'FK_refund_approvals_decided_by_users'
          AND fkc.parent_object_id = @ApprovalsObjectId
          AND fkc.parent_column_id = @DecidedByColumnId
          AND fk.referenced_object_id = @UsersObjectId
          AND fkc.referenced_column_id = @UsersIdColumnId
          AND fk.is_disabled = 0
          AND fk.is_not_trusted = 0
    )
    BEGIN
        DECLARE @FkName sysname;
        DECLARE @Sql nvarchar(max);

        -- Existing SQL Server databases may have an auto-generated FK name.
        WHILE EXISTS (
            SELECT 1
            FROM sys.foreign_keys AS fk
            JOIN sys.foreign_key_columns AS fkc
                ON fkc.constraint_object_id = fk.object_id
            WHERE fkc.parent_object_id = @ApprovalsObjectId
              AND fkc.parent_column_id = @DecidedByColumnId
        )
        BEGIN
            SELECT TOP (1) @FkName = fk.name
            FROM sys.foreign_keys AS fk
            JOIN sys.foreign_key_columns AS fkc
                ON fkc.constraint_object_id = fk.object_id
            WHERE fkc.parent_object_id = @ApprovalsObjectId
              AND fkc.parent_column_id = @DecidedByColumnId
            ORDER BY fk.name;

            SET @Sql = N'ALTER TABLE [core].[refund_approvals] DROP CONSTRAINT ' + QUOTENAME(@FkName) + N';';
            EXEC sys.sp_executesql @Sql;
        END;

        ALTER TABLE [core].[refund_approvals] WITH CHECK
        ADD CONSTRAINT [FK_refund_approvals_decided_by_users]
            FOREIGN KEY ([decided_by]) REFERENCES [core].[users] ([id])
            ON DELETE NO ACTION;
    END;

    -- Fail and roll back if the final FK is missing, disabled or untrusted.
    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys AS fk
        JOIN sys.foreign_key_columns AS fkc
            ON fkc.constraint_object_id = fk.object_id
        WHERE fk.name = N'FK_refund_approvals_decided_by_users'
          AND fkc.parent_object_id = @ApprovalsObjectId
          AND fkc.parent_column_id = @DecidedByColumnId
          AND fk.referenced_object_id = @UsersObjectId
          AND fkc.referenced_column_id = @UsersIdColumnId
          AND fk.is_disabled = 0
          AND fk.is_not_trusted = 0
    )
        THROW 51085, 'Function 4 refund approval FK validation failed.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

-- One-row confirmation for the team after a successful run.
SELECT fk.name AS constraint_name,
       OBJECT_SCHEMA_NAME(fk.referenced_object_id) + N'.' + OBJECT_NAME(fk.referenced_object_id) AS referenced_table,
       fk.is_disabled,
       fk.is_not_trusted,
       (SELECT COUNT(*) FROM [core].[refund_approvals]) AS preserved_approval_rows
FROM sys.foreign_keys AS fk
WHERE fk.parent_object_id = OBJECT_ID(N'[core].[refund_approvals]', N'U')
  AND fk.name = N'FK_refund_approvals_decided_by_users';
GO
