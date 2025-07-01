namespace PgCaGen.Models;

public sealed class ColumnModel
{
    public required string Name { get; init; }
    public required string DataType { get; init; }
    public bool IsNullable { get; init; }
    public int? MaxLength { get; init; }
    public bool IsIdentity { get; init; }
    public bool IsVersion { get; init; }
}