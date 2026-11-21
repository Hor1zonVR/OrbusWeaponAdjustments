using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
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
        "PlayerMenu"
    };

    private sealed record Member(string Name, string Type);
    private sealed record ExaminedType(string Name, string BaseType,
        List<Member> Data, List<string> Methods);

    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: dotnet run --project tools/InteropAudit -- \"FULL_GAME_DIRECTORY\"");
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
            report.AppendLine("No game state or preference values inspected.");
            report.AppendLine();

            var known = new Dictionary<string, ExaminedType>(StringComparer.Ordinal);
            var weaponCandidates = new List<ExaminedType>();
            var rigCandidates = new List<ExaminedType>();

            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                TypeDefinition td = reader.GetTypeDefinition(handle);
                string full = FullName(reader, td);
                // Property accessors in generated Il2CppInterop code usually represent
                // native fields, so inspect properties as well as ordinary fields.
                bool named = KnownTypes.Contains(full, StringComparer.Ordinal);
                bool plausible = named || td.GetProperties().Count() >= 4 ||
                                 td.GetFields().Count() >= 4;
                if (!plausible) continue;

                List<Member> data = GetData(reader, td);
                bool weaponShape = HasShape(data,
                    new[] { "REF", "REF", "Single", "Boolean" });
                bool rigShape = HasShape(data,
                    new[] { "Vector3", "Vector3", "Quaternion",
                        "Vector3", "Vector3", "Vector3", "Vector3",
                        "Vector3", "Vector3", "Boolean" });
                if (!named && !weaponShape && !rigShape) continue;

                var entry = new ExaminedType(full, TypeName(reader, td.BaseType),
                    data, td.GetMethods().Select(m =>
                        reader.GetString(reader.GetMethodDefinition(m).Name)).ToList());
                if (named) known[full] = entry;
                if (weaponShape) weaponCandidates.Add(entry);
                if (rigShape) rigCandidates.Add(entry);
            }

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
            report.AppendLine("=== POSSIBLE WEAPON ADJUSTER SHAPES (NOT VERIFIED) ===");
            report.AppendLine("Looking for two adjacent reference-like members, Single, Boolean.");
            report.AppendLine("Candidates: " + weaponCandidates.Count);
            foreach (var t in weaponCandidates.Take(40))
                WriteType(report, t, maxMethods: 12, maxData: 12);

            report.AppendLine();
            report.AppendLine("=== POSSIBLE RIG-ADJUSTMENT SHAPES (NOT VERIFIED) ===");
            report.AppendLine("Looking for the ten consecutive expected native member types.");
            report.AppendLine("Candidates: " + rigCandidates.Count);
            foreach (var t in rigCandidates.Take(25))
                WriteType(report, t, maxMethods: 18, maxData: 16);

            report.AppendLine();
            report.AppendLine("Candidates are ONLY hypotheses; do not activate hooks or preset writes");
            report.AppendLine("until mappings are independently verified against the current client.");

            string reportPath = Path.Combine(Path.GetTempPath(),
                "OrbusWeaponInteropAudit_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".txt");
            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
            Console.WriteLine("Read-only audit completed.");
            Console.WriteLine("Saved report: " + reportPath);
            Console.WriteLine("Potential weapon-shaped types: " + weaponCandidates.Count);
            Console.WriteLine("Potential rig-shaped types: " + rigCandidates.Count);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Audit failed (" + ex.GetType().Name + "): " + ex.Message);
            return 1;
        }
    }

    private static void WriteType(StringBuilder sb, ExaminedType t,
        int maxMethods, int maxData)
    {
        sb.AppendLine(t.Name + " : " + t.BaseType);
        sb.AppendLine("  Members (" + t.Data.Count + "): " +
            string.Join("; ", t.Data.Take(maxData).Select(x => x.Name + ":" + x.Type)));
        sb.AppendLine("  Methods (" + t.Methods.Count + "): " +
            string.Join(", ", t.Methods.Take(maxMethods)));
        if (t.Data.Count > maxData || t.Methods.Count > maxMethods)
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
            0x1d => ReadSignatureType(r, ref b) + "[]",
            0x10 => "byref " + ReadSignatureType(r, ref b),
            0x0f => "ptr " + ReadSignatureType(r, ref b),
            _ => "element-0x" + code.ToString("x2")
        };
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
