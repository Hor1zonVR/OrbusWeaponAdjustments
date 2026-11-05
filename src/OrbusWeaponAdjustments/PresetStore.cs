using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OrbusWeaponAdjustments;

// One independent file per preset. Files never live in the RM instance or DLL folder.
internal sealed class GripPreset
{
    public int Schema { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Scope { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string Controller { get; set; } = "";
    public bool Original { get; set; }
    public string Origin { get; set; } = "user";
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public string UpdatedUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public string[] Keys { get; set; } = Array.Empty<string>(); // exact vanilla keys, local only
    public float[] Values { get; set; } = Array.Empty<float>();
    public string? FlagKey { get; set; }
    public int FlagValue { get; set; } = 1;
}

internal sealed class GripSnapshot
{
    internal string Scope = "";
    internal string ClassName = "";
    internal string Controller = "";
    internal string[] Keys = Array.Empty<string>();
    internal float[] Values = Array.Empty<float>();
    internal string? FlagKey;
    internal int FlagValue = 1;
    internal bool[] Exists = Array.Empty<bool>();
    internal bool FlagExists;

    internal GripSnapshot Copy() => new()
    {
        Scope = Scope, ClassName = ClassName, Controller = Controller,
        Keys = (string[])Keys.Clone(), Values = (float[])Values.Clone(),
        FlagKey = FlagKey, FlagValue = FlagValue,
        Exists = (bool[])Exists.Clone(), FlagExists = FlagExists
    };
}

internal sealed class GripRollbackData
{
    public int Schema { get; set; } = 1;
    public string Scope { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string Controller { get; set; } = "";
    public string[] Keys { get; set; } = Array.Empty<string>();
    public float[] Values { get; set; } = Array.Empty<float>();
    public bool[] Exists { get; set; } = Array.Empty<bool>();
    public string FlagKey { get; set; } = "";
    public bool FlagExists { get; set; }
    public int FlagValue { get; set; }
}

internal static class PresetStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    // NOT under a RebornManager-managed game instance.
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OrbusWeaponAdjustments", "Presets", "v1");
    private static readonly string Active = Path.Combine(Root, "Active");
    private static readonly string Trash = Path.Combine(Root, "Trash");
    private static readonly string Backups = Path.Combine(Root, "Backups");
    private static readonly string Originals = Path.Combine(Root, "Originals");
    private static readonly string Exports = Path.Combine(Root, "Exports");
    internal static string Location => Root;
    internal static string Error { get; private set; } = "";

    internal static void Init()
    {
        try
        {
            foreach (string folder in new[] { Root, Active, Trash, Backups, Originals, Exports })
                Directory.CreateDirectory(folder);
            // Fail closed if storage has unknown or unreadable records.
            _ = Enumerate(Active).ToArray();
            _ = Enumerate(Trash).ToArray();
            Error = "";
        }
        catch (Exception ex) { Error = "Preset storage unavailable: " + ex.GetType().Name; }
    }

    internal static string HashScope(string character, string cls, string controller)
    {
        string input = character.Trim().ToUpperInvariant() + "\n" +
                       cls.Trim().ToUpperInvariant() + "\n" + controller.Trim().ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool ValidName(string name) => name.Length >= 1 && name.Length <= 48 &&
        !name.Any(c => char.IsControl(c) || c == '<' || c == '>');
    private static bool ValidRecord(GripPreset p) =>
        p.Schema == 1 && Guid.TryParseExact(p.Id, "N", out _) &&
        ValidName(p.Name) && p.Scope.Length == 64 &&
        !string.IsNullOrWhiteSpace(p.ClassName) && !string.IsNullOrWhiteSpace(p.Controller) &&
        p.Keys.Length == 6 && p.Values.Length == 6 &&
        p.Keys.All(k => !string.IsNullOrWhiteSpace(k)) &&
        p.Keys.Distinct(StringComparer.Ordinal).Count() == 6 &&
        p.Values.All(float.IsFinite) &&
        (p.FlagKey == null || p.FlagKey.Length is > 0 and < 1024);

    private static IEnumerable<GripPreset> Enumerate(string directory)
    {
        foreach (string path in Directory.GetFiles(directory, "*.json"))
        {
            GripPreset? p = JsonSerializer.Deserialize<GripPreset>(File.ReadAllText(path));
            if (p == null || !ValidRecord(p) ||
                !string.Equals(Path.GetFileNameWithoutExtension(path), p.Id, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid preset record: " + Path.GetFileName(path));
            yield return p;
        }
    }

    private static bool Ready()
    {
        if (!string.IsNullOrEmpty(Error)) return false;
        try { Directory.CreateDirectory(Active); return true; }
        catch (Exception ex) { Error = "Storage write unavailable: " + ex.GetType().Name; return false; }
    }

    internal static IReadOnlyList<GripPreset> List(string scope)
    {
        if (!Ready()) return Array.Empty<GripPreset>();
        try { return Enumerate(Active).Where(p => p.Scope == scope)
            .OrderByDescending(p => p.Original).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch (Exception ex)
        {
            Error = "Preset file unreadable (" + ex.GetType().Name + "); changes locked to protect your files.";
            return Array.Empty<GripPreset>();
        }
    }

    internal static IReadOnlyList<GripPreset> Deleted(string scope)
    {
        if (!Ready()) return Array.Empty<GripPreset>();
        try { return Enumerate(Trash).Where(p => p.Scope == scope).ToArray(); }
        catch (Exception ex)
        {
            Error = "Trash unreadable (" + ex.GetType().Name + "); changes locked.";
            return Array.Empty<GripPreset>();
        }
    }

    private static bool ExistsOriginal(string scope) =>
        Enumerate(Active).Concat(Enumerate(Trash)).Any(x => x.Scope == scope && x.Original);

    internal static bool HasOriginal(string scope)
    {
        if (!Ready()) return false;
        try { return ExistsOriginal(scope); }
        catch (Exception ex) { Error = "Original lookup failed: " + ex.GetType().Name; return false; }
    }

    private static void WriteNew(string target, GripPreset preset)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(preset, Json));
        string temp = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            // A new record is NEVER allowed to replace another.
            File.Move(temp, target);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static (bool ok, string message) SaveNew(string name, GripSnapshot snapshot, bool original = false)
    {
        if (!Ready()) return (false, Error);
        name = name.Trim();
        if (!ValidName(name)) return (false, "Name must be 1-48 characters (no control/angle characters).");
        if (snapshot.Keys.Length != 6 || snapshot.Values.Length != 6 ||
            snapshot.Values.Any(x => !float.IsFinite(x)) ||
            snapshot.Keys.Distinct(StringComparer.Ordinal).Count() != 6)
            return (false, "Incomplete grip values; no preset saved.");
        try
        {
            IReadOnlyList<GripPreset> list = List(snapshot.Scope);
            if (!Ready()) return (false, Error);
            if (original && ExistsOriginal(snapshot.Scope)) return (true, "Original already protected.");
            if (!original && list.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                return (false, "This class already has a preset with that name.");
            var p = new GripPreset
            {
                Name = name, Scope = snapshot.Scope, ClassName = snapshot.ClassName,
                Controller = snapshot.Controller, Original = original,
                Origin = original ? "vanilla-first-import" : "user",
                Keys = (string[])snapshot.Keys.Clone(), Values = (float[])snapshot.Values.Clone(),
                FlagKey = snapshot.FlagKey, FlagValue = snapshot.FlagValue
            };
            if (!ValidRecord(p)) return (false, "Invalid scope or snapshot; nothing saved.");
            if (original)
            {
                // Protected recovery copy is written and validated before the visible preset.
                WriteNew(Path.Combine(Originals, p.Id + ".json"), p);
                _ = JsonSerializer.Deserialize<GripPreset>(
                    File.ReadAllText(Path.Combine(Originals, p.Id + ".json"))) ??
                    throw new InvalidDataException("Original backup unreadable");
            }
            WriteNew(Path.Combine(Active, p.Id + ".json"), p);
            return (true, original ? "Original grip backed up." : "Preset saved.");
        }
        catch (Exception ex) { return (false, "Save failed: " + ex.GetType().Name + ". Existing files untouched."); }
    }

    private static (bool ok, string message) Change(string id, string scope, string? newName, bool delete)
    {
        if (!Ready()) return (false, Error);
        try
        {
            GripPreset p = Enumerate(Active).Single(x => x.Id == id && x.Scope == scope);
            string src = Path.Combine(Active, p.Id + ".json");
            if (delete)
            {
                // Both a revision backup and recoverable Trash survive indefinitely.
                File.Copy(src, Path.Combine(Backups, DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") +
                    "-" + Guid.NewGuid().ToString("N") + ".json"), false);
                File.Move(src, Path.Combine(Trash, p.Id + ".json"));
                return (true, "Moved to Trash. Use Restore to undo.");
            }
            string trimmed = (newName ?? "").Trim();
            if (!ValidName(trimmed)) return (false, "Invalid preset name.");
            if (Enumerate(Active).Any(x => x.Scope == scope && x.Id != id &&
                string.Equals(x.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                return (false, "That name is already used in this class.");
            var copy = new GripPreset
            {
                Id = p.Id, Name = trimmed, Scope = p.Scope, ClassName = p.ClassName,
                Controller = p.Controller, Keys = p.Keys, Values = p.Values, FlagKey = p.FlagKey,
                FlagValue = p.FlagValue, Original = p.Original, Origin = p.Origin,
                CreatedUtc = p.CreatedUtc, UpdatedUtc = DateTime.UtcNow.ToString("O")
            };
            string backup = Path.Combine(Backups, DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") +
                "-" + Guid.NewGuid().ToString("N") + ".json");
            string tmp = src + ".rename-" + Guid.NewGuid().ToString("N");
            WriteNew(tmp, copy);
            try { File.Replace(tmp, src, backup); }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
            return (true, "Renamed; previous revision backed up.");
        }
        catch (Exception ex) { return (false, "Change failed: " + ex.GetType().Name); }
    }

    internal static (bool ok, string message) Rename(string id, string scope, string name) =>
        Change(id, scope, name, false);
    internal static (bool ok, string message) Delete(string id, string scope) =>
        Change(id, scope, null, true);

    internal static (bool ok, string message) Restore(string id, string scope)
    {
        if (!Ready()) return (false, Error);
        try
        {
            GripPreset p = Enumerate(Trash).Single(x => x.Id == id && x.Scope == scope);
            string destination = Path.Combine(Active, p.Id + ".json");
            if (File.Exists(destination)) return (false, "An active preset already has this ID.");
            if (Enumerate(Active).Any(x => x.Scope == scope &&
                string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
                return (false, "Rename the conflicting active preset before restoring.");
            File.Move(Path.Combine(Trash, p.Id + ".json"), destination);
            return (true, "Preset restored.");
        }
        catch (Exception ex) { return (false, "Restore failed: " + ex.GetType().Name); }
    }

    internal static (bool ok, string message) BackupBeforeApply(GripSnapshot before)
    {
        if (!Ready()) return (false, Error);
        if (before.Keys.Length != 6 || before.Values.Length != 6 ||
            before.Exists.Length != 6 || before.Values.Any(x => !float.IsFinite(x)) ||
            before.FlagKey == null || before.Scope.Length != 64)
            return (false, "Cannot validate previous grip before loading.");
        var record = new GripRollbackData
        {
            Scope = before.Scope, ClassName = before.ClassName, Controller = before.Controller,
            Keys = before.Keys, Values = before.Values, Exists = before.Exists,
            FlagKey = before.FlagKey, FlagExists = before.FlagExists,
            FlagValue = before.FlagValue
        };
        try
        {
            string target = Path.Combine(Backups, "before-load-" +
                DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" +
                Guid.NewGuid().ToString("N") + ".json");
            string temp = target + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record, Json));
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { fs.Write(bytes, 0, bytes.Length); fs.Flush(true); }
                File.Move(temp, target);
                GripRollbackData? check = JsonSerializer.Deserialize<GripRollbackData>(
                    File.ReadAllText(target));
                if (check == null || check.Schema != 1 ||
                    check.Keys.Length != 6 || check.Exists.Length != 6)
                    return (false, "Rollback file failed verification.");
                return (true, "Previous grip backed up.");
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        catch (Exception ex) { return (false, "Rollback backup failed: " + ex.GetType().Name); }
    }

    internal static (bool ok, string message) BackupAll()
    {
        if (!Ready()) return (false, Error);
        try
        {
            string folder = Path.Combine(Exports, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") +
                "_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            foreach (string group in new[] { "Active", "Trash", "Originals", "Backups" })
            {
                string from = Path.Combine(Root, group), to = Path.Combine(folder, group);
                Directory.CreateDirectory(to);
                foreach (string file in Directory.GetFiles(from, "*.json"))
                    File.Copy(file, Path.Combine(to, Path.GetFileName(file)), false);
            }
            return (true, "Backup exported to " + folder);
        }
        catch (Exception ex) { return (false, "Backup failed: " + ex.GetType().Name); }
    }
}
