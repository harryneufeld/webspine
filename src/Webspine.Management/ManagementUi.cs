using System.Net;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Webspine.Content.Sqlite;
using Webspine.Core;

namespace Webspine.Management;

internal static class ManagementUi
{
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
    public static IResult Html(string title, string body, int status = 200) => Results.Content($"""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{E(title)} · webspine</title><link rel="stylesheet" href="/manage/assets/editor.css"></head><body><a class="skip" href="#main">Skip to content</a><header class="shell"><a class="wordmark" href="/manage">web<strong>spine</strong></a><span class="badge">Local workspace</span></header><main id="main" class="shell">{body}</main><footer class="shell">Your content. Your hosting. <span>Draft workspace · Publishing is not available yet.</span></footer></body></html>
        """, "text/html; charset=utf-8", statusCode: status);

    public static string Token(HttpContext context)
    {
        var tokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
        return $"<input type=\"hidden\" name=\"{E(tokens.FormFieldName)}\" value=\"{E(tokens.RequestToken)}\">";
    }
    private static string Input(string name, string label, string value, bool multiline = false)
    {
        var field = multiline ? $"<textarea id=\"{E(name)}\" name=\"{E(name)}\" rows=\"4\" required>{E(value)}</textarea>" : $"<input id=\"{E(name)}\" name=\"{E(name)}\" value=\"{E(value)}\" required>";
        return $"<label for=\"{E(name)}\">{E(label)}</label>{field}";
    }
    private static string Alert(string? message) => message is null ? "" : $"<div class=\"notice\" role=\"alert\">{E(message)}</div>";
    public static IResult Setup(HttpContext context, string? error = null, int status = 200, string title = "My website")
        => Html("Create your website", $"""
            <div class="intro"><p class="eyebrow">A place to begin</p><h1>Create your website.</h1><p>Start with a clean Home page, or explore a five-page example. Both become your editable site.</p></div>{Alert(error)}
            <form method="post" action="/manage/setup" class="panel setup">{Token(context)}{Input("title", "Website name", title)}
            <div class="choices"><label class="choice"><input type="radio" name="mode" value="blank" checked><strong>Start blank</strong><span>A Home page, ready for your own words.</span></label><label class="choice"><input type="radio" name="mode" value="demo"><strong>Use demo</strong><span>Home, Services, Products, About and Contact.</span></label></div><button type="submit">Create website</button><p class="hint">Your site is saved on this computer. Nothing is published.</p></form>
            """, status);

    public static IResult Overview(HttpContext context, ContentSnapshot snapshot, int revisions, string? error = null, int status = 200)
    {
        var rows = new StringBuilder();
        foreach (var page in snapshot.Website.Pages)
            rows.Append($"<li><div><strong>{E(page.Title)}</strong><span>{E(page.Path)}</span></div><a class=\"text-link\" href=\"/manage/pages/{E(page.Id)}\">Edit {E(page.Title)} <span aria-hidden=\"true\">↗</span></a></li>");
        return Html(snapshot.Website.Title, $"""
            <div class="intro"><p class="eyebrow">Your website</p><h1>{E(snapshot.Website.Title)}</h1><p>Edit a page, then build a preview to review your saved draft.</p></div>{Alert(error)}<div class="workspace"><section class="panel"><div class="panel-heading"><h2>Pages</h2><span>{snapshot.Website.Pages.Length} total</span></div><ul class="pages">{rows}</ul></section><aside class="panel"><h2>Review your draft</h2><p>A preview captures the current pages and images. Later edits do not change an existing preview.</p><form method="post" action="/manage/preview">{Token(context)}<input type="hidden" name="revision" value="{E(snapshot.Revision)}"><button type="submit">Build preview</button></form><p class="hint">{revisions} saved revision{(revisions == 1 ? "" : "s")} · No live publication</p><a class="text-link" href="/manage/export">Download content and images</a></aside></div>
            <section class="panel add-page"><h2>Add a page</h2><form method="post" action="/manage/pages">{Token(context)}<input type="hidden" name="revision" value="{E(snapshot.Revision)}">{Input("title", "Page name", "")}{Input("path", "Page address", "/new-page/")}<p class="hint">Use a unique address such as /news/, ending in a slash.</p>{Input("description", "Page headline", "")}<button type="submit">Add page</button></form></section>
            """, status);
    }

