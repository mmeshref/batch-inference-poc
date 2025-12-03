using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BatchPortal.TagHelpers;

[HtmlTargetElement("status-badge")]
public sealed class StatusBadgeTagHelper : TagHelper
{
    public string? Value { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var status = Value ?? string.Empty;
        var (bg, text) = status.ToLowerInvariant() switch
        {
            "queued" => ("bg-warning", "text-dark"),
            "running" => ("bg-primary", "text-light"),
            "completed" => ("bg-success", "text-light"),
            "failed" => ("bg-danger", "text-light"),
            _ => ("bg-secondary", "text-light")
        };

        output.TagName = "span";
        output.Attributes.SetAttribute("class", $"badge badge-sm {bg} {text}");
        output.Content.SetContent(status);
    }
}

