using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Reflection;
using System.Linq;
using System.IO;
using Unity.CodeEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UnitySlangShader
{
    [InitializeOnLoad]
    public class SlangShaderVariantTracker
    {
        private static int totalVariantCount;

        private static readonly Func<int> getCurrentShaderVariantCollectionVariantCountWrapper;
        private static readonly Action<string> saveCurrentShaderVariantCollectionWrapper;
        private static readonly Action<Shader, bool> openShaderCombinationsWrapper;

        static SlangShaderVariantTracker()
        {
            totalVariantCount = 0;

            getCurrentShaderVariantCollectionVariantCountWrapper = (Func<int>)typeof(ShaderUtil)
                .GetMethod("GetCurrentShaderVariantCollectionVariantCount", BindingFlags.NonPublic | BindingFlags.Static)
                .CreateDelegate(typeof(Func<int>));

            saveCurrentShaderVariantCollectionWrapper = (Action<string>)typeof(ShaderUtil)
                .GetMethod("SaveCurrentShaderVariantCollection", BindingFlags.NonPublic | BindingFlags.Static)
                .CreateDelegate(typeof(Action<string>));

            openShaderCombinationsWrapper = (Action<Shader, bool>)typeof(ShaderUtil)
                .GetMethod("OpenShaderCombinations", BindingFlags.Static | BindingFlags.NonPublic)
                .CreateDelegate(typeof(Action<Shader, bool>));

            EditorApplication.update -= Update;
            EditorApplication.update += Update;

            //EditorSceneManager.sceneOpened -= OpenScene;
            //EditorSceneManager.sceneOpened += OpenScene;
        }

        private static Dictionary<string, HashSet<SlangShaderVariant>> GetAllSlangShaderVariants()
        {
            string basePath = "Assets/SlangShaderCache";
            string svcPath = $"{basePath}/ShaderVariants.shadervariants";
            if (!Directory.Exists(basePath))
                Directory.CreateDirectory(basePath);

            saveCurrentShaderVariantCollectionWrapper(svcPath);
            var svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(svcPath);

            // TODO: Make this faster. Perhaps the importer itself can write into a static map on import?
            // Maybe try vanilla System.IO calls
            var slangShaderPaths = AssetDatabase.FindAssets("t:Shader")
                .Select(x => AssetDatabase.GUIDToAssetPath(x))
                .Where(x => AssetDatabase.GetImporterType(x) == typeof(SlangShaderImporter))
                .ToHashSet();

            var slangShaderVariantMap = new Dictionary<string, HashSet<SlangShaderVariant>>();
            if (slangShaderPaths.Count == 0)
                return slangShaderVariantMap;

            var serializedSvc = new SerializedObject(svc);
            var shaderArray = serializedSvc.FindProperty("m_Shaders");

            for (int shaderIdx = 0; shaderIdx < shaderArray.arraySize; shaderIdx++)
            {
                var elem = shaderArray.GetArrayElementAtIndex(shaderIdx);
                var shader = elem.FindPropertyRelative("first").objectReferenceValue as Shader;
                string shaderPath = AssetDatabase.GetAssetPath(shader);

                if (slangShaderPaths.Contains(shaderPath))
                {
                    var variants = elem.FindPropertyRelative("second.variants");
                    var variantsArray = new SlangShaderVariant[variants.arraySize];
                    for (int variantIdx = 0; variantIdx < variants.arraySize; variantIdx++)
                    {
                        var keywords = variants.GetArrayElementAtIndex(variantIdx).FindPropertyRelative("keywords").stringValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        variantsArray[variantIdx] = new SlangShaderVariant(keywords);
                    }
                    slangShaderVariantMap[shaderPath] = variantsArray.ToHashSet();
                }
            }

            return slangShaderVariantMap;
        }

        private static void Update()
        {
            int newVariantCount = getCurrentShaderVariantCollectionVariantCountWrapper();
            if (newVariantCount != totalVariantCount)
            {
                totalVariantCount = newVariantCount;

                Dictionary<string, HashSet<SlangShaderVariant>> variantMap = GetAllSlangShaderVariants();

                foreach ((string path, HashSet<SlangShaderVariant> variants) in variantMap)
                {
                    var importer = AssetImporter.GetAtPath(path) as SlangShaderImporter;
                    var currentSet = new HashSet<SlangShaderVariant>(importer.GeneratedVariants); // TODO
                    if (!currentSet.IsSupersetOf(variants))
                    {
                        currentSet.UnionWith(variants);
                        importer.GeneratedVariants = currentSet.ToArray();
                        EditorUtility.SetDirty(importer);
                        importer.SaveAndReimport();
                    }
                }
            }
        }

        private class DummyCodeEditor : IExternalCodeEditor
        {
            public void Initialize(string editorInstallationPath) { }
            public void OnGUI() { }
            public bool OpenProject(string filePath = "", int line = -1, int column = -1) { return true; }
            public void SyncAll() { }
            public void SyncIfNeeded(string[] addedFiles, string[] deletedFiles, string[] movedFiles, string[] movedFromFiles, string[] importedFiles) { }

            public static string DummyPath = "DO_NOT_USE_THIS_EDITOR";

            public CodeEditor.Installation[] Installations => new CodeEditor.Installation[]
            {
                new CodeEditor.Installation { Name = DummyPath, Path = DummyPath }
            };

            public bool TryGetInstallationForPath(string editorPath, out CodeEditor.Installation installation)
            {
                installation = Installations[0];
                return true;
            }
        }

        public static HashSet<SlangShaderVariant> GetSlangShaderVariantsForShaderFromCurrentScene(Shader shader)
        {
            HashSet<SlangShaderVariant> result = new HashSet<SlangShaderVariant>();

            string prevEditorPath = CodeEditor.CurrentEditorPath;
            var prevLogFilter = Debug.unityLogger.filterLogType;

            try
            {
                // Set to dummy editor
                CodeEditor.Editor.SetCodeEditor(DummyCodeEditor.DummyPath);

                // Supress warnings from the editor not actually existing
                Debug.unityLogger.filterLogType = LogType.Assert;

                // Generate list of variants
                openShaderCombinationsWrapper(shader, true);
                string shaderCombinationsPath = $"Temp/ParsedCombinations-{shader.name.Replace("/", "-").Replace("\\", "-")}.shader";
                string[] shaderCombinationsLines = File.ReadAllLines(shaderCombinationsPath);

                // Parse the variants
                bool isParsingVariants = false;
                for (int line = 0; line < shaderCombinationsLines.Length; line++)
                {
                    string lineText = shaderCombinationsLines[line];
                    if (lineText == string.Empty)
                    {
                        isParsingVariants = false;
                    }
                    else if (!isParsingVariants && lineText.Contains("keyword variants used in scene"))
                    {
                        isParsingVariants = true;
                        line++;
                    }
                    else if (isParsingVariants)
                    {
                        string[] keywords = lineText == "<no keywords defined>" ? Array.Empty<string>() : lineText.Split(' ');
                        result.Add(new SlangShaderVariant(keywords));
                    }
                }
            }
            finally
            {
                CodeEditor.Editor.SetCodeEditor(prevEditorPath);
                Debug.unityLogger.filterLogType = LogType.Log;
            }

            return result;
        }

        /*private static void OpenScene(Scene scene, OpenSceneMode mode)
        {
            // TODO: Make this faster. Perhaps the importer itself can write into a static map on import?
            // Maybe try vanilla System.IO calls
            var slangShaderPaths = AssetDatabase.FindAssets("t:Shader")
                .Select(x => AssetDatabase.GUIDToAssetPath(x))
                .Where(x => AssetDatabase.GetImporterType(x) == typeof(SlangShaderImporter))
                .ToHashSet();

            foreach (string path in slangShaderPaths)
            {
                var importer = AssetImporter.GetAtPath(path) as SlangShaderImporter;
                var currentSet = new HashSet<SlangShaderVariant>(importer.GeneratedVariants); // TODO

                var variants = GetSlangShaderVariantsForShaderFromCurrentScene(AssetDatabase.LoadAssetAtPath<Shader>(path));

                if (!currentSet.IsSupersetOf(variants))
                {
                    currentSet.UnionWith(variants);
                    importer.GeneratedVariants = currentSet.ToArray();
                    EditorUtility.SetDirty(importer);
                    importer.SaveAndReimport(); 
                }
            }
        }*/
    }
}
