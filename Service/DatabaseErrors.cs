using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace NIBRAS.API.Services;

/// <summary>
/// The database errors the application answers with a controlled response instead of a
/// server error.
/// </summary>
public static class DatabaseErrors
{
    /// <summary>
    /// Whether the exception is a unique constraint violation (SQL Server 2601 or 2627). The
    /// answer never contains the value that collided, so a unique violation can be turned
    /// into a conflict without repeating the address, the phone number or any other input.
    /// </summary>
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);

    /// <summary>
    /// Whether the exception is a foreign-key violation (SQL Server 547). It is used when an
    /// upload races the deletion of a referenced row: the upload is answered with a controlled
    /// conflict instead of a server error, and the file it already wrote is removed.
    /// </summary>
    public static bool IsForeignKeyViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sql && sql.Number == 547;
}
