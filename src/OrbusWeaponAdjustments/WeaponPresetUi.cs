using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace OrbusWeaponAdjustments;

/// <summary>
/// Native PowerUI-only extension, inserted immediately after Orbus's
/// Toggle Weapon Adjustment / Reset list. No separate canvas or wrist overlay.
/// All destructive actions have an explicit confirmation stage.
/// </summary>
public sealed class WeaponPresetUi : MonoBehaviour
{
    public WeaponPresetUi(IntPtr ptr) : base(ptr) { }

    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static;
    private object? _doc, _panel;
    private float _next;
    private string _lastRender = "";
    private string _scope = "";
    private string _deleteId = "";
    private string _draftName = "";
    private string _message = "";
    private bool _showTrash;
    private string _lastError = "";
    private static Type? Find(string name)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { Type? t = assembly.GetType(name, false); if (t != null) return t; }
            catch { }
        }
        return null;
    }

    private static object? Get(object? obj, string name)
    {
        if (obj == null) return null;
        try
        {
            Type t = obj is Type type ? type : obj.GetType();
            object? instance = obj is Type ? null : obj;
            return t.GetProperty(name, Flags)?.GetValue(instance) ??
                   t.GetField(name, Flags)?.GetValue(instance) ??
                   t.GetMethod("get_" + name, Flags, null, Type.EmptyTypes, null)?.Invoke(instance, null);
        }
        catch { return null; }
    }

    private static object? Call(object? obj, string name, string arg)
    {
        if (obj == null) return null;
        try
        {
            MethodInfo? m = obj.GetType().GetMethod(name, Flags, null, new[] { typeof(string) }, null);
            return m?.Invoke(obj, new object[] { arg });
        }
        catch { return null; }
    }
    private static object? ById(object? doc, string id) => Call(doc, "getElementById", id);
    private static string Text(object? obj, string property) => Get(obj, property)?.ToString() ?? "";

    private static object? FirstVanillaAdjustmentList(object doc)
    {
        // Do not depend on a guessed .NET element hierarchy. Find the exact
        // existing vanilla list through its documented onmousedown handlers.
        object? list = Call(doc, "getElementsByTagName", "ul");
        int length;
        try { length = Math.Min(250, Convert.ToInt32(Get(list, "length") ?? Get(list, "Count"),
            CultureInfo.InvariantCulture)); }
        catch { return null; }
        if (list == null || length <= 0) return null;
        for (int i = 0; i < length; i++)
        {
            object? item = list.GetType().GetMethod("item", Flags, null, new[] { typeof(int) }, null)?
                .Invoke(list, new object[] { i });
            string html = Text(item, "innerHTML");
            if (html.Contains("ng.emh", StringComparison.Ordinal) &&
                html.Contains("ng.emi", StringComparison.Ordinal) &&
                html.Contains("Toggle Weapon Adjustment", StringComparison.Ordinal))
                return item;
            if (html.Contains("PlayerMenu.ToggleAdjustWeapon", StringComparison.Ordinal) &&
                html.Contains("PlayerMenu.ResetAdjustWeapon", StringComparison.Ordinal))
                return item;
        }
        return null;
    }

    private static string Escape(string? text) =>
        System.Net.WebUtility.HtmlEncode(text ?? "").Replace("'", "&#39;");

    private static bool InsertAfter(object target, string html)
    {
        try
        {
            MethodInfo? m = target.GetType().GetMethod("insertAdjacentHTML", Flags,
                null, new[] { typeof(string), typeof(string) }, null);
            if (m == null) return false;
            m.Invoke(target, new object[] { "afterend", html });
            return true;
        }
        catch (Exception e)
        {
            Plugin.ModLog.LogWarning("[WeaponAdjustments/NativeUI] Insert failed: " +
                (e.InnerException ?? e).GetType().Name);
            return false;
        }
    }

    private static bool InnerHtml(object element, string html)
    {
        try
        {
            PropertyInfo? p = element.GetType().GetProperty("innerHTML", Flags);
            if (p?.CanWrite == true && p.PropertyType == typeof(string))
            {
                p.SetValue(element, html);
                return true;
            }
            MethodInfo? setter = element.GetType().GetMethod("set_innerHTML", Flags,
                null, new[] { typeof(string) }, null);
            if (setter == null) return false;
            setter.Invoke(element, new object[] { html });
            return true;
        }
        catch { return false; }
    }

    private bool Bind(string id, Action click)
    {
        object? el = ById(_doc, id);
        if (el == null) return false;
        bool okay = WeaponPointerEvents.Bind(el, "mousedown", _ =>
        {
            try { click(); }
            catch (Exception ex)
            {
                _message = "Action failed: " + ex.GetType().Name;
                Refresh(true);
            }
        }, out string reason);
        if (!okay && _lastError != id)
        {
            _lastError = id;
            Plugin.ModLog.LogWarning("[WeaponAdjustments/NativeUI] Button " + id +
                " not bound: " + reason);
        }
        return okay;
    }

    private string CurrentName()
    {
        string value = Text(ById(_doc, "wa-preset-name"), "value").Trim();
        return value.Length == 0 ? _draftName : value;
    }

    private void Action(Func<string> operation)
    {
        _message = operation();
        Refresh(true);
    }

    private void Update()
    {
        if (Time.realtimeSinceStartup < _next) return;
        _next = Time.realtimeSinceStartup + 1.1f;
        if (!GripEngine.Installed) GripEngine.Install();
        try { Ensure(); }
        catch (Exception ex)
        {
            if (_lastError != "exception")
            {
                _lastError = "exception";
                Plugin.ModLog.LogWarning("[WeaponAdjustments/NativeUI] Waiting for Settings: " +
                    ex.GetType().Name);
            }
        }
    }

    private void Ensure()
    {
        Type? menu = Find("ng") ?? Find("PlayerMenu");
        object? doc = Get(menu, menu?.Name == "ng" ? "sew" : "document");
        if (doc == null) return;
        // The PlayerMenu document can be replaced when shards change.
        object? panel = ById(doc, "wa-preset-root");
        if (panel == null)
        {
            object? ul = FirstVanillaAdjustmentList(doc);
            if (ul == null) return;
            const string placeholder =
                "<div id='wa-preset-root' style='margin-top:10px;margin-bottom:14px;'>" +
                "<h4>Weapon Presets</h4><p>Loading preset controls...</p></div>";
            if (!InsertAfter(ul, placeholder)) return;
            panel = ById(doc, "wa-preset-root");
            if (panel == null) return;
            Plugin.ModLog.LogInfo("[WeaponAdjustments/NativeUI] Inserted preset section beneath vanilla Adjust Weapon Grip.");
            _lastRender = "";
        }
        _doc = doc;
        _panel = panel;
        // First eligible Settings visit attempts a vanilla rig refresh once,
        // triggering the exact GetFloat reader hooks for initial backups.
        GripEngine.TryImportOnFirstMenu();
        if (GripEngine.TryContext(out _, out GripSnapshot ctx))
        {
            if (ctx.Scope != _scope)
            {
                _scope = ctx.Scope;
                _deleteId = "";
                _draftName = "";
                _showTrash = false;
                _message = "";
                _lastRender = "";
            }
        }
        else if (_scope != "")
        {
            _scope = "";
            _deleteId = "";
            _lastRender = "";
        }
        Refresh(false);
    }

    private static string Btn(string id, string label) =>
        "<li class='compact option' id='" + id + "'>" + Escape(label) + "</li>";

    private void Refresh(bool force)
    {
        if (_panel == null || _doc == null) return;
        bool identified = GripEngine.TryContext(out _, out GripSnapshot ctx);
        string scope = identified ? ctx.Scope : "";
        IReadOnlyList<GripPreset> active = identified ? PresetStore.List(scope) : Array.Empty<GripPreset>();
        IReadOnlyList<GripPreset> trash = identified ? PresetStore.Deleted(scope) : Array.Empty<GripPreset>();
        string indicator = identified ? ctx.ClassName + " / " + ctx.Controller :
            "Character or class not ready";
        string fingerprint = indicator + "/" + PresetStore.Error + "/" + GripEngine.Status +
            "/" + _message + "/" + _deleteId + "/" + _showTrash + "/" +
            string.Join(",", active.Select(p => p.Id + ":" + p.Name)) + "/" +
            string.Join(",", trash.Select(p => p.Id + ":" + p.Name));
        if (!force && fingerprint == _lastRender) return;
        _lastRender = fingerprint;

        var html = new StringBuilder(2400);
        html.Append("<h4>Weapon Presets</h4><p>");
        html.Append(Escape(indicator));
        html.Append(". Presets are kept separately for every class and controller.</p>");

        if (!string.IsNullOrEmpty(PresetStore.Error))
        {
            html.Append("<p style='color:#8e261f;'>").Append(Escape(PresetStore.Error))
                .Append(" Changes locked to protect preset files.</p>");
        }
        else if (!identified)
        {
            html.Append("<p>Waiting for the local character and active class. No changes made.</p>");
        }
        else
        {
            bool original = PresetStore.HasOriginal(scope);
            if (!original)
                html.Append("<p style='color:#694c2c;'>Original grip import pending. " +
                    "Equip or reload this class to detect its existing saved adjustment.</p>");
            else
                html.Append("<p>Original grip is protected. Save new presets without replacing it.</p>");
            html.Append("<p><input type='text' id='wa-preset-name' value='")
                .Append(Escape(_draftName))
                .Append("' placeholder='Name your grip preset' maxlength='48' " +
                    "style='width:75%;color:#24190f;background:#e5d7b6;padding:4px;'/></p>");
            html.Append("<ul class='menu-list'>")
                .Append(Btn("wa-save", "Save Current as New Preset"))
                .Append(Btn("wa-backup", "Back Up All Presets"))
                .Append("</ul>");
            if (active.Count == 0)
                html.Append("<p>No saved presets for this class yet.</p>");
            int count = 0;
            foreach (GripPreset preset in active.Take(80))
            {
                string key = count++.ToString(CultureInfo.InvariantCulture);
                html.Append("<p><strong>").Append(Escape(preset.Name)).Append("</strong>");
                if (preset.Original) html.Append(" (Original backup)");
                html.Append("</p><ul class='menu-list'>")
                    .Append(Btn("wa-load-" + key, "Load"))
                    .Append(Btn("wa-rename-" + key, "Rename using name above"))
                    .Append(Btn("wa-delete-" + key, "Delete"))
                    .Append("</ul>");
                if (_deleteId == preset.Id)
                {
                    html.Append("<p>Move <strong>").Append(Escape(preset.Name))
                        .Append("</strong> (").Append(Escape(ctx.ClassName))
                        .Append(") to recoverable Trash?</p><ul class='menu-list'>")
                        .Append(Btn("wa-cancel", "Cancel"))
                        .Append(Btn("wa-confirm", "Confirm Delete"))
                        .Append("</ul>");
                }
            }
            html.Append("<ul class='menu-list'>")
                .Append(Btn("wa-undo", "Restore Previous Grip"))
                .Append(Btn("wa-trash-toggle", _showTrash ? "Hide Trash" : "Show Trash"))
                .Append("</ul>");
            if (_showTrash)
            {
                int ti = 0;
                foreach (GripPreset p in trash.Take(80))
                {
                    string key = (ti++).ToString(CultureInfo.InvariantCulture);
                    html.Append("<p>").Append(Escape(p.Name)).Append(" (Trash)</p><ul class='menu-list'>")
                        .Append(Btn("wa-restore-" + key, "Restore Preset"))
                        .Append("</ul>");
                }
            }
        }
        if (!string.IsNullOrEmpty(_message)) html.Append("<p>").Append(Escape(_message)).Append("</p>");
        html.Append("<p style='font-size:13px;'>Presets and recovery copies are stored outside RebornManager updates.</p>");
        if (!InnerHtml(_panel, html.ToString()))
        {
            if (_lastError != "render")
            {
                _lastError = "render";
                Plugin.ModLog.LogWarning("[WeaponAdjustments/NativeUI] Native innerHTML setter unavailable.");
            }
            return;
        }
        if (!identified || !string.IsNullOrEmpty(PresetStore.Error)) return;
        Bind("wa-save", () =>
        {
            _draftName = CurrentName();
            Action(() => GripEngine.SaveNamed(_draftName));
        });
        Bind("wa-backup", () => Action(() => PresetStore.BackupAll().message));
        for (int i = 0; i < Math.Min(80, active.Count); i++)
        {
            GripPreset preset = active[i];
            string id = i.ToString(CultureInfo.InvariantCulture);
            Bind("wa-load-" + id, () => Action(() => GripEngine.Load(preset.Id)));
            Bind("wa-rename-" + id, () =>
            {
                _draftName = CurrentName();
                Action(() =>
                {
                    if (!GripEngine.TryContext(out _, out GripSnapshot current) || current.Scope != scope)
                        return "Class changed; rename cancelled.";
                    return PresetStore.Rename(preset.Id, scope, _draftName).message;
                });
            });
            Bind("wa-delete-" + id, () =>
            {
                _deleteId = preset.Id;
                _draftName = CurrentName();
                Refresh(true);
            });
        }
        if (_deleteId.Length > 0)
        {
            Bind("wa-cancel", () => { _deleteId = ""; Refresh(true); });
            Bind("wa-confirm", () => Action(() =>
            {
                string id = _deleteId;
                _deleteId = "";
                if (!GripEngine.TryContext(out _, out GripSnapshot current) || current.Scope != scope)
                    return "Class changed; deletion cancelled.";
                return PresetStore.Delete(id, scope).message;
            }));
        }
        Bind("wa-undo", () => Action(GripEngine.Undo));
        Bind("wa-trash-toggle", () =>
        {
            _showTrash = !_showTrash;
            _draftName = CurrentName();
            Refresh(true);
        });
        if (_showTrash)
            for (int i = 0; i < Math.Min(80, trash.Count); i++)
            {
                GripPreset preset = trash[i];
                string key = i.ToString(CultureInfo.InvariantCulture);
                Bind("wa-restore-" + key, () => Action(() =>
                {
                    if (!GripEngine.TryContext(out _, out GripSnapshot current) ||
                        current.Scope != scope) return "Class changed; restore cancelled.";
                    return PresetStore.Restore(preset.Id, scope).message;
                }));
            }
    }
}
