public static class ReimportScripts
{
    public static string Run()
    {
        UnityEditor.AssetDatabase.ImportAsset("Assets/WarehouseASRS/Scripts", UnityEditor.ImportAssetOptions.ForceUpdate | UnityEditor.ImportAssetOptions.ImportRecursive);
        UnityEditor.AssetDatabase.Refresh();
        UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
        return "reimported";
    }
}

