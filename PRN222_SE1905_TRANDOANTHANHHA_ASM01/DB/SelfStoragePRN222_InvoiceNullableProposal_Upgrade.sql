/*
  Repair the legacy UNIQUE constraint on invoices.ticket_charge_proposal_id.
  Reservation invoices may all have NULL here. A non-NULL proposal still has
  at most one invoice. Run against the existing database; no rows are changed.
  Safe to rerun. Do not run database_init.sql to upgrade an existing database.
*/
USE [SelfStoragePRN222];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @TableId int = OBJECT_ID(N'core.invoices', N'U');
    DECLARE @ColumnId int = COLUMNPROPERTY(@TableId, N'ticket_charge_proposal_id', 'ColumnId');
    IF @TableId IS NULL OR @ColumnId IS NULL
        THROW 51100, 'Required core.invoices.ticket_charge_proposal_id column is missing.', 1;

    -- Reject a conflicting index definition instead of silently replacing it.
    IF EXISTS (
        SELECT 1 FROM sys.indexes AS i
        WHERE i.object_id = @TableId AND i.name = N'invoices_ticket_charge_proposal_id_uidx'
          AND (i.is_unique = 0 OR i.is_disabled = 1 OR i.has_filter = 0
            OR REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(i.filter_definition),
                N' ', N''), N'[', N''), N']', N''), N'(', N''), N')', N'') <> N'ticket_charge_proposal_idisnotnull'
            OR (SELECT COUNT(*) FROM sys.index_columns AS ic
                WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) <> 1
            OR NOT EXISTS (SELECT 1 FROM sys.index_columns AS ic
                WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                  AND ic.key_ordinal = 1 AND ic.column_id = @ColumnId))
    )
        THROW 51101, 'Existing invoice proposal index has an unexpected definition.', 1;

    -- Establish non-NULL uniqueness before removing the old constraint.
    IF NOT EXISTS (SELECT 1 FROM sys.indexes
        WHERE object_id = @TableId AND name = N'invoices_ticket_charge_proposal_id_uidx')
        CREATE UNIQUE INDEX invoices_ticket_charge_proposal_id_uidx
            ON core.invoices(ticket_charge_proposal_id)
            WHERE ticket_charge_proposal_id IS NOT NULL;

    DECLARE @ConstraintName sysname, @Sql nvarchar(max);
    WHILE EXISTS (
        SELECT 1 FROM sys.key_constraints AS kc
        JOIN sys.index_columns AS ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
        WHERE kc.parent_object_id = @TableId AND kc.type = N'UQ' AND ic.column_id = @ColumnId
          AND (SELECT COUNT(*) FROM sys.index_columns AS k
               WHERE k.object_id = kc.parent_object_id AND k.index_id = kc.unique_index_id AND k.key_ordinal > 0) = 1
    )
    BEGIN
        SELECT TOP (1) @ConstraintName = kc.name
        FROM sys.key_constraints AS kc
        JOIN sys.index_columns AS ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
        WHERE kc.parent_object_id = @TableId AND kc.type = N'UQ' AND ic.column_id = @ColumnId
          AND (SELECT COUNT(*) FROM sys.index_columns AS k
               WHERE k.object_id = kc.parent_object_id AND k.index_id = kc.unique_index_id AND k.key_ordinal > 0) = 1;
        SET @Sql = N'ALTER TABLE core.invoices DROP CONSTRAINT ' + QUOTENAME(@ConstraintName) + N';';
        EXEC sys.sp_executesql @Sql;
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT name, is_unique, has_filter, filter_definition
FROM sys.indexes
WHERE object_id = OBJECT_ID(N'core.invoices') AND name = N'invoices_ticket_charge_proposal_id_uidx';
