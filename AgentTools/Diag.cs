using System.Linq;
using UnityEditor;

public static class Diag
{
    public static string Run()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var p in new[] { "Graph", "Tf", "Layout", "Traffic" })
        {
            string path = $"Assets/WarehouseASRS/Scripts/Core/{p}.cs";
            var t = AssetDatabase.GetMainAssetTypeAtPath(path);
            var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            sb.AppendLine($"{path}: type={t} mono={(ms != null)} len={(ms != null ? ms.text.Length : -1)} guid={AssetDatabase.AssetPathToGUID(path)}");
        }
        var asm = UnityEditor.Compilation.CompilationPipeline.GetAssemblies().FirstOrDefault(a => a.name == "Assembly-CSharp");
        if (asm != null) sb.AppendLine("sources: " + string.Join(", ", asm.sourceFiles.Where(s => s.Contains("WarehouseASRS")).Select(System.IO.Path.GetFileName)));
        return sb.ToString();
    }
}

