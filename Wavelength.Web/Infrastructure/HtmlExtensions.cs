using System;
using System.Linq.Expressions;
using System.Web;
using System.Web.Mvc;
using System.Web.Mvc.Html;
using Wavelength.Web.Models;
using Wavelength.Web.Services;

namespace Wavelength.Web.Infrastructure
{
    /// <summary>Custom HTML helpers used across the views (Html.Cover, Html.StatusBadge, Html.BrutField ...).</summary>
    public static class HtmlExtensions
    {
        /// <summary>Cover art: the image, or a colored halftone block with the first letter.</summary>
        public static MvcHtmlString Cover(this HtmlHelper html, CoverVm m)
        {
            string size;
            switch (m.Size)
            {
                case "xs": size = "h-12 w-12 text-xl"; break;
                case "sm": size = "h-16 w-16 text-2xl"; break;
                case "lg": size = "aspect-square w-full text-[9rem]"; break;
                case "fill": size = "h-full w-full text-7xl"; break;
                default: size = "aspect-square w-full text-7xl"; break;
            }
            var round = m.Round ? " rounded-full" : "";
            if (!string.IsNullOrEmpty(m.Url))
            {
                var img = new TagBuilder("img");
                img.MergeAttribute("src", m.Url);
                img.MergeAttribute("alt", "");
                img.MergeAttribute("loading", "lazy");
                img.AddCssClass(size + " shrink-0 border-2 border-ink object-cover" + round);
                return MvcHtmlString.Create(img.ToString(TagRenderMode.SelfClosing));
            }
            var initial = string.IsNullOrWhiteSpace(m.Label) ? "♪" : m.Label.Trim().Substring(0, 1).ToUpperInvariant();
            var box = new TagBuilder("span");
            box.AddCssClass(size + " relative flex shrink-0 items-center justify-center overflow-hidden border-2 border-ink" + round);
            box.MergeAttribute("style", "background:" + m.Color + ";color:" + Fmt.On(m.Color));
            box.MergeAttribute("aria-hidden", "true");
            box.InnerHtml = "<span class=\"halftone absolute inset-0 opacity-20\"></span><span class=\"display relative leading-none\">" + HttpUtility.HtmlEncode(initial) + "</span>";
            return MvcHtmlString.Create(box.ToString());
        }

        /// <summary>Round-free avatar: uploaded picture or initial on a palette color.</summary>
        public static MvcHtmlString Avatar(this HtmlHelper html, string url, string name, string css)
        {
            if (!string.IsNullOrEmpty(url))
            {
                var img = new TagBuilder("img");
                img.MergeAttribute("src", url);
                img.MergeAttribute("alt", "");
                img.AddCssClass(css + " shrink-0 border-2 border-ink object-cover");
                return MvcHtmlString.Create(img.ToString(TagRenderMode.SelfClosing));
            }
            name = string.IsNullOrWhiteSpace(name) ? "?" : name.Trim();
            var initial = name.Substring(0, 1).ToUpperInvariant();
            var color = Fmt.ColorFor(name.Length + initial[0]);
            var span = new TagBuilder("span");
            span.AddCssClass(css + " display flex shrink-0 items-center justify-center border-2 border-ink");
            span.MergeAttribute("style", "background:" + color + ";color:" + Fmt.On(color));
            span.MergeAttribute("aria-hidden", "true");
            span.SetInnerText(initial);
            return MvcHtmlString.Create(span.ToString());
        }

        /// <summary>Colored badge for a track / report / message status (draft, pending, published ...).</summary>
        public static MvcHtmlString StatusBadge(this HtmlHelper html, string status)
        {
            var tag = new TagBuilder("span");
            tag.AddCssClass("status status-" + (status ?? "").ToLowerInvariant());
            tag.SetInnerText(status ?? "");
            return MvcHtmlString.Create(tag.ToString());
        }

        /// <summary>Sticker label in the landing page style.</summary>
        public static MvcHtmlString Sticker(this HtmlHelper html, string text, string bgClass = "bg-volt")
        {
            var tag = new TagBuilder("span");
            tag.AddCssClass("sticker " + bgClass);
            tag.SetInnerText(text);
            return MvcHtmlString.Create(tag.ToString());
        }

        /// <summary>A link in the nav style that is highlighted when active.</summary>
        public static MvcHtmlString NavLink(this HtmlHelper html, string text, string href, bool active)
        {
            var a = new TagBuilder("a");
            a.MergeAttribute("href", href);
            a.AddCssClass("border-2 px-3 py-1.5 font-mono text-sm font-bold uppercase transition hover:border-ink hover:bg-volt " + (active ? "border-ink bg-ink text-paper" : "border-transparent"));
            a.SetInnerText(text);
            return MvcHtmlString.Create(a.ToString());
        }

        /// <summary>
        /// Label + input + validation message for a model property, in the brutalist style.
        /// Uses the standard helpers (LabelFor, TextBoxFor/PasswordFor/TextAreaFor, ValidationMessageFor),
        /// so jQuery unobtrusive validation works on the client.
        /// </summary>
        public static MvcHtmlString BrutField<TModel, TValue>(this HtmlHelper<TModel> html, Expression<Func<TModel, TValue>> expression,
            string label, string type = "text", string placeholder = null, string autocomplete = null, int rows = 0)
        {
            var attrs = new System.Collections.Generic.Dictionary<string, object> { { "class", "field" } };
            if (placeholder != null) attrs["placeholder"] = placeholder;
            if (autocomplete != null) attrs["autocomplete"] = autocomplete;
            MvcHtmlString input;
            if (rows > 0) { attrs["rows"] = rows; attrs["class"] = "field resize-none"; input = html.TextAreaFor(expression, attrs); }
            else if (type == "password") input = html.PasswordFor(expression, attrs);
            else { attrs["type"] = type; input = html.TextBoxFor(expression, attrs); }

            return MvcHtmlString.Create(
                "<div>" +
                html.LabelFor(expression, label, new { @class = "label" }) +
                input +
                html.ValidationMessageFor(expression, null, new { @class = "err block" }) +
                "</div>");
        }

        public static string Duration(this HtmlHelper html, int seconds) { return Fmt.Duration(seconds); }
        public static string Count(this HtmlHelper html, long n) { return Fmt.Count(n); }
        public static string Ago(this HtmlHelper html, DateTime utc) { return Fmt.Ago(utc); }
    }
}
