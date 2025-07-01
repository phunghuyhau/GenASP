namespace PgCaGen.Models;
using System.Collections.Generic;

public sealed class DatabaseModel
{
    public required IReadOnlyList<TableModel> Tables { get; init; }
}