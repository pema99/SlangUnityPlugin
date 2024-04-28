using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace UnitySlangShader
{
    [ScriptedImporter(1, "slang")]
    public class SlangIncludeImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var dummyInclude = new TextAsset(File.ReadAllText(ctx.assetPath));
            ctx.AddObjectToAsset("Slang Shader Include", dummyInclude, EditorGUIUtility.IconContent("TextScriptImporter Icon").image as Texture2D);
            ctx.SetMainObject(dummyInclude);
        }
    }
}