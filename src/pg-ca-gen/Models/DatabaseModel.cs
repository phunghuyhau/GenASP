namespace PgCaGen.Models;

public sealed class DatabaseModel
{
    public required IReadOnlyList<TableModel> Tables { get; init; }
}