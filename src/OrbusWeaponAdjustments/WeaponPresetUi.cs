using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace OrbusWeaponAdjustments;

/// <summary>
/// A dedicated, native PowerUI page inside RebornMenu's right-hand Mods panel.
/// There is NO longer any WeaponAdjustments insertion in vanilla General Settings.
/// The data store, first-import logic and class-scope validation are unchanged.
/// </summary>
public sealed class WeaponPresetUi : MonoBehaviour
{
    // Generic v1 RebornMenu page convention. Matches our BepInPlugin GUID.
    // Any other mod can export the same named constant + render method.
    public const string RebornMenuModGuid = "com.horizon.orbus.weaponadjustments";
    public WeaponPresetUi(IntPtr ptr) : base(ptr) { }

    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Static | BindingFlags.Instance;
    private static WeaponPresetUi? _instance;
    private object? _document, _root;
    private string _scope = "";
    private string _deleteId = "";
    private string _message = "";
    private string _lastFingerprint = "";
    private bool _showTrash;
    private float _nextPoll;
    private string _lastBindWarning = "";
    // Local, temporary naming editor. Nothing touches preset files until
    // "Save Name" succeeds. This is a VR-pointer fallback when Orbus's
    // native keyboard cannot be invoked on a particular client.
    private bool _editingName;
    private string _editorValue = "";
    private string _editorScope = "";
    private string? _editorPresetId;
    private static readonly string[] EditorRows =
    {
        "1234567890", "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM-_"
    };

    private void Awake() => _instance = this;
    private void OnDestroy()
    {
        if (ReferenceEquals(_instance, this)) _instance = null;
    }

    private static object? Get(object? obj, string name)
    {
        if (obj == null) return null;
        try
        {
            Type t = obj.GetType();
            return t.GetProperty(name, Flags)?.GetValue(obj) ??
                   t.GetField(name, Flags)?.GetValue(obj) ??
                   t.GetMethod("get_" + name, Flags, null, Type.EmptyTypes, null)?.Invoke(obj, null);
        }
        catch { return null; }
    }

    private static object? ById(object? doc, string id)
    {
        try
        {
            return doc?.GetType().GetMethod("getElementById", Flags, null,
                new[] { typeof(string) }, null)?.Invoke(doc, new object[] { id });
        }
        catch { return null; }
    }

    private static bool SetHtml(object? element, string html)
    {
        if (element == null) return false;
        try
        {
            Type t = element.GetType();
            PropertyInfo? property = t.GetProperty("innerHTML", Flags);
            if (property?.CanWrite == true && property.PropertyType == typeof(string))
            {
                property.SetValue(element, html);
                return true;
            }
            MethodInfo? setter = t.GetMethod("set_innerHTML", Flags,
                null, new[] { typeof(string) }, null);
            if (setter == null) return false;
            setter.Invoke(element, new object[] { html });
            return true;
        }
        catch { return false; }
    }

    private static string Escape(string? source) =>
        System.Net.WebUtility.HtmlEncode(source ?? "").Replace("'", "&#39;");

    /// <summary>
    /// Reflection entry point for RebornMenu v0.2.11+. This is intentionally
    /// an optional extension with no assembly reference from RebornMenu.
    /// </summary>
    public static bool RenderInRebornMenu(object document, object content)
    {
        if (_instance == null) return false;
        return _instance.Mount(document, content);
    }

    private bool Mount(object document, object content)
    {
        if (!SetHtml(content,
            "<div id='wa-page-root' style='width:338px;max-width:338px;" +
            "overflow-x:hidden;padding:2px 0 14px 2px;'></div>")) return false;
        object? root = ById(document, "wa-page-root");
        if (root == null) return false;
        _document = document;
        _root = root;
        _lastFingerprint = "";
        _deleteId = "";
        _showTrash = false;
        _message = "";
        _editingName = false;
        GripEngine.TryImportOnFirstMenu();
        Refresh(true);
        Plugin.ModLog.LogInfo("[WeaponAdjustments/NativeUI] Opened dedicated presets in RebornMenu Mods tab.");
        return true;
    }