    public static IResult Edit(HttpContext context, ContentSnapshot snapshot, PageContent page, string? error = null, int status = 200, IFormCollection? submitted = null)
    {
        string Value(string key, string fallback) => submitted?.ContainsKey(key) == true ? submitted[key].ToString() : fallback;
        var fields = new StringBuilder();
        foreach (var section in page.Sections)
        {
            fields.Append("<fieldset><legend>" + E(section switch { TextSection => "Text", ImageSection => "Image", CtaSection => "Call to action", CardsSection => "Cards", _ => "Content" }) + "</legend>");
            foreach (var field in ContentFields.Describe(section)) fields.Append(Input("field." + field.Key, field.Label, Value("field." + field.Key, field.Value), field.Multiline));
            fields.Append("</fieldset>");
        }
        return Html("Edit " + page.Title, $"""
            <a class="text-link" href="/manage">← All pages</a><div class="intro"><p class="eyebrow">{E(page.Path)}</p><h1>Edit {E(page.Title)}</h1><p>Save your words here. Build a preview from the website overview when you are ready.</p></div>{Alert(error)}
            <form method="post" action="/manage/pages/{E(page.Id)}" class="panel editor">{Token(context)}<input type="hidden" name="revision" value="{E(Value("revision", snapshot.Revision))}">{Input("title", "Page name", Value("title", page.Title))}{Input("description", "Page headline", Value("description", page.Description))}{fields}<div class="actions"><button type="submit">Save draft</button><a class="text-link" href="/manage/pages/{E(page.Id)}">Reload latest content</a></div><p class="hint">Saving creates a revision. It does not publish your website.</p></form>
            """, status);
    }
    public static IResult Problem(string message, int status) => Html("Unable to complete", $"<div class=\"intro\"><h1>Unable to complete this step.</h1></div>{Alert(message)}<a class=\"text-link\" href=\"/manage\">Return to your website</a>", status);

    public const string Css = """
        :root{--paper:#f5f3ec;--ink:#202f30;--muted:#536564;--accent:#285c52;--line:#d5dcd6;--shell:1120px;--gutter:24px;--radius:16px}*{box-sizing:border-box}html{scrollbar-gutter:stable}@supports not(scrollbar-gutter:stable){html{overflow-y:scroll}}body{margin:0;background:var(--paper);color:var(--ink);font:16px/1.6 system-ui,sans-serif}.shell{width:min(var(--shell),calc(100% - 2 * var(--gutter)));margin-inline:auto}header{display:flex;justify-content:space-between;align-items:center;padding:25px 0;border-bottom:1px solid var(--line)}a{color:var(--accent);text-underline-offset:.25em}.wordmark{font-size:30px;letter-spacing:-.06em;text-decoration:none;color:var(--ink);font-weight:400}.wordmark strong{font-weight:800}.badge,.eyebrow{font-size:12px;letter-spacing:.1em;text-transform:uppercase;color:var(--muted)}main{padding:35px 0 0}.intro{max-width:760px;padding:15px 0 25px}h1{font:500 clamp(34px,5vw,52px)/1.12 Georgia,serif;letter-spacing:-.035em;margin:12px 0 18px}h2{font-size:22px;line-height:1.25;margin:0 0 18px}p{color:var(--muted)}.panel{background:#fffdf8;border:1px solid var(--line);border-radius:var(--radius);padding:30px}.setup,.editor{max-width:800px}.workspace{display:grid;grid-template-columns:1.6fr 1fr;gap:24px}.panel-heading{display:flex;align-items:center;justify-content:space-between;gap:15px}.panel-heading span,.hint{font-size:13px;color:var(--muted)}.pages{padding:0;margin:0;list-style:none}.pages li{display:flex;align-items:center;justify-content:space-between;gap:20px;padding:18px 0;border-top:1px solid var(--line)}.pages li span{display:block;color:var(--muted);font-size:13px}.pages li a span{display:inline}.text-link{font-weight:600}label{display:block;margin:18px 0 7px;font-weight:650}input:not([type=radio]),textarea{display:block;width:100%;font:inherit;background:white;border:1px solid #acbcb4;border-radius:8px;padding:11px 13px;color:var(--ink)}textarea{resize:vertical}input[type=hidden]{display:none!important}button{border:0;border-radius:40px;padding:13px 24px;background:var(--accent);color:white;font:650 15px system-ui;cursor:pointer;margin-top:22px}.choices{display:grid;grid-template-columns:1fr 1fr;gap:20px;margin:18px 0}.choice{border:1px solid var(--line);border-radius:12px;padding:20px;margin:0;cursor:pointer}.choice input{margin-right:10px}.choice span{display:block;margin-top:10px;font-weight:400;color:var(--muted)}.add-page{margin-top:24px;max-width:800px}.notice{background:#fff0da;border:1px solid #c9934a;border-radius:10px;padding:18px;margin-bottom:24px}.actions{display:flex;gap:25px;align-items:baseline;flex-wrap:wrap}fieldset{border:0;border-top:1px solid var(--line);padding:15px 0;margin:30px 0 0}legend{font-size:19px;font-weight:700;padding:0 15px 0 0}footer{display:flex;justify-content:space-between;gap:20px;padding:28px 0;margin-top:55px;border-top:1px solid var(--line);color:var(--muted);font-size:12px}.skip{position:absolute;top:-100px;left:12px}.skip:focus{top:12px;background:white;padding:12px;z-index:2}a:focus-visible,button:focus-visible,input:focus-visible,textarea:focus-visible{outline:3px solid #bb792a;outline-offset:4px}@media(max-width:700px){.workspace,.choices{grid-template-columns:1fr}.panel{padding:22px}.pages li{align-items:flex-start}.badge{letter-spacing:0;font-size:11px}footer{flex-direction:column;gap:5px}.wordmark{font-size:27px}}
        """;
}
