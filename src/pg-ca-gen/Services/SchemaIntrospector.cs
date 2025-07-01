using Npgsql;
using PgCaGen.Config;
using PgCaGen.Models;
using System.Text.Json;

namespace PgCaGen.Services;

public sealed class SchemaIntrospector
{
    public async Task<DatabaseModel> IntrospectAsync(GeneratorConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.ConnectionString))
            throw new ArgumentException("ConnectionString is required in pgca.json");

        var tables = new Dictionary<string, TableBuilder>(StringComparer.OrdinalIgnoreCase);

        await using var conn = new NpgsqlConnection(config.ConnectionString);
        await conn.OpenAsync(ct);

        var schema = config.Schema ?? "public";

        // 1. Load columns
        const string columnsSql = @"select table_schema, table_name, column_name, is_nullable, data_type, character_maximum_length, is_identity
                                    from information_schema.columns
                                    where table_schema = @schema
                                    order by table_name, ordinal_position;";
        await using (var cmd = new NpgsqlCommand(columnsSql, conn))
        {
            cmd.Parameters.AddWithValue("schema", schema);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var tableSchema = reader.GetString(0);
                var tableName = reader.GetString(1);

                if (config.Tables is { Count: > 0 } && !config.Tables.Contains(tableName))
                    continue; // skip tables not in the list

                var columnName = reader.GetString(2);
                var isNullable = reader.GetString(3) == "YES";
                var dataType = reader.GetString(4);
                int? charMax = reader.IsDBNull(5) ? null : reader.GetInt32(5);
                var isIdentity = reader.IsDBNull(6) ? false : reader.GetString(6) == "YES";

                var key = $"{tableSchema}.{tableName}";
                if (!tables.TryGetValue(key, out var tb))
                {
                    tb = new TableBuilder(tableSchema, tableName);
                    tables[key] = tb;
                }

                tb.AddColumn(new ColumnModel
                {
                    Name = columnName,
                    IsNullable = isNullable,
                    DataType = dataType,
                    MaxLength = charMax,
                    IsIdentity = isIdentity
                });
            }
        }

        // 2. Primary keys
        const string pkSql = @"select kc.table_schema, kc.table_name, kc.column_name, kc.constraint_name
                               from information_schema.table_constraints tc
                               join information_schema.key_column_usage kc
                                 on tc.constraint_name = kc.constraint_name
                                 and tc.table_schema = kc.table_schema
                               where tc.constraint_type = 'PRIMARY KEY' and tc.table_schema = @schema
                               order by kc.table_name, kc.ordinal_position;";
        await using (var cmd = new NpgsqlCommand(pkSql, conn))
        {
            cmd.Parameters.AddWithValue("schema", schema);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var tableSchema = reader.GetString(0);
                var tableName = reader.GetString(1);
                var columnName = reader.GetString(2);

                var key = $"{tableSchema}.{tableName}";
                if (tables.TryGetValue(key, out var tb))
                {
                    tb.AddPrimaryKeyColumn(columnName);
                }
            }
        }

        // 3. Foreign keys
        const string fkSql = @"select
                                tc.table_schema,
                                tc.table_name,
                                tc.constraint_name,
                                kcu.column_name,
                                ccu.table_schema as referenced_schema,
                                ccu.table_name as referenced_table,
                                ccu.column_name as referenced_column
                              from information_schema.table_constraints tc
                              join information_schema.key_column_usage kcu
                                on tc.constraint_name = kcu.constraint_name
                                and tc.table_schema = kcu.table_schema
                              join information_schema.constraint_column_usage ccu
                                on ccu.constraint_name = tc.constraint_name
                                and ccu.table_schema = tc.table_schema
                              where tc.constraint_type = 'FOREIGN KEY' and tc.table_schema = @schema
                              order by tc.table_name, tc.constraint_name, kcu.ordinal_position;";
        await using (var cmd = new NpgsqlCommand(fkSql, conn))
        {
            cmd.Parameters.AddWithValue("schema", schema);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var tableSchema = reader.GetString(0);
                var tableName = reader.GetString(1);
                var fkName = reader.GetString(2);
                var columnName = reader.GetString(3);
                var refSchema = reader.GetString(4);
                var refTable = reader.GetString(5);
                var refColumn = reader.GetString(6);

                var key = $"{tableSchema}.{tableName}";
                if (!tables.TryGetValue(key, out var tb))
                    continue;

                tb.AddForeignKey(fkName, columnName, refSchema, refTable, refColumn);
            }
        }

        var database = new DatabaseModel
        {
            Tables = tables.Values.Select(t => t.Build()).ToList()
        };
        return database;
    }

    private sealed class TableBuilder
    {
        private readonly List<ColumnModel> _columns = new();
        private readonly HashSet<string> _pk = new();
        private readonly Dictionary<string, ForeignKeyAccumulator> _foreignKeys = new(StringComparer.OrdinalIgnoreCase);

        public string Schema { get; }
        public string Name { get; }

        public TableBuilder(string schema, string name)
        {
            Schema = schema;
            Name = name;
        }

        public void AddColumn(ColumnModel col) => _columns.Add(col);
        public void AddPrimaryKeyColumn(string colName) => _pk.Add(colName);

        public void AddForeignKey(string fkName, string colName, string refSchema, string refTable, string refCol)
        {
            if (!_foreignKeys.TryGetValue(fkName, out var acc))
            {
                acc = new ForeignKeyAccumulator(fkName, refSchema, refTable);
                _foreignKeys[fkName] = acc;
            }
            acc.AddColumn(colName, refCol);
        }

        public TableModel Build() => new()
        {
            Schema = Schema,
            Name = Name,
            Columns = _columns.ToList(),
            PrimaryKey = _pk.ToList(),
            ForeignKeys = _foreignKeys.Values.Select(fk => fk.Build()).ToList()
        };

        private sealed class ForeignKeyAccumulator
        {
            private readonly List<string> _columns = new();
            private readonly List<string> _refColumns = new();
            public string Name { get; }
            public string RefSchema { get; }
            public string RefTable { get; }

            public ForeignKeyAccumulator(string name, string refSchema, string refTable)
            {
                Name = name;
                RefSchema = refSchema;
                RefTable = refTable;
            }

            public void AddColumn(string col, string refCol)
            {
                _columns.Add(col);
                _refColumns.Add(refCol);
            }

            public ForeignKeyModel Build() => new()
            {
                Name = Name,
                Columns = _columns.ToList(),
                ReferencedSchema = RefSchema,
                ReferencedTable = RefTable,
                ReferencedColumns = _refColumns.ToList()
            };
        }
    }
}