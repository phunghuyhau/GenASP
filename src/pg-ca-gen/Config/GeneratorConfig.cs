namespace PgCaGen.Config;

public sealed class GeneratorConfig
{
    public string ConnectionString { get; init; } = string.Empty;
    public string Schema { get; init; } = "public";
    public List<string>? Tables { get; init; }
}