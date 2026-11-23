using System.Net;
using System.Text.Encodings.Web;
using Gradian.Mdm.Services.DTO;
using Gradian.Mdm.Services.Interfaces;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Gradian.Mdm.Web.TagHelpers
{
    // On Edit and Bulk Update pages, shows fields for locked columns as read-only. This is display only:
    // the services keep the stored value on save, whatever the browser sends.
    [HtmlTargetElement("input", Attributes = "name")]
    [HtmlTargetElement("input", Attributes = "asp-for")]
    [HtmlTargetElement("select", Attributes = "name")]
    [HtmlTargetElement("select", Attributes = "asp-for")]
    public class LockedColumnTagHelper : TagHelper
    {
        private readonly IColumnEditService _columnEdit;

        public LockedColumnTagHelper(IColumnEditService columnEdit)
        {
            _columnEdit = columnEdit;
        }

        // Runs after the built-in input / select helpers, which write the "name" attribute for asp-for
        public override int Order => 1000;

        [HtmlAttributeNotBound]
        [ViewContext]
        public ViewContext ViewContext { get; set; } = default!;

        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            var route = ViewContext.RouteData.Values;
            var action = route["action"] as string;
            if (action is not ("Edit" or "BulkUpdate")) return;

            var table = ColumnEditCatalog.ForController(route["controller"] as string);
            if (table == null) return;

            var locked = await _columnEdit.GetLockedColumnsAsync(table.Key);
            if (locked.Count == 0) return;

            // "Items[3].ParentCompany" and "ParentCompany" both refer to the ParentCompany column
            var name = ReadAttribute(output, "name");
            if (string.IsNullOrEmpty(name) || !locked.Contains(name[(name.LastIndexOf('.') + 1)..])) return;

            // A disabled select is not posted, and a select has no read-only state
            if (output.TagName == "select") output.Attributes.SetAttribute("disabled", "disabled");
            else output.Attributes.SetAttribute("readonly", "readonly");

            output.Attributes.SetAttribute("title", "Locked by an administrator");
        }

        private static string? ReadAttribute(TagHelperOutput output, string attributeName)
        {
            if (!output.Attributes.TryGetAttribute(attributeName, out var attribute)) return null;
            if (attribute.Value is not IHtmlContent content) return attribute.Value?.ToString();

            using var writer = new StringWriter();
            content.WriteTo(writer, HtmlEncoder.Default);
            return WebUtility.HtmlDecode(writer.ToString());
        }
    }
}