    private void Update()
    {
        if (_instance == null) _instance = this;
        if (Time.realtimeSinceStartup < _nextPoll) return;
        _nextPoll = Time.realtimeSinceStartup + 0.9f;
        if (!GripEngine.Installed) GripEngine.Install();
        if (_document == null || _root == null) return;
        if (ById(_document, "wa-page-root") == null)
        {
            _document = null;
            _root = null;
            _lastFingerprint = "";
            return;
        }
        Refresh(false);
    }

    private static void RowButton(StringBuilder html, string id, string text,
                                  int left, int width, string color = "#d6c09a")
    {
        // Wrap each button in the game's actual .menu-list > li markup,
        // so the PowerUI parchment background and VR click areas match
        // RebornMenu's other controls.
        html.Append("<ul class='menu-list' style='position:absolute;top:3px;left:")
            .Append(left).Append("px;width:").Append(width)
            .Append("px;height:29px;margin:0;padding:0;list-style:none;'>")
            .Append("<li id='").Append(id).Append("' class='compact option' style='")
            .Append("display:block;width:").Append(width - 2)
            .Append("px;height:27px;margin:0;padding:0;text-align:center;")
            .Append("line-height:26px;font-size:12px;color:").Append(color)
            .Append(";'>").Append(Escape(text)).Append("</li></ul>");
    }

    private static void MenuButton(StringBuilder html, string id, string text, int width = 315)
    {
        html.Append("<ul class='menu-list' style='margin:5px 0 8px 0;padding:0;")
            .Append("list-style:none;width:").Append(width).Append("px;'>")
            .Append("<li id='").Append(id).Append("' class='compact option' style='width:")
            .Append(width - 2).Append("px;min-height:28px;margin:0;")
            .Append("padding:3px 0;font-size:14px;text-align:center;'>")
            .Append(Escape(text)).Append("</li></ul>");
    }

    private void Bind(string id, Action handler) => BindEvent(id, _ => handler());

    private void BindEvent(string id, Action<object> handler)
    {
        object? element = ById(_document, id);
        if (element == null) return;
        if (WeaponPointerEvents.Bind(element, "mousedown", evt =>
        {
            try { handler(evt); }
            catch (Exception ex)
            {
                _message = "That action couldn't be completed. Your saved presets are safe.";
                Plugin.ModLog.LogWarning("[WeaponAdjustments/NativeUI] Button action failed: " +
                    ex.GetType().Name);
                Refresh(true);
            }
        }, out string reason)) return;

        if (_lastBindWarning != id)
        {
            _lastBindWarning = id;
            Plugin.ModLog.LogWarning("[WeaponAdjustments/NativeUI] " + id +
                " could not bind VR pointer: " + reason);
        }
    }

    private void Apply(Func<string> action)
    {
        _message = action();
        Refresh(true);
    }

