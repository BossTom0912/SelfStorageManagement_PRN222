using System;

namespace SelfStorageManagementSystem.DataAccess.Exceptions;

/// <summary>
/// Base exception for persistence failures within storage unit operations.
/// </summary>
public class StorageUnitPersistenceException : Exception
{
    public StorageUnitPersistenceException(string message) : base(message) { }
    public StorageUnitPersistenceException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when optimistic concurrency check fails because the storage unit was modified concurrently.
/// </summary>
public class StorageUnitConcurrencyException : StorageUnitPersistenceException
{
    public StorageUnitConcurrencyException(string message) : base(message) { }
    public StorageUnitConcurrencyException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when a unique constraint is violated (e.g., unit_code already exists within the facility, SQL error 2601/2627).
/// </summary>
public class StorageUnitUniqueConstraintException : StorageUnitPersistenceException
{
    public StorageUnitUniqueConstraintException(string message) : base(message) { }
    public StorageUnitUniqueConstraintException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when a foreign key constraint is violated (SQL error 547).
/// </summary>
public class StorageUnitForeignKeyException : StorageUnitPersistenceException
{
    public StorageUnitForeignKeyException(string message) : base(message) { }
    public StorageUnitForeignKeyException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when an area does not belong to the facility of the storage unit (trigger 51020).
/// </summary>
public class StorageUnitAreaScopeMismatchException : StorageUnitPersistenceException
{
    public StorageUnitAreaScopeMismatchException(string message) : base(message) { }
    public StorageUnitAreaScopeMismatchException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when attempting an illegal storage-unit physical_status transition (trigger 51050).
/// </summary>
public class StorageUnitInvalidStatusTransitionException : StorageUnitPersistenceException
{
    public StorageUnitInvalidStatusTransitionException(string message) : base(message) { }
    public StorageUnitInvalidStatusTransitionException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when acquiring capacity application lock times out (sp_getapplock return < 0 or SQL deadlock 1205/1222).
/// </summary>
public class StorageUnitLockTimeoutException : StorageUnitPersistenceException
{
    public StorageUnitLockTimeoutException(string message) : base(message) { }
    public StorageUnitLockTimeoutException(string message, Exception innerException) : base(message, innerException) { }
}
