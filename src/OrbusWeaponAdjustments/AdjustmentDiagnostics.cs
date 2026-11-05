using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace OrbusWeaponAdjustments;

/// <summary>Bounded manual snapshots of possible native weapon-adjustment objects.</summary>
public sealed class AdjustmentDiagnostics : MonoBehaviour
{
    public AdjustmentDiagnostics(IntPtr pointer) : base(pointer) { }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    private const int Ctrl = 0x11, Alt = 0x12, J = 0x4A, K = 0x4B, U = 0x55;
    private const int MaxScanned = 40000, MaxCandidates = 550, MaxPrint = 220;
    private static readonly string[] Terms = {
        "weapon", "wand", "adjust", "offset", "grip", "runemage", "mage",
        "hand", "controller", "held", "inventory", "equipment", "settings",
        "menu", "slider", "canvas", "powerui", "calibrat"
    };

    private sealed class Candidate
    {
        public int Id, Score;
        public string Path = "", Name = "", Components = "";
        public Vector3 Position;
        public Quaternion Rotation;
        public bool Active;
    }

    private bool _j, _k, _u, _busy;
    private List<Candidate>? _baseline;
    private DateTime _baselineAt;

    private static bool Held(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    private static bool Has(string s, string term) =>
        s.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
    private static string Clean(string? s, int max = 110)
    {
        if (string.IsNullOrWhiteSpace(s)) return "(unnamed)";
        s = s.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();
        return s.Length > max ? s[..max] + "..." : s;
    }
    private static int Score(string s)
    {
        int n = 0;
        foreach (string term in Terms)
            if (Has(s, term)) n +=
                term is "wand" or "weapon" or "adjust" or "offset" or "grip" ? 30 : 8;
        return n;
    }
    private static string PathOf(Transform t)
    {
        var parts = new List<string>();
        Transform? node = t;
        for (int depth = 0; depth < 12 && node != null; depth++, node = node.parent)
            parts.Add(Clean(node.gameObject.name, 70));
        parts.Reverse();
        return string.Join("/", parts);
    }
    private static string NativeType(Component c)
    {
        try
        {
            IntPtr klass = IL2CPP.il2cpp_object_get_class(c.Pointer);
            if (klass != IntPtr.Zero)
            {
                string ns = IL2CPP.il2cpp_class_get_namespace_(klass) ?? "";
                string name = IL2CPP.il2cpp_class_get_name_(klass) ?? "";
                if (!string.IsNullOrWhiteSpace(name))
                    return ns.Length > 0 ? ns + "." + name : name;
            }
        }
        catch { }
        return c.GetType().FullName ?? c.GetType().Name;
    }
    private static string Components(GameObject go)
    {
        var found = new List<string>();
        try
        {
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                if (found.Count >= 16) break;
                found.Add(Clean(NativeType(c), 96));
            }
        }
        catch { }
        return string.Join(", ", found);
    }
    private static List<Candidate> Scan()
    {
        // Active scene objects only. FindObjectsOfTypeAll also includes
        // inactive prefabs; the previous snapshot was dominated by 550 of them.
        var candidates = new List<Candidate>();
        int inspected = 0;
        foreach (GameObject go in UnityEngine.Object.FindObjectsOfType<GameObject>())
        {
            if (++inspected > MaxScanned) break;
            try
            {
                if (go == null || !go.activeInHierarchy) continue;
                Transform t = go.transform;
                string path = PathOf(t);
                int score = Score(go.name) + Math.Min(40, Score(path) / 3);
                if (score == 0) continue;
                if (Has(path, "PlayerRigv4")) score += 180;
                if (Has(path, "LocalPlayer")) score += 120;
                if (Has(path, "Runemage")) score += 90;
                if (Has(path, "Controller (right)") || Has(path, "Controller (left)"))
                    score += 70;
                if (Has(path, "OtherPlayer")) score -= 140;
                if (Has(path, "TransmogStation")) score -= 110;
                candidates.Add(new Candidate {
                    Id = go.GetInstanceID(), Name = Clean(go.name), Path = path,
                    Score = score, Position = t.localPosition,
                    Rotation = t.localRotation, Active = true,
                    Components = Components(go)
                });
            }
            catch { }
        }
        return candidates.OrderByDescending(c => c.Score)
            .ThenBy(c => c.Path, StringComparer.Ordinal)
            .Take(MaxCandidates).ToList();
    }
    private static string V(Vector3 v) => $"({v.x:0.00000},{v.y:0.00000},{v.z:0.00000})";
    private static string Q(Quaternion q) => $"({q.x:0.00000},{q.y:0.00000},{q.z:0.00000},{q.w:0.00000})";
    private static string Header(string name) =>
        "ORBUS WEAPON ADJUSTMENTS - DIAGNOSTIC v0.1.2\n" +
        $"Report={name} saved={DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
        "READ ONLY: These are observed Unity object transforms, not verified saved weapon settings.\n" +
        "Object paths can contain player names. Review before sharing.\n" +
        "No UI input text, login data or network payloads collected.\n\n";
    private static void Write(string category, StringBuilder report)
    {
        string root = Path.Combine(Paths.BepInExRootPath, "WeaponAdjustmentDiagnostics");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "WeaponAdjustments_" + category + "_" +
            DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
        File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
        Plugin.ModLog.LogInfo("[WeaponAdjustments] Wrote report: " + path);
    }
    private static void Dump(StringBuilder sb, List<Candidate> objects)
    {
        sb.AppendLine("=== WEAPON / MENU / CONTROLLER OBJECT CANDIDATES ===");
        sb.AppendLine($"Objects matched: {objects.Count}; shown: {Math.Min(objects.Count, MaxPrint)}");
        foreach (Candidate c in objects.Take(MaxPrint))
        {
            sb.AppendLine($"ID={c.Id} score={c.Score} active={c.Active} {c.Path}");
            sb.AppendLine("    local=" + V(c.Position) + " rotation=" + Q(c.Rotation));
            sb.AppendLine("    components: " + c.Components);
        }
    }
    private static void TypeHints(StringBuilder sb)
    {
        sb.AppendLine("=== POSSIBLE IMPLEMENTATION TYPES AND MEMBER NAMES ===");
        if (!Plugin.IncludeTypeNames.Value) { sb.AppendLine("(disabled)"); return; }
        int assemblies = 0, matched = 0;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            string assemblyName = assembly.GetName().Name ?? "";
            if (!(Has(assemblyName, "Assembly-CSharp") || Has(assemblyName, "Orbus") ||
                  Has(assemblyName, "PowerUI") || Has(assemblyName, "Il2CppAssembly"))) continue;
            if (++assemblies > 24) break;
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).Select(t => t!).ToArray();
            }
            catch { continue; }
            int inspected = 0;
            foreach (Type t in types)
            {
                if (++inspected > 32000 || matched >= 110) break;
                string name = t.FullName ?? t.Name;
                if (!(Has(name, "weapon") || Has(name, "wand") || Has(name, "adjust") ||
                    Has(name, "offset") || Has(name, "grip") || Has(name, "settings"))) continue;
                sb.AppendLine(Clean(name, 170));
                try
                {
                    foreach (MemberInfo m in t.GetMembers(BindingFlags.Public |
                         BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                         .Where(m => Has(m.Name, "adjust") || Has(m.Name, "weapon") ||
                            Has(m.Name, "offset") || Has(m.Name, "grip") ||
                            Has(m.Name, "save") || Has(m.Name, "load") ||
                            Has(m.Name, "rotation") || Has(m.Name, "position")).Take(20))
                        sb.AppendLine("    " + m.MemberType + " " + Clean(m.Name, 120));
                }
                catch { sb.AppendLine("    (member information unavailable)"); }
                matched++;
            }
            if (matched >= 110) break;
        }
        sb.AppendLine($"Relevant assemblies: {assemblies}; matching types: {matched}");
    }

    private void Run(Action action)
    {
        _busy = true;
        try { action(); }
        catch (Exception ex) { Plugin.ModLog.LogError("[WeaponAdjustments] Snapshot failed: " + ex); }
        finally { _busy = false; }
    }
    private void Update()
    {
        if (_busy) return;
        bool modifiers = (!Plugin.RequireFocus.Value || Application.isFocused)
            && Held(Ctrl) && Held(Alt);
        bool j = modifiers && Held(J), k = modifiers && Held(K), u = modifiers && Held(U);
        if (j && !_j) Run(() => {
            _baseline = Scan();
            _baselineAt = DateTime.Now;
            Plugin.ModLog.LogInfo("[WeaponAdjustments] Baseline captured. Adjust wand, then press Ctrl+Alt+K.");
        });
        if (k && !_k) Run(Compare);
        if (u && !_u) Run(Snapshot);
        _j = j; _k = k; _u = u;
    }
    private void Snapshot()
    {
        var items = Scan();
        var sb = new StringBuilder(Header("Snapshot"));
        Dump(sb, items);
        TypeHints(sb);
        Write("Snapshot", sb);
    }
    private void Compare()
    {
        if (_baseline == null) {
            Plugin.ModLog.LogWarning("[WeaponAdjustments] No baseline yet; press Ctrl+Alt+J first.");
            return;
        }
        var after = Scan();
        var before = _baseline.ToDictionary(x => x.Id);
        var sb = new StringBuilder(Header("Compare"));
        sb.AppendLine($"Baseline captured: {_baselineAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Before={_baseline.Count}, after={after.Count}");
        int changed = 0, created = 0;
        foreach (Candidate next in after)
        {
            if (!before.TryGetValue(next.Id, out Candidate? prev))
            {
                if (++created <= 50)
                    sb.AppendLine("CREATED " + next.Path + " local=" + V(next.Position) + " rot=" + Q(next.Rotation));
                continue;
            }
            float distance = Vector3.Distance(next.Position, prev.Position);
            float angle = Quaternion.Angle(next.Rotation, prev.Rotation);
            if (distance < 0.0001f && angle < 0.08f && prev.Active == next.Active) continue;
            if (++changed <= 160)
                sb.AppendLine($"CHANGED {next.Path} dPos={distance:0.0000}m dRot={angle:0.00}deg " +
                    $"before={V(prev.Position)} {Q(prev.Rotation)} after={V(next.Position)} {Q(next.Rotation)}");
        }
        sb.AppendLine($"CHANGED={changed}; CREATED={created}; counts capped in report.");
        Dump(sb, after);
        TypeHints(sb);
        Write("Compare", sb);
    }
}