    private string ToggleNativeAdjustment(object evt)
    {
        // Reuse the SAME exact handler that the game's General tab invokes;
        // no grip transform manipulation and no replacement of vanilla UI.
        if (!GripEngine.TryContext(out _, out _))
            return "Select a class before adjusting its weapon.";
        try
        {
            Type? menu = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { menu = assembly.GetType("ng", false) ??
                             assembly.GetType("PlayerMenu", false); }
                catch { }
                if (menu != null) break;
            }
            if (menu == null) return "Weapon adjustment isn't ready. Try General > Adjust Weapon Grip.";
            string name = menu.Name == "ng" ? "emh" : "ToggleAdjustWeapon";
            MethodInfo? method = menu.GetMethods(Flags).FirstOrDefault(m =>
                m.Name == name && m.IsStatic && m.ReturnType == typeof(void) &&
                m.GetParameters().Length == 1);
            if (method == null ||
                !method.GetParameters()[0].ParameterType.IsInstanceOfType(evt))
                return "Couldn't open adjustment here. You can still use General > Adjust Weapon Grip.";
            method.Invoke(null, new[] { evt });
            return "Weapon adjustment toggled. Grip your weapon and press trigger to save.";
        }
        catch (Exception ex)
        {
            Plugin.ModLog.LogWarning("[WeaponAdjustments/NativeUI] Vanilla adjust handler: " +
                (ex.InnerException ?? ex).GetType().Name);
            return "Couldn't open weapon adjustment here. Use General > Adjust Weapon Grip.";
        }
    }

    private void AskForName(string currentName, string scope, string? presetId)
    {
        _editingName = true;
        _editorValue = currentName;
        _editorScope = scope;
        _editorPresetId = presetId;

        // Try Orbus's built-in keyboard first, but ALWAYS expose the
        // pointer-driven letter keys. This works even when native keyboard
        // delegate conversion or headset keyboard support isn't available.
        bool opened = WeaponPresetKeyboard.Open(currentName, input =>
        {
            if (!_editingName || _editorScope != scope ||
                _editorPresetId != presetId) return;
            _editorValue = (input ?? "").Trim();
            SaveEnteredName();
        }, out string reason);
        if (!opened)
        {
            Plugin.ModLog.LogWarning("[WeaponAdjustments/Keyboard] Native keyboard unavailable: " +
                reason);
            _message = "Orbus keyboard didn't open. Use the letter buttons below to name your preset.";
        }
        else
            _message = "Enter a name using the Orbus keyboard or the buttons below.";
        Refresh(true);
    }

    private void SaveEnteredName()
    {
        if (!_editingName) return;
        string name = _editorValue.Trim();
        if (name.Length == 0)
        {
            _message = "Enter a name before saving.";
            Refresh(true);
            return;
        }
        if (!GripEngine.TryContext(out _, out GripSnapshot current) ||
            current.Scope != _editorScope)
        {
            _message = "You changed class. Nothing was saved; open Save again.";
            _editingName = false;
            Refresh(true);
            return;
        }
        string scope = _editorScope;
        string? id = _editorPresetId;
        _editingName = false;
        Apply(() => id == null
            ? GripEngine.SaveNamed(name)
            : PresetStore.Rename(id, scope, name).message);
    }

    private void UpdateNamePreview()
    {
        // Retain the same native button nodes and their VR event handlers
        // while typing. Rebuilding ~40 buttons per keypress would keep
        // registering new IL2CPP delegates and leak native callbacks.
        string shown = _editorValue.Length == 0 ? "Enter name..." : _editorValue;
        if (!SetHtml(ById(_document, "wa-name-preview"), Escape(shown)))
            Refresh(true);
    }

    private void AddNameCharacter(char ch)
    {
        if (!_editingName || _editorValue.Length >= 48) return;
        _editorValue += ch;
        UpdateNamePreview();
    }

    private void Refresh(bool force)
    {
        if (_root == null || _document == null) return;
        bool identified = GripEngine.TryContext(out _, out GripSnapshot current);
        string scope = identified ? current.Scope : "";
        if (scope != _scope)
        {
            _scope = scope;
            _deleteId = "";
            _showTrash = false;
            _message = "";
            _editingName = false;
            force = true;
        }
        IReadOnlyList<GripPreset> active = identified
            ? PresetStore.List(scope) : Array.Empty<GripPreset>();
        IReadOnlyList<GripPreset> trash = identified && _showTrash
            ? PresetStore.Deleted(scope) : Array.Empty<GripPreset>();
        string fingerprint = scope + "|" + GripEngine.Status + "|" + GripEngine.ContextStatus +
            "|" + PresetStore.Error + "|" + _message + "|" + _deleteId + "|" + _showTrash +
            "|" + _editingName + "|" + _editorPresetId + "|" +
            string.Join(",", active.Select(p => p.Id + p.Name)) +
            "|" + string.Join(",", trash.Select(p => p.Id + p.Name));
        if (!force && fingerprint == _lastFingerprint) return;

        var html = new StringBuilder(4200);
        html.Append("<h4 style='margin:3px 0 4px 0;font-size:20px;'>Weapon Presets</h4>");
        if (!identified)
        {
            html.Append("<p style='font-size:13px;margin:6px 0;'>")
                .Append(Escape(GripEngine.ContextStatus))
                .Append(". Saves and loads are locked until the local character is verified.</p>");
        }
        else
        {
            html.Append("<p style='font-size:13px;margin:1px 0 8px 0;'>")
                .Append(Escape(current.ClassName)).Append(" &middot; ")
                .Append(Escape(current.Controller)).Append("</p>");
            bool originalProtected = PresetStore.HasOriginal(scope);
            if (originalProtected)
                html.Append("<p style='font-size:12px;color:#386034;margin:3px 0 10px 0;'>")
                    .Append("Original adjustment backed up.</p>");
            else
                html.Append("<p style='font-size:12px;color:#895520;margin:3px 0 10px 0;'>")
                    .Append("Original grip import pending; no existing grip will be overwritten.</p>");
            MenuButton(html, "wa-adjust", "Adjust Weapon Grip");
            html.Append("<p style='font-size:12px;margin:2px 0 6px;'>")
                .Append("Opens the same adjustment tool as General Settings.</p>");
            MenuButton(html, "wa-save", "Save Current Grip...");
            if (_editingName)
            {
                html.Append("<div style='width:331px;border-top:1px solid #90795c;")
                    .Append("border-bottom:1px solid #90795c;padding:7px 0;'>")
                    .Append("<p style='font-size:13px;margin:2px;'>")
                    .Append(_editorPresetId == null ? "Name your new preset:" : "Rename this preset:")
                    .Append("</p><p id='wa-name-preview' style='font-size:15px;margin:4px;")
                    .Append("background:#e0d0b0;padding:5px;min-height:20px;'>")
                    .Append(Escape(_editorValue.Length == 0 ? "Enter name..." : _editorValue))
                    .Append("</p>");
                int keyNumber = 0;
                for (int row = 0; row < EditorRows.Length; row++)
                {
                    html.Append("<div style='position:relative;height:35px;width:330px;'>");
                    for (int k = 0; k < EditorRows[row].Length; k++)
                    {
                        string label = EditorRows[row][k].ToString();
                        RowButton(html, "wa-key-" + keyNumber, label, 2 + k * 33, 31);
                        keyNumber++;
                    }
                    html.Append("</div>");
                }
                html.Append("<div style='position:relative;height:38px;width:330px;'>");
                RowButton(html, "wa-key-space", "Space", 2, 127);
                RowButton(html, "wa-key-back", "Backspace", 142, 185);
                html.Append("</div>");
                MenuButton(html, "wa-key-save", "Save Name");
                MenuButton(html, "wa-key-cancel", "Cancel");
                html.Append("</div>");
            }
            html.Append("<h4 style='font-size:15px;margin:10px 0 6px 0;'>")
                .Append("Saved presets (").Append(active.Count).Append(")</h4>");

            if (active.Count == 0)
                html.Append("<p style='font-size:12px;margin:5px 0 15px 0;'>")
                    .Append("No presets for this class yet.</p>");

            for (int i = 0; i < Math.Min(40, active.Count); i++)
            {
                GripPreset preset = active[i];
                string suffix = i.ToString(CultureInfo.InvariantCulture);
                html.Append("<div style='position:relative;width:332px;height:35px;")
                    .Append("border-bottom:1px solid #8a7455;'>");
                html.Append("<span style='position:absolute;top:7px;left:3px;")
                    .Append("width:122px;overflow:hidden;white-space:nowrap;font-size:12px;")
                    .Append(preset.Original ? "color:#447638;" : "color:#382a1c;")
                    .Append("'>").Append(Escape(preset.Name)).Append("</span>");
                RowButton(html, "wa-load-" + suffix, "Load", 131, 50);
                RowButton(html, "wa-rename-" + suffix, "Rename", 187, 64);
                RowButton(html, "wa-delete-" + suffix, "Delete", 257, 65, "#eed0aa");
                html.Append("</div>");
                if (_deleteId == preset.Id)
                {
                    html.Append("<p style='margin:8px 0 3px 0;font-size:12px;'>")
                        .Append("Delete '").Append(Escape(preset.Name))
                        .Append("' from ").Append(Escape(current.ClassName))
                        .Append("? It can be restored from Trash.</p>")
                        .Append("<div style='position:relative;width:330px;height:38px;'>");
                    RowButton(html, "wa-cancel", "Cancel", 3, 146);
                    RowButton(html, "wa-confirm", "Confirm Delete", 164, 159, "#ffceaa");
                    html.Append("</div>");
                }
            }
            if (active.Count > 40)
                html.Append("<p style='font-size:12px;'>First 40 presets shown.</p>");
            html.Append("<div style='height:8px;'></div>");
            MenuButton(html, "wa-undo", "Restore Previous Grip");
            MenuButton(html, "wa-trash-toggle", _showTrash ? "Hide Trash" : "View Trash");
            if (_showTrash)
            {
                html.Append("<h4 style='font-size:14px;margin:7px 0 2px;'>Trash</h4>");
                for (int i = 0; i < Math.Min(40, trash.Count); i++)
                {
                    GripPreset item = trash[i];
                    string key = i.ToString(CultureInfo.InvariantCulture);
                    html.Append("<div style='position:relative;width:330px;height:34px;")
                        .Append("border-bottom:1px solid #8a7455;'>")
                        .Append("<span style='position:absolute;top:7px;left:3px;")
                        .Append("width:215px;font-size:12px;overflow:hidden;white-space:nowrap;'>")
                        .Append(Escape(item.Name)).Append("</span>");
                    RowButton(html, "wa-restore-" + key, "Restore", 232, 87);
                    html.Append("</div>");
                }
            }
        }

        if (string.IsNullOrEmpty(PresetStore.Error))
            MenuButton(html, "wa-backup", "Back Up / Export Presets");
        else
            html.Append("<p style='font-size:12px;color:#a33;'>")
                .Append(Escape(PresetStore.Error)).Append("</p>");
        if (!string.IsNullOrEmpty(_message))
            html.Append("<p style='font-size:12px;margin:7px 0;color:#3c3022;'>")
                .Append(Escape(_message)).Append("</p>");
        html.Append("<p style='font-size:11px;color:#705943;margin-top:7px;'>")
            .Append("Presets are separated by class and controller. ")
            .Append("Deleted presets remain recoverable.</p>");

        if (!SetHtml(_root, html.ToString())) return;
        _lastFingerprint = fingerprint;

        if (string.IsNullOrEmpty(PresetStore.Error))
            Bind("wa-backup", () => Apply(() => PresetStore.BackupAll().message));
        if (!identified || !string.IsNullOrEmpty(PresetStore.Error)) return;

        BindEvent("wa-adjust", evt => Apply(() => ToggleNativeAdjustment(evt)));
        Bind("wa-save", () => AskForName("", scope, null));
        if (_editingName)
        {
            int keyNumber = 0;
            foreach (string row in EditorRows)
                foreach (char character in row)
                {
                    string id = "wa-key-" + keyNumber++;
                    char letter = character;
                    Bind(id, () => AddNameCharacter(letter));
                }
            Bind("wa-key-space", () => AddNameCharacter(' '));
            Bind("wa-key-back", () =>
            {
                if (_editorValue.Length > 0)
                    _editorValue = _editorValue.Substring(0, _editorValue.Length - 1);
                UpdateNamePreview();
            });
            Bind("wa-key-save", SaveEnteredName);
            Bind("wa-key-cancel", () =>
            {
                _editingName = false;
                _message = "Naming cancelled; nothing changed.";
                Refresh(true);
            });
        }
        for (int i = 0; i < Math.Min(40, active.Count); i++)
        {
            GripPreset preset = active[i];
            string key = i.ToString(CultureInfo.InvariantCulture);
            Bind("wa-load-" + key, () => Apply(() => GripEngine.Load(preset.Id)));
            Bind("wa-rename-" + key, () => AskForName(preset.Name, scope, preset.Id));
            Bind("wa-delete-" + key, () =>
            {
                _deleteId = preset.Id;
                Refresh(true);
            });
        }

        if (_deleteId.Length > 0)
        {
            Bind("wa-cancel", () => { _deleteId = ""; Refresh(true); });
            Bind("wa-confirm", () => Apply(() =>
            {
                string id = _deleteId;
                _deleteId = "";
                if (!GripEngine.TryContext(out _, out GripSnapshot check) ||
                    check.Scope != scope)
                    return "Class changed; deletion cancelled.";
                return PresetStore.Delete(id, scope).message;
            }));
        }

        Bind("wa-undo", () => Apply(GripEngine.Undo));
        Bind("wa-trash-toggle", () =>
        {
            _showTrash = !_showTrash;
            Refresh(true);
        });
        if (_showTrash)
            for (int i = 0; i < Math.Min(40, trash.Count); i++)
            {
                GripPreset item = trash[i];
                string key = i.ToString(CultureInfo.InvariantCulture);
                Bind("wa-restore-" + key, () => Apply(() =>
                {
                    if (!GripEngine.TryContext(out _, out GripSnapshot check) ||
                        check.Scope != scope)
                        return "Class changed; restore cancelled.";
                    return PresetStore.Restore(item.Id, scope).message;
                }));
            }
    }
}
