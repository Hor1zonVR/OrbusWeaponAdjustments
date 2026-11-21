using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

// Read-only, OFFLINE inspection of the BepInEx-generated managed interop.
// This program never loads or executes the game's code. It only reads PE metadata.
// No PlayerPrefs, player IDs, network calls, hook installation or game changes.
internal static class Program
{
    private static readonly string[] KnownTypes =
    {
        "cmi", "cgr", "cbn", "ceh", "cei", "ng", "my", "cle",
        "MagicalActual.WeaponAdjuster",
        "MagicalActual.RigBasedAdjustment",
        "MagicalActual.PlayerAvatar",
        "MagicalActual.PlayerCharacter",
        "MagicalActual.PlayerClass",
        "PlayerMenu",
        // Candidates found in the Nov 21 Renewed interop, NOT verified mappings.
        "com", "ciu"
    };

    private sealed record Member(string Name, string Type);
    private sealed record ExaminedType(string Name, string BaseType,
        List<Member> Data, List<string> Methods);

    private static int Main(string[] args)
    {
        if (args.Length != 1 &&
            (args.Length != 3 ||
             (args[1] != "--focus" && args[1] != "--only")))
        {
            Console.Error.WriteLine("Usage: dotnet run --project tools/InteropAudit -- \"FULL_GAME_DIRECTORY\" [--focus or --only \"cac,cxe,cnw\"]");
            return 2;
        }
        bool only = args.Length == 3 && args[1] == "--only";
        string[] requestedFocus = args.Length == 3
            ? args[2].Split(',', StringSplitOptions.RemoveEmptyEntries |
                                 StringSplitOptions.TrimEntries)
            : Array.Empty<string>();
        if (requestedFocus.Length > 20 || requestedFocus.Any(n =>
                n.Length == 0 || n.Length > 90 ||
                n.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '.' or '+' or '`'))))
        {
            Console.Error.WriteLine("Invalid focus list: up to 20 type identifiers.");
            return 2;
        }

        string dll = Path.Combine(args[0], "BepInEx", "interop", "Assembly-CSharp.dll");
        if (!File.Exists(dll))
        {
            Console.Error.WriteLine("No Assembly-CSharp.dll in this game's BepInEx/interop folder.");
            Console.Error.WriteLine("Run the intended Orbus installation with BepInEx once, then retry.");
            return 3;
        }

        try
        {
            using var stream = File.OpenRead(dll);
            using var hasher = SHA256.Create();
            string interopHash = Convert.ToHexString(hasher.ComputeHash(stream));
            stream.Position = 0;
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
            {
                Console.Error.WriteLine("Interop DLL contains no readable .NET metadata.");
                return 4;
            }
            MetadataReader reader = pe.GetMetadataReader();
            var report = new StringBuilder();
            report.AppendLine("ORBUS WEAPON INTEROP AUDIT — READ ONLY");
            report.AppendLine("Assembly: " + Path.GetFileName(dll));
            report.AppendLine("Types in assembly: " + reader.TypeDefinitions.Count);
            report.AppendLine("Interop last modified (UTC): " + File.GetLastWriteTimeUtc(dll).ToString("O"));
            report.AppendLine("Interop SHA-256: " + interopHash);
            string gameBinary = Path.Combine(args[0], "GameAssembly.dll");
            if (File.Exists(gameBinary))
            {
                var gameInfo = new FileInfo(gameBinary);
                report.AppendLine("GameAssembly.dll length: " + gameInfo.Length);
                report.AppendLine("GameAssembly.dll modified (UTC): " +
                    gameInfo.LastWriteTimeUtc.ToString("O"));
            }
            report.AppendLine("IMPORTANT: This audit reads GENERATED interop. After a Spades update,");
            report.AppendLine("ensure BepInEx has regenerated it for that exact game build.");
            report.AppendLine("No game state or preference values inspected.");
            report.AppendLine();

            var known = new Dictionary<string, ExaminedType>(StringComparer.Ordinal);
            var weaponCandidates = new List<ExaminedType>();
            var rigCandidates = new List<ExaminedType>();
            var menuCandidates = new List<ExaminedType>();
            var avatarCandidates = new List<ExaminedType>();
            var allTypes = new List<ExaminedType>();
            var strongAdjusters = new List<ExaminedType>();
            var strongRigs = new List<ExaminedType>();

            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                TypeDefinition td = reader.GetTypeDefinition(handle);
                string full = FullName(reader, td);
                // Property accessors in generated Il2CppInterop code usually represent
                // native fields, so inspect properties as well as ordinary fields.
                bool named = KnownTypes.Contains(full, StringComparer.Ordinal);
                List<Member> data = GetData(reader, td);
                bool weaponShape = HasShape(data,
                    new[] { "REF", "REF", "Single", "Boolean" });
                bool rigShape = HasShape(data,
                    new[] { "Vector3", "Vector3", "Quaternion",
                        "Vector3", "Vector3", "Vector3", "Vector3",
                        "Vector3", "Vector3", "Boolean" });
                bool menuShape = data.Any(m =>
                    m.Type.Contains("Dom.Document", StringComparison.OrdinalIgnoreCase) ||
                    m.Type.Contains("HtmlDocument", StringComparison.OrdinalIgnoreCase) ||
                    m.Type.Contains("PowerUI.Document", StringComparison.OrdinalIgnoreCase));
                // Community and earlier Renewed avatar progression fields:
                // xp, neededxp, supercharge, active flag. Pure hypothesis.
                bool avatarShape = HasShape(data,
                    new[] { "Int32", "Int32", "UInt16", "Boolean" }) ||
                    HasShape(data, new[] { "Boolean", "GameObject", "GameObject",
                        "GameObject", "GameObject", "Transform", "Transform" });
                // Keep the full class index so we can show which player classes
                // reference the candidates, without executing any game code.
                var entry = new ExaminedType(full, TypeName(reader, td.BaseType),
                    data, td.GetMethods().Select(m => MethodSummary(reader, m)).ToList());
                allTypes.Add(entry);
                if (named) known[full] = entry;
                if (weaponShape) weaponCandidates.Add(entry);
                if (rigShape) rigCandidates.Add(entry);
                if (menuShape) menuCandidates.Add(entry);
                if (avatarShape) avatarCandidates.Add(entry);
                bool isBehaviour = entry.BaseType == "UnityEngine.MonoBehaviour";
                bool hasStartUpdate = entry.Methods.Any(m => m.StartsWith("Start[",
                    StringComparison.Ordinal)) &&
                    entry.Methods.Any(m => m.StartsWith("Update[",
                        StringComparison.Ordinal));
                if (isBehaviour && data.Count == 4 && weaponShape && hasStartUpdate)
                    strongAdjusters.Add(entry);
                if (isBehaviour && data.Count == 10 && rigShape &&
                    entry.Methods.Any(m => m.StartsWith("Reset[",
                        StringComparison.Ordinal)))
                    strongRigs.Add(entry);
            }

            if (!only)
            {
            report.AppendLine("=== EXPECTED CLASS NAMES ===");
            foreach (string name in KnownTypes)
            {
                if (!known.TryGetValue(name, out var type))
                {
                    report.AppendLine(name + " = NOT FOUND");
                    continue;
                }
                WriteType(report, type, maxMethods: 28, maxData: 30);
            }

            report.AppendLine();
            report.AppendLine("=== STRONG STRUCTURAL CANDIDATES (STILL NOT VERIFIED) ===");
            report.AppendLine("Weapon adjuster: MonoBehaviour, EXACT four fields with two");
            report.AppendLine("references + float + bool, and Start/Update. Count: " +
                strongAdjusters.Count);
            foreach (var t in strongAdjusters.Take(20))
                WriteType(report, t, maxMethods: 70, maxData: 70);
            report.AppendLine("Rig adjustment: MonoBehaviour, EXACT ten-field shape");
            report.AppendLine("including Quaternion + bool and Reset. Count: " +
                strongRigs.Count);
            foreach (var t in strongRigs.Take(20))
                WriteType(report, t, maxMethods: 70, maxData: 70);
            report.AppendLine();
            report.AppendLine("=== POSSIBLE WEAPON ADJUSTER SHAPES (NOT VERIFIED) ===");
            report.AppendLine("Looking for two adjacent reference-like members, Single, Boolean.");
            report.AppendLine("Candidates: " + weaponCandidates.Count);
            foreach (var t in weaponCandidates.Take(60))
                WriteType(report, t, maxMethods: 40, maxData: 35);

            report.AppendLine();
            report.AppendLine("=== POSSIBLE RIG-ADJUSTMENT SHAPES (NOT VERIFIED) ===");
            report.AppendLine("Looking for the ten consecutive expected native member types.");
            report.AppendLine("Candidates: " + rigCandidates.Count);
            foreach (var t in rigCandidates.Take(50))
                WriteType(report, t, maxMethods: 60, maxData: 30);

            report.AppendLine();
            report.AppendLine("=== POWERUI DOCUMENT OWNER CANDIDATES ===");
            report.AppendLine("Native menu candidates containing document-like properties.");
            report.AppendLine("Candidates: " + menuCandidates.Count);
            foreach (var t in menuCandidates.Take(80))
                WriteType(report, t, maxMethods: 90, maxData: 80);

            report.AppendLine();
            report.AppendLine("=== PLAYER AVATAR PROGRESSION SHAPES ===");
            report.AppendLine("Possible xp, needed-xp, supercharge, active-field progression quartet.");
            report.AppendLine("Candidates: " + avatarCandidates.Count);
            foreach (var t in avatarCandidates.Take(80))
                WriteType(report, t, maxMethods: 80, maxData: 90);

            report.AppendLine();
            report.AppendLine("=== REFERENCES TO HIGH-CONFIDENCE CANDIDATES ===");
            report.AppendLine("Find owners of candidate objects to map PlayerAvatar /");
            report.AppendLine("PlayerCharacter / active class relationships.");
            foreach (var candidate in strongAdjusters.Concat(strongRigs)
                .Concat(menuCandidates.Take(10)).Concat(avatarCandidates.Take(20))
                .DistinctBy(x => x.Name).Take(45))
            {
                var owners = allTypes.SelectMany(t => t.Data
                    .Where(m => m.Type == "class " + candidate.Name ||
                                m.Type == candidate.Name)
                    .Select(m => t.Name + "." + m.Name + ":" + m.Type))
                    .Take(50).ToArray();
                if (owners.Length == 0) continue;
                report.AppendLine(candidate.Name + " referenced by (" + owners.Length +
                    " displayed): " + string.Join("; ", owners));
            }

            report.AppendLine();
            }

            report.AppendLine("=== COMPLETE FOCUSED STRUCTURAL TYPE MAP ===");
            report.AppendLine("This is full METADATA (no player values), including");
            report.AppendLine("all fields/properties and all non-accessor methods.");
            var focus = new HashSet<string>(requestedFocus, StringComparer.Ordinal);
            // These selectors do not depend on the obfuscated type names.
            if (!only)
            {
                foreach (var t in strongAdjusters.Concat(strongRigs).Concat(avatarCandidates))
                    focus.Add(t.Name);
            // Owners of an Avatar that are MonoBehaviours, especially a
            // PlayerCharacter-like singleton with PowerUI components.
            foreach (var avatar in avatarCandidates)
            {
                foreach (var owner in allTypes.Where(t =>
                    t.BaseType == "UnityEngine.MonoBehaviour" &&
                    t.Data.Any(m => m.Type == "class " + avatar.Name ||
                                    m.Type == avatar.Name)))
                    focus.Add(owner.Name);
            }
            // Among multiple UI owners, favour a large interface controller
            // with a document field and dozens of UIEvent handlers.
            foreach (var candidate in menuCandidates.Where(t =>
                t.Methods.Count(m => m.Contains("PowerUI.UIEvent",
                    StringComparison.Ordinal)) >= 25))
                focus.Add(candidate.Name);
            }
            report.AppendLine("Selected: " + string.Join(", ", focus.OrderBy(x => x)));
            foreach (string name in focus.OrderBy(x => x, StringComparer.Ordinal))
            {
                var match = allTypes.FirstOrDefault(t => t.Name == name);
                if (match == null)
                {
                    report.AppendLine(name + " — not present");
                    continue;
                }
                WriteDetailedType(report, match);
                // Candidate player/class fields may point to other obfuscated
                // classes. Show their type metadata next if further research
                // indicates their roles; do NOT infer field meaning from a name.
            }

            report.AppendLine();
            report.AppendLine("Candidates are ONLY hypotheses; do not activate hooks or preset writes");
            report.AppendLine("until mappings are independently verified against the current client.");

            string reportPath = Path.Combine(Path.GetTempPath(),
                "OrbusWeaponInteropAudit_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".txt");
            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
            Console.WriteLine("Read-only audit completed.");
            Console.WriteLine("Saved report: " + reportPath);
            Console.WriteLine("Strong adjuster candidates: " + strongAdjusters.Count);
            Console.WriteLine("Strong rig candidates: " + strongRigs.Count);
            Console.WriteLine("Potential weapon-shaped types: " + weaponCandidates.Count);
            Console.WriteLine("Potential rig-shaped types: " + rigCandidates.Count);
            Console.WriteLine("Potential document-owner types: " + menuCandidates.Count);
            Console.WriteLine("Potential avatar-progression types: " + avatarCandidates.Count);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Audit failed (" + ex.GetType().Name + "): " + ex.Message);
            return 1;
        }
    }


    private static string MethodSummary(MetadataReader r, MethodDefinitionHandle handle)
    {
        MethodDefinition method = r.GetMethodDefinition(handle);
        string name = r.GetString(method.Name);
        string kind = (method.Attributes & MethodAttributes.Static) != 0 ? "static" : "instance";
        try
        {
            BlobReader reader = r.GetBlobReader(method.Signature);
            byte header = reader.ReadByte();
            if ((header & 0x10) != 0) reader.ReadCompressedInteger(); // generic arity
            int count = reader.ReadCompressedInteger();
            string result = ReadSignatureType(r, ref reader);
            var parameters = new List<string>();
            for (int i = 0; i < count && i < 40; i++)
                parameters.Add(ReadSignatureType(r, ref reader));
            return name + "[args=" + count + ",types=(" +
                string.Join("|", parameters) + "),ret=" + result + "," + kind + "]";
        }
        catch
        {
            return name + "[" + kind + ",signature-unavailable]";
        }
    }

    private static void WriteDetailedType(StringBuilder sb, ExaminedType t)
    {
        sb.AppendLine("TYPE " + t.Name + " : " + t.BaseType);
        sb.AppendLine("FIELDS/PROPERTIES (" + t.Data.Count + ") — in metadata order:");
        for (int i = 0; i < t.Data.Count; i++)
            sb.AppendLine("  #" + i.ToString("D3") + " " +
                          t.Data[i].Name + " : " + t.Data[i].Type);
        var gameplay = t.Methods.Where(x =>
            !x.StartsWith("get_", StringComparison.Ordinal) &&
            !x.StartsWith("set_", StringComparison.Ordinal) &&
            !x.StartsWith(".ctor[", StringComparison.Ordinal) &&
            !x.StartsWith(".cctor[", StringComparison.Ordinal)).ToList();
        sb.AppendLine("NON-ACCESSOR METHODS (" + gameplay.Count +
                      ") — in metadata order:");
        for (int i = 0; i < gameplay.Count; i++)
            sb.AppendLine("  #" + i.ToString("D3") + " " + gameplay[i]);
        sb.AppendLine();
    }

    private static void WriteType(StringBuilder sb, ExaminedType t,
        int maxMethods, int maxData)
    {
        sb.AppendLine(t.Name + " : " + t.BaseType);
        sb.AppendLine("  Members (" + t.Data.Count + "): " +
            string.Join("; ", t.Data.Take(maxData).Select(x => x.Name + ":" + x.Type)));
        // Accessor methods are already represented by Data. Hiding them
        // keeps the actual native gameplay callbacks readable in reports.
        var useful = t.Methods.Where(x =>
            !x.StartsWith("get_", StringComparison.Ordinal) &&
            !x.StartsWith("set_", StringComparison.Ordinal) &&
            !x.StartsWith(".ctor[", StringComparison.Ordinal) &&
            !x.StartsWith(".cctor[", StringComparison.Ordinal)).ToList();
        sb.AppendLine("  Gameplay methods (" + useful.Count + "): " +
            string.Join(", ", useful.Take(maxMethods)));
        if (t.Data.Count > maxData || useful.Count > maxMethods)
            sb.AppendLine("  (output truncated for this class)");
    }

    private static List<Member> GetData(MetadataReader r, TypeDefinition t)
    {
        var data = new List<Member>();
        foreach (PropertyDefinitionHandle handle in t.GetProperties())
        {
            var prop = r.GetPropertyDefinition(handle);
            string type = DecodeProperty(r, prop);
            data.Add(new Member(r.GetString(prop.Name), type));
        }
        // Il2CppInterop may expose game fields via real fields on some builds.
        foreach (FieldDefinitionHandle handle in t.GetFields())
        {
            var field = r.GetFieldDefinition(handle);
            if ((field.Attributes & FieldAttributes.Static) != 0) continue;
            string name = r.GetString(field.Name);
            if (data.Any(x => x.Name == name)) continue;
            data.Add(new Member(name, DecodeField(r, field)));
        }
        return data;
    }

    private static bool HasShape(List<Member> data, string[] shape)
    {
        if (data.Count < shape.Length) return false;
        for (int start = 0; start + shape.Length <= data.Count; start++)
        {
            bool match = true;
            for (int i = 0; i < shape.Length; i++)
            {
                string actual = data[start + i].Type;
                string wanted = shape[i];
                if (wanted == "REF")
                {
                    if (!actual.StartsWith("class ", StringComparison.Ordinal) &&
                        !actual.StartsWith("UnityEngine.", StringComparison.Ordinal) &&
                        !actual.StartsWith("MagicalActual.", StringComparison.Ordinal))
                        match = false;
                }
                else if (actual != wanted && !actual.EndsWith("." + wanted,
                    StringComparison.Ordinal)) match = false;
                if (!match) break;
            }
            if (match) return true;
        }
        return false;
    }

    private static string DecodeProperty(MetadataReader r, PropertyDefinition property)
    {
        try
        {
            BlobReader b = r.GetBlobReader(property.Signature);
            b.ReadByte(); // property calling-convention
            b.ReadCompressedInteger(); // parameter count
            return ReadSignatureType(r, ref b);
        }
        catch { return "unreadable"; }
    }

    private static string DecodeField(MetadataReader r, FieldDefinition field)
    {
        try
        {
            BlobReader b = r.GetBlobReader(field.Signature);
            b.ReadByte(); // FIELD signature prefix
            return ReadSignatureType(r, ref b);
        }
        catch { return "unreadable"; }
    }

    private static string ReadSignatureType(MetadataReader r, ref BlobReader b)
    {
        if (b.RemainingBytes == 0) return "empty";
        int code = b.ReadByte();
        return code switch
        {
            0x01 => "Void",
            0x02 => "Boolean",
            0x03 => "Char",
            0x04 => "SByte",
            0x05 => "Byte",
            0x06 => "Int16",
            0x07 => "UInt16",
            0x08 => "Int32",
            0x09 => "UInt32",
            0x0a => "Int64",
            0x0b => "UInt64",
            0x0c => "Single",
            0x0d => "Double",
            0x0e => "String",
            0x11 => TypeFromCodedIndex(r, b.ReadCompressedInteger()),
            0x12 => "class " + TypeFromCodedIndex(r, b.ReadCompressedInteger()),
            0x15 => ReadGenericInstance(r, ref b),
            0x1d => ReadSignatureType(r, ref b) + "[]",
            0x10 => "byref " + ReadSignatureType(r, ref b),
            0x0f => "ptr " + ReadSignatureType(r, ref b),
            0x1c => "Object",
            0x18 => "IntPtr",
            0x19 => "UIntPtr",
            0x13 => "!" + b.ReadCompressedInteger(),
            0x1e => "!!" + b.ReadCompressedInteger(),
            _ => "element-0x" + code.ToString("x2")
        };
    }

    private static string ReadGenericInstance(MetadataReader r, ref BlobReader b)
    {
        // ELEMENT_TYPE_GENERICINST(0x15), followed by CLASS or VALUETYPE,
        // then a TypeDefOrRef coded index and its argument type list.
        // This exposes Il2CppReferenceArray<T> and class collections which
        // are important for mapping active classes after obfuscation.
        if (b.RemainingBytes == 0) return "generic-empty";
        byte kind = b.ReadByte();
        if (kind is not (0x11 or 0x12)) return "generic-unknown-kind";
        string definition = TypeFromCodedIndex(r, b.ReadCompressedInteger());
        int count = b.ReadCompressedInteger();
        if (count < 0 || count > 24) return "generic-invalid-arity";
        var args = new List<string>(count);
        for (int i = 0; i < count; i++)
            args.Add(ReadSignatureType(r, ref b));
        return (kind == 0x12 ? "class " : "") +
               definition + "<" + string.Join(",", args) + ">";
    }

    private static string TypeFromCodedIndex(MetadataReader r, int coded)
    {
        if (coded < 4) return "invalid";
        int row = coded >> 2;
        return (coded & 3) switch
        {
            0 => TypeName(r, MetadataTokens.TypeDefinitionHandle(row)),
            1 => TypeName(r, MetadataTokens.TypeReferenceHandle(row)),
            2 => "TypeSpec#" + row,
            _ => "invalid"
        };
    }

    private static string FullName(MetadataReader r, TypeDefinition d)
    {
        string ns = r.GetString(d.Namespace);
        string name = r.GetString(d.Name);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string TypeName(MetadataReader r, EntityHandle handle)
    {
        if (handle.IsNil) return "(none)";
        try
        {
            return handle.Kind switch
            {
                HandleKind.TypeDefinition => FullName(r,
                    r.GetTypeDefinition((TypeDefinitionHandle)handle)),
                HandleKind.TypeReference => RefName(r,
                    r.GetTypeReference((TypeReferenceHandle)handle)),
                HandleKind.TypeSpecification => "TypeSpecification",
                _ => handle.Kind.ToString()
            };
        }
        catch { return "(unreadable)"; }
    }

    private static string RefName(MetadataReader r, TypeReference tr)
    {
        string ns = r.GetString(tr.Namespace);
        string name = r.GetString(tr.Name);
        return ns.Length == 0 ? name : ns + "." + name;
    }
}
