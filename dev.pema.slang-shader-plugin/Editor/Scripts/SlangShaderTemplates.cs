using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;

namespace UnitySlangShader
{
    public static class SlangShaderTemplates
    {
        [MenuItem("Assets/Create/Shader/Slang/Unlit Slang Shader")]
        private static void CreateUnlitSlangShader()
        {
            CreateShader("NewUnlitSlangShader.txt", "NewUnlitSlangShader");
        }

        private static void CreateShader(string templatePath, string name)
        {
            MethodInfo getActiveFolderPath = typeof(ProjectWindowUtil).GetMethod("GetActiveFolderPath", BindingFlags.Static | BindingFlags.NonPublic);
            if (getActiveFolderPath == null)
            {
                Debug.LogWarning("Failed to get current folder path");
                return;
            }

            object obj = getActiveFolderPath.Invoke(null, new object[0]);
            string pathToCurrentFolder = obj.ToString();
            string uniquePath = AssetDatabase.GenerateUniqueAssetPath($"{pathToCurrentFolder}/{name}.slangshader");

            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
                0,
                ScriptableObject.CreateInstance<RenameShaderAfterEdit>(),
                uniquePath,
                EditorGUIUtility.IconContent("Shader Icon").image as Texture2D,
                templatePath);
        }

        private class RenameShaderAfterEdit : EndNameEditAction
        {
            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                var template = AssetDatabase.LoadAssetAtPath<TextAsset>($"Packages/dev.pema.slang-shader-plugin/Editor/SlangShaderTemplates/{resourceFile}");
                string code = template.text;

                string name = Path.GetFileNameWithoutExtension(pathName);
                code = code.Replace("__SHADERNAME__", name);

                File.WriteAllText(pathName, code);
                AssetDatabase.Refresh();
                UnityEngine.Object o = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(pathName);
                Selection.activeObject = o;
            }
        }
    }
}