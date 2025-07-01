using PgCaGen.Config;
using PgCaGen.Models;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Text;

namespace PgCaGen.Services;

public sealed class CodeGenerator
{
    private readonly SchemaIntrospector _introspector = new();
    private readonly TemplateRenderer _renderer = new();

    public async Task GenerateAsync(GeneratorConfig config, string outputDir, CancellationToken ct = default)
    {
        var dbModel = await _introspector.IntrospectAsync(config, ct);

        var domainNamespace = "Domain.Entities"; // could be configurable later
        var infraNamespace = "Infrastructure.Persistence";
        var dbContextName = "AppDbContext"; // later config

        var templatesRoot = Path.Combine(AppContext.BaseDirectory, "templates");
        if (!Directory.Exists(templatesRoot))
        {
            // fallback to current directory execution context
            templatesRoot = Path.Combine(Directory.GetCurrentDirectory(), "templates");
        }
        if (!Directory.Exists(templatesRoot))
            throw new InvalidOperationException($"Templates directory not found. Looked in {templatesRoot}");

        // Generate BaseEntity once
        var baseEntityTemplatePath = Path.Combine(templatesRoot, "domain", "baseentity.scriban");
        if (File.Exists(baseEntityTemplatePath))
        {
            var baseTemplate = await File.ReadAllTextAsync(baseEntityTemplatePath, ct);
            var baseModel = new { namespace = domainNamespace };
            var baseRendered = _renderer.Render(baseTemplate, baseModel);
            var basePath = Path.Combine(outputDir, "Domain", "Entities", "BaseEntity.cs");
            RegionFileWriter.WriteFile(basePath, baseRendered);
        }

        // Generate entities:
        var entityTemplatePath = Path.Combine(templatesRoot, "domain", "entity.scriban");
        var entityTemplate = await File.ReadAllTextAsync(entityTemplatePath, ct);

        foreach (var table in dbModel.Tables)
        {
            var entityName = NameHelper.ToPascalCase(NameHelper.Singularize(table.Name));
            var entityCsPath = Path.Combine(outputDir, "Domain", "Entities", $"{entityName}.cs");

            var navs = table.ForeignKeys.Select(fk => new
            {
                Type = NameHelper.ToPascalCase(NameHelper.Singularize(fk.ReferencedTable)),
                Name = NameHelper.ToPascalCase(NameHelper.Singularize(fk.ReferencedTable))
            }).DistinctBy(n => n.Type).ToList();

            var model = new
            {
                namespace = domainNamespace,
                name = entityName,
                properties = table.Columns.Where(c => !c.IsVersion).Select(c => new
                {
                    Name = NameHelper.ToPascalCase(c.Name),
                    Type = MapColumnType(c)
                }).ToList(),
                navs = navs
            };

            var rendered = _renderer.Render(entityTemplate, model);
            RegionFileWriter.WriteFile(entityCsPath, rendered);
        }

        // Generate DTOs
        var dtoNamespace = "Application.DTOs";
        var dtoTemplatePath = Path.Combine(templatesRoot, "application", "dto.scriban");
        var dtoTemplate = await File.ReadAllTextAsync(dtoTemplatePath, ct);
        var fullDtoTemplatePath = Path.Combine(templatesRoot, "application", "fullinfo_dto.scriban");
        var fullDtoTemplate = await File.ReadAllTextAsync(fullDtoTemplatePath, ct);

        foreach (var table in dbModel.Tables)
        {
            var entityName = NameHelper.ToPascalCase(NameHelper.Singularize(table.Name));

            var navs = table.ForeignKeys.Select(fk => new
            {
                Type = NameHelper.ToPascalCase(NameHelper.Singularize(fk.ReferencedTable)),
                Name = NameHelper.ToPascalCase(NameHelper.Singularize(fk.ReferencedTable))
            }).DistinctBy(n => n.Type).ToList();

            var propList = table.Columns.Where(c => !c.IsVersion).Select(c => new
            {
                Name = NameHelper.ToPascalCase(c.Name),
                Type = MapColumnType(c)
            }).ToList();

            var dtoModel = new
            {
                namespace = dtoNamespace,
                name = entityName,
                properties = propList
            };
            var renderedDto = _renderer.Render(dtoTemplate, dtoModel);
            var dtoPath = Path.Combine(outputDir, "Application", "DTOs", $"{entityName}Dto.cs");
            RegionFileWriter.WriteFile(dtoPath, renderedDto);

            var fullModel = new
            {
                namespace = dtoNamespace,
                name = entityName,
                properties = propList,
                navs = navs
            };
            var renderedFull = _renderer.Render(fullDtoTemplate, fullModel);
            var fullPath = Path.Combine(outputDir, "Application", "DTOs", $"{entityName}FullInfoDto.cs");
            RegionFileWriter.WriteFile(fullPath, renderedFull);

            // Generate SQL query for FullInfo
            var sqlPath = Path.Combine(outputDir, "Infrastructure", "Sql", $"{entityName}FullInfo.sql");
            Directory.CreateDirectory(Path.GetDirectoryName(sqlPath)!);
            var sql = BuildFullInfoSql(table);
            File.WriteAllText(sqlPath, sql);

            // Generate sync endpoint stub
            var syncTemplatePath = Path.Combine(templatesRoot, "webapi", "syncEndpoint.scriban");
            if (File.Exists(syncTemplatePath))
            {
                var syncTemplate = await File.ReadAllTextAsync(syncTemplatePath, ct);
                var syncModel = new
                {
                    domainNamespace = domainNamespace,
                    namespace = "WebApi.Endpoints",
                    entityName = entityName,
                    entityLower = NameHelper.Singularize(table.Name).ToLowerInvariant()
                };
                var syncRendered = _renderer.Render(syncTemplate, syncModel);
                var syncPath = Path.Combine(outputDir, "WebApi", "Endpoints", $"{entityName}SyncEndpoint.cs");
                RegionFileWriter.WriteFile(syncPath, syncRendered);
            }
        }

        // Generate DbContext
        var dbContextTemplatePath = Path.Combine(templatesRoot, "infrastructure", "dbcontext.scriban");
        var dbContextTemplate = await File.ReadAllTextAsync(dbContextTemplatePath, ct);

        var dbModelForTemplate = new
        {
            namespace = infraNamespace,
            domainNamespace = domainNamespace,
            dbContextName = dbContextName,
            tables = dbModel.Tables.Select(t => new
            {
                EntityName = NameHelper.ToPascalCase(NameHelper.Singularize(t.Name)),
                EntityNamePlural = NameHelper.Pluralize(NameHelper.ToPascalCase(NameHelper.Singularize(t.Name))),
                Name = t.Name,
                Schema = t.Schema,
                PrimaryKey = t.PrimaryKey.Select(pk => new { PropertyName = NameHelper.ToPascalCase(pk) }).ToList(),
                ForeignKeys = t.ForeignKeys.Select(fk => new
                {
                    fk.Name,
                    fk.Columns,
                    fk.ReferencedTable,
                    ReferencedEntity = NameHelper.ToPascalCase(NameHelper.Singularize(fk.ReferencedTable))
                }).ToList()
            }).ToList()
        };

        var dbRendered = _renderer.Render(dbContextTemplate, dbModelForTemplate);
        var dbContextPath = Path.Combine(outputDir, "Infrastructure", "Persistence", $"{dbContextName}.cs");
        RegionFileWriter.WriteFile(dbContextPath, dbRendered);
    }

