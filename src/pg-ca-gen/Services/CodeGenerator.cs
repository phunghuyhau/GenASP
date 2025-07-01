using PgCaGen.Config;
using PgCaGen.Models;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace PgCaGen.Services;

public sealed class CodeGenerator
{
    private readonly SchemaIntrospector _introspector = new();
    private readonly TemplateRenderer _renderer = new();

    public async Task GenerateAsync(GeneratorConfig config, string outputDir, CancellationToken ct = default)
    {
        var dbModel = await _introspector.IntrospectAsync(config, ct);

        var templatesRoot = Path.Combine(AppContext.BaseDirectory, "templates");
        if (!Directory.Exists(templatesRoot))
        {
            // fallback to current directory execution context
            templatesRoot = Path.Combine(Directory.GetCurrentDirectory(), "templates");
        }
        if (!Directory.Exists(templatesRoot))
            throw new InvalidOperationException($"Templates directory not found. Looked in {templatesRoot}");

        var domainNamespace = "Domain.Entities"; // could be configurable later
        var infraNamespace = "Infrastructure.Persistence";
        var dbContextName = "AppDbContext"; // later config

        // Generate entities:
        var entityTemplatePath = Path.Combine(templatesRoot, "domain", "entity.scriban");
        var entityTemplate = await File.ReadAllTextAsync(entityTemplatePath, ct);

        foreach (var table in dbModel.Tables)
        {
            var entityName = NameHelper.ToPascalCase(NameHelper.Singularize(table.Name));
            var entityCsPath = Path.Combine(outputDir, "Domain", "Entities", $"{entityName}.cs");

            var model = new
            {
                namespace = domainNamespace,
                name = entityName,
                properties = table.Columns.Select(c => new
                {
                    Name = NameHelper.ToPascalCase(c.Name),
                    Type = MapColumnType(c)
                }).ToList()
            };

            var rendered = _renderer.Render(entityTemplate, model);
            RegionFileWriter.WriteFile(entityCsPath, rendered);
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
            _ => "string"
        };
    }
}