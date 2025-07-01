namespace PgCaGen.Models;

public sealed class TableModel
{
    public required string Schema { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<ColumnModel> Columns { get; init; }
    public required IReadOnlyList<string> PrimaryKey { get; init; }
    public required IReadOnlyList<ForeignKeyModel> ForeignKeys { get; init; }
}