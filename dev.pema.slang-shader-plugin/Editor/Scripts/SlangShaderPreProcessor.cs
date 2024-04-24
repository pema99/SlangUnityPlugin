using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnitySlangShader;

public class SlangShaderPreProcessor : IPreprocessShaders
{
    public int callbackOrder => int.MaxValue - 1;

    public HashSet<string> handledShaders = new HashSet<string>();

    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
    {
        string path = AssetDatabase.GetAssetPath(shader);
        if (handledShaders.Contains(path))
            return;

        var importer = AssetImporter.GetAtPath(path) as SlangShaderImporter;
        if (importer == null)
            return;

        // TODO: Platform keywords
        var desiredVariants = data.Select(x => new SlangShaderVariant(x.shaderKeywordSet.GetShaderKeywords().Select(x => x.name).ToArray())).ToHashSet();

        var currentSet = new HashSet<SlangShaderVariant>(importer.GeneratedVariants);

        if (!currentSet.IsSupersetOf(desiredVariants))
        {
            Debug.Log("Handling " + path);
            Debug.Log("FOO " + System.Threading.Thread.CurrentThread.Name);

            currentSet.UnionWith(desiredVariants);
            string generatedSourceCode = SlangShaderImporter.GenerateSourceCodeWithoutImport(path, currentSet.ToArray());

            // Have mercy on me, O God, according to your steadfast love; according to your abundant mercy blot out my transgressions.
            // Wash me thoroughly from my iniquity and cleanse me from my sin! - Psalm 51:1-2
            ShaderUtil.UpdateShaderAsset(shader, generatedSourceCode);
        }
    }
}
