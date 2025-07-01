using Scriban;
using System.Linq;

namespace PgCaGen.Services;

public sealed class TemplateRenderer
{
    private readonly Dictionary<string, Template> _cache = new();

    public string Render(string templateText, object model)
    {
        if (!_cache.TryGetValue(templateText, out var template))
        {
            template = Template.Parse(templateText);
            if (template.HasErrors)
                throw new InvalidOperationException($"Template parse error: {string.Join(", ", template.Messages.Select(m => m.Message))}");
            _cache[templateText] = template;
        }
        return template.Render(model, member => member.Name);
    }
}