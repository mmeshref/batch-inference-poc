using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BatchPortal.TagHelpers;

[HtmlTargetElement("pool-badge")]
public sealed class PoolBadgeTagHelper : TagHelper
{
    public string? Value { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var pool = Value ?? string.Empty;
        var (bg, text) = pool.ToLowerInvariant() switch
        {
            "spot" => ("bg-purple", "text-light"),
            "dedicated" => ("bg-orange", "text-dark"),
            _ => ("bg-secondary", "text-light")
        };

        output.TagName = "span";
        output.Attributes.SetAttribute("class", $"badge badge-sm {bg} {text}");
        output.Content.SetContent(pool);
    }
}