    private static string MapColumnType(ColumnModel col)
    {
        // Very small subset mapping for demonstration.
        return col.DataType switch
        {
            "integer" => "int",
            "bigint" => "long",
            "smallint" => "short",
            "uuid" => "Guid",
            "boolean" => "bool",
            "text" or "character varying" => "string",
            "timestamp without time zone" or "timestamp with time zone" => "DateTime",
            "date" => "DateTime",
            "numeric" or "decimal" => "decimal",
            "bytea" => "byte[]",
            _ => "string"
        };
    }

    private static string BuildFullInfoSql(TableModel table)
    {
        var sb = new StringBuilder();
        sb.Append("SELECT e.*");
        for (int i = 0; i < table.ForeignKeys.Count; i++)
        {
            sb.Append($", r{i}.*");
        }
        sb.AppendLine();
        sb.AppendLine($"FROM {table.Schema}.{table.Name} e");
        for (int i = 0; i < table.ForeignKeys.Count; i++)
        {
            var fk = table.ForeignKeys[i];
            sb.Append($"LEFT JOIN {fk.ReferencedSchema}.{fk.ReferencedTable} r{i} ON ");
            for (int colIndex = 0; colIndex < fk.Columns.Count; colIndex++)
            {
                if (colIndex > 0) sb.Append(" AND ");
                sb.Append($"e.{fk.Columns[colIndex]} = r{i}.{fk.ReferencedColumns[colIndex]}");
            }
            sb.AppendLine();
        }
        sb.AppendLine("WHERE 1=1 ;");
        return sb.ToString();
    }
}