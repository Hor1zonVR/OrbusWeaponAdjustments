using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

if (args.Length != 1 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/SourceSyntaxCheck -- src/OrbusWeaponAdjustments");
    return 2;
}
int checkedFiles = 0, errors = 0;
foreach (string file in Directory.EnumerateFiles(args[0], "*.cs",
             SearchOption.AllDirectories))
{
    if (file.Contains(Path.DirectorySeparatorChar + "obj" +
                      Path.DirectorySeparatorChar) ||
        file.Contains(Path.DirectorySeparatorChar + "bin" +
                      Path.DirectorySeparatorChar)) continue;
    string source = File.ReadAllText(file);
    var tree = CSharpSyntaxTree.ParseText(source, path: file);
    var failures = tree.GetDiagnostics().Where(d =>
        d.Severity == DiagnosticSeverity.Error).ToArray();
    checkedFiles++;
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure.ToString());
        errors++;
    }
}
Console.WriteLine("Checked " + checkedFiles + " C# files; syntax errors: " + errors);
return errors == 0 && checkedFiles > 0 ? 0 : 1;
