using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace UnitySlangShader
{
    [CustomEditor(typeof(SlangShaderImporter))]
    public class SlangShaderImporterEditor : ScriptedImporterEditor
    {
        public override void OnInspectorGUI()
        {
            var importer = target as SlangShaderImporter;
            if (importer == null) return;

            EditorGUILayout.LabelField("Source code:");
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextArea(File.ReadAllText(importer.assetPath));
            }

            EditorGUILayout.LabelField("Generated shader:");
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextArea(serializedObject.FindProperty("GeneratedSourceCode").stringValue);
            }

            base.ApplyRevertGUI();
        }
    }
}