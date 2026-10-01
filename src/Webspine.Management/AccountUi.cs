using System.Net;

namespace Webspine.Management;

internal static class AccountUi
{
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string RoleOptions(string selected) => string.Join("", AccountAdministration.Roles.Select(role => $"<option value=\"{role}\"{(role == selected ? " selected" : "")}>{role}</option>"));
    private static string Notice(string? error) => error is null ? "" : $"<div class=\"notice\" role=\"alert\">{E(error)}</div>";
    private static string PasswordField(string id, string label, string autocomplete = "new-password") => $"<label for=\"{id}\">{label}</label><input id=\"{id}\" name=\"{id}\" type=\"password\" autocomplete=\"{autocomplete}\" required>";
    public static IResult Users(HttpContext context, IReadOnlyCollection<ManagedAccount> accounts, string? error = null, int status = 200)
    {
        var rows = string.Join("", accounts.Select(account => $"""
            <li><div><strong>{E(account.Name)}</strong><span>{E(account.Role)} · {(account.Disabled ? "Disabled" : "Enabled")}</span></div>{(account.Owner || account.Id == context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ? "<span>Protected account</span>" : $"<form method=\"post\" action=\"/manage/users/{E(account.Id)}\">{ManagementUi.Token(context)}<input type=\"hidden\" name=\"expectedStamp\" value=\"{E(account.Version)}\"><label for=\"role-{E(account.Id)}\">Role</label><select id=\"role-{E(account.Id)}\" name=\"role\">{RoleOptions(account.Role)}</select><label class=\"scope\"><input type=\"checkbox\" name=\"disabled\" value=\"true\"{(account.Disabled ? " checked" : "")}>Disable account</label><button type=\"submit\">Save account</button></form>")}</li>
            """));
        return ManagementUi.Html("People and access", $"""
            <a href="/manage">← All pages</a><div class="intro"><h1>People and access.</h1><p>Editors create and edit pages and build previews. Reviewers read content and view shared preview links. Operators also manage website settings, apps and accounts.</p></div>{Notice(error)}<section class="panel"><h2>Accounts</h2><ul class="pages">{rows}</ul></section><form class="panel setup" method="post" action="/manage/users">{ManagementUi.Token(context)}<h2>Add a person</h2><label for="username">Username</label><input id="username" name="username" required>{PasswordField("password", "Initial password")}<p class="hint">At least 12 characters, with uppercase, lowercase, a number and a symbol. Share privately; the person can change it after signing in.</p><label for="role">Role</label><select id="role" name="role">{RoleOptions("Editor")}</select><button type="submit">Create account</button></form>
            """, status);
    }
    public static IResult Password(HttpContext context, string? error = null, int status = 200) => ManagementUi.Html("Change password", $"""
        <a href="/manage">← All pages</a><div class="intro"><h1>Change your password.</h1><p>This invalidates your other sessions and app credentials. Your current session stays signed in.</p></div>{Notice(error)}<form class="panel editor" method="post" action="/manage/account/password">{ManagementUi.Token(context)}{PasswordField("current", "Current password", "current-password")}{PasswordField("password", "New password")}{PasswordField("confirm", "Repeat new password")}<p class="hint">At least 12 characters, with uppercase, lowercase, a number and a symbol.</p><button type="submit">Change password</button></form>
        """, status);
}
