using UnityEditor;

namespace YARG.Editor.Submodules
{
    [InitializeOnLoad]
    public class ProjectAdder : AssetPostprocessor
    {
        // Undocumented post-process hook called by IDE packages.
        // Return type can be either void (no modifications) or string (modifications made).
        private static string OnGeneratedSlnSolution(string path, string contents)
        {
            EditorUtility.ClearProgressBar();
            return contents;
        }
    }
}
