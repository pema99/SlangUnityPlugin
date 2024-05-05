using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnitySlangShader
{
    public static class SlangShaderSnippets
    {
        private static string slangSupportPreamble;
        public static string SlangSupportPreamble => slangSupportPreamble ??= AssetDatabase.LoadAssetAtPath<TextAsset>($"Packages/dev.pema.slang-shader-plugin/Editor/SlangShaderTemplates/SlangSupportPreamble.txt").text;
    }
}