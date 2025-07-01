using System.CommandLine;
using System.Text.Json;
using PgCaGen.Config;
using PgCaGen.Services;

var rootCmd = new RootCommand("PostgreSQL Clean Architecture Generator (Phase 1 – schema introspection)");

var initCmd = new Command("init", "Create a default pgca.json config file in the current directory");
initCmd.SetHandler(() =>
{
    var path = Path.Combine(Directory.GetCurrentDirectory(), "pgca.json");
    if (File.Exists(path))
    {
        Console.WriteLine($"Config file already exists at {path}");
        return;
    }

    var defaultCfg = new GeneratorConfig
    {
        ConnectionString = "Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=secret",
        Schema = "public",
        Tables = new List<string>()
    };
    var json = JsonSerializer.Serialize(defaultCfg, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json);
    Console.WriteLine($"Created default config at {path}. Please edit the connection string before running 'introspect'.");
});

var introspectCmd = new Command("introspect", "Read pgca.json and print database schema metadata as JSON");
var prettyOpt = new Option<bool>("--pretty", "Pretty-print JSON output");
introspectCmd.AddOption(prettyOpt);
introspectCmd.SetHandler(async (bool pretty) =>
{
    var path = Path.Combine(Directory.GetCurrentDirectory(), "pgca.json");
    if (!File.Exists(path))
    {
        Console.Error.WriteLine("pgca.json not found. Run 'pg-ca-gen init' first or specify a path.");
        return;
    }

    var cfgJson = await File.ReadAllTextAsync(path);
    var cfg = JsonSerializer.Deserialize<GeneratorConfig>(cfgJson) ?? throw new InvalidOperationException("Unable to parse pgca.json");

    var introspector = new SchemaIntrospector();
    try
    {
        var dbModel = await introspector.IntrospectAsync(cfg);
        var options = new JsonSerializerOptions { WriteIndented = pretty };
        var output = JsonSerializer.Serialize(dbModel, options);
        Console.WriteLine(output);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
    }
}, prettyOpt);

rootCmd.AddCommand(initCmd);
rootCmd.AddCommand(introspectCmd);

return await rootCmd.InvokeAsync(args);