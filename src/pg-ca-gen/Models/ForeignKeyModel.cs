namespace PgCaGen.Models;

public sealed class ForeignKeyModel
{
    public required string Name { get; init; }
    public required IReadOnlyList<string> Columns { get; init; }
    public required string ReferencedSchema { get; init; }
    public required string ReferencedTable { get; init; }
    public required IReadOnlyList<string> ReferencedColumns { get; init; }
}