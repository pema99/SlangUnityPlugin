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
using UnityEditor.Rendering;
using UnityEditor.Build;
using HarmonyLib;

namespace UnitySlangShader
{
    [InitializeOnLoad]
    public class SlangShaderVariantTracker
    {
        // TODO: Player build
        #region Harmony patches
        private static Harmony harmonyInstance = new Harmony("pema.dev.slang-shader-plugin");

        [HarmonyPatch(typeof(BuildPipeline), nameof(BuildPipeline.BuildAssetBundles),
            new Type[] { typeof(string), typeof(BuildAssetBundleOptions), typeof(BuildTargetGroup), typeof(BuildTarget), typeof(int) })]
        private class HarmonyAssetBundleBuildHook0
        {
            static bool Prefix()
            {
                CompileSlangShaderVariantsFromScenes(new string[] { SceneManager.GetActiveScene().path });
                return true;
            }
        }

        [HarmonyPatch(typeof(BuildPipeline), nameof(BuildPipeline.BuildAssetBundles),
            new Type[] { typeof(string), typeof(AssetBundleBuild[]), typeof(BuildAssetBundleOptions), typeof(BuildTargetGroup), typeof(BuildTarget), typeof(int) })]
        private class HarmonyAssetBundleBuildHook1
        {
            static bool Prefix(AssetBundleBuild[] builds)
            {
                List<string> scenePaths = new List<string>();
                scenePaths.Add(SceneManager.GetActiveScene().path);
                scenePaths.AddRange(builds.SelectMany(x => x.assetNames.Where(y => y.EndsWith(".unity"))));
                CompileSlangShaderVariantsFromScenes(scenePaths.Distinct());
                return true;
            }
        }
        #endregion

        private static readonly Func<int> getCurrentShaderVariantCollectionVariantCountWrapper;
        private static readonly Action<string> saveCurrentShaderVariantCollectionWrapper;
        private static readonly Action clearCurrentShaderVariantCollectionWrapper;
        private static readonly Action<Shader, bool> openShaderCombinationsWrapper;

        private static int totalVariantCount;
        public static Dictionary<string, HashSet<SlangShaderVariant>> CurrentlyLoadedSlangShaderVariants = new Dictionary<string, HashSet<SlangShaderVariant>>();

        public static HashSet<string> SlangShaderPaths = new HashSet<string>();

        static SlangShaderVariantTracker()
        {
            harmonyInstance.PatchAll();

            totalVariantCount = 0;

            getCurrentShaderVariantCollectionVariantCountWrapper = (Func<int>)typeof(ShaderUtil)
                .GetMethod("GetCurrentShaderVariantCollectionVariantCount", BindingFlags.NonPublic | BindingFlags.Static)
                .CreateDelegate(typeof(Func<int>));

            saveCurrentShaderVariantCollectionWrapper = (Action<string>)typeof(ShaderUtil)
                .GetMethod("SaveCurrentShaderVariantCollection", BindingFlags.NonPublic | BindingFlags.Static)
                .CreateDelegate(typeof(Action<string>));

            clearCurrentShaderVariantCollectionWrapper = (Action)typeof(ShaderUtil)
                .GetMethod("ClearCurrentShaderVariantCollection", BindingFlags.NonPublic | BindingFlags.Static)
                .CreateDelegate(typeof(Action));

            openShaderCombinationsWrapper = (Action<Shader, bool>)typeof(ShaderUtil)
                .GetMethod("OpenShaderCombinations", BindingFlags.Static | BindingFlags.NonPublic)
                .CreateDelegate(typeof(Action<Shader, bool>));

            SlangShaderPaths = AssetDatabase.FindAssets("t:Shader")
                .Select(x => AssetDatabase.GUIDToAssetPath(x))
                .Where(x => AssetDatabase.GetImporterType(x) == typeof(SlangShaderImporter))
                .ToHashSet();

            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        #region ShaderVariantCollection based gathering
        public static void ResetTrackedVariants()
        {
            totalVariantCount = 0;
            clearCurrentShaderVariantCollectionWrapper();
        }

        private static Dictionary<string, HashSet<SlangShaderVariant>> GetAllSlangShaderVariants()
        {
            string basePath = "Assets/SlangShaderCache";
            string svcPath = $"{basePath}/ShaderVariants.shadervariants";
            if (!Directory.Exists(basePath))
                Directory.CreateDirectory(basePath);

            saveCurrentShaderVariantCollectionWrapper(svcPath);
            var svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(svcPath);

            var slangShaderVariantMap = new Dictionary<string, HashSet<SlangShaderVariant>>();
            if (SlangShaderPaths.Count == 0)
                return slangShaderVariantMap;

            var serializedSvc = new SerializedObject(svc);
            var shaderArray = serializedSvc.FindProperty("m_Shaders");

            for (int shaderIdx = 0; shaderIdx < shaderArray.arraySize; shaderIdx++)
            {
                var elem = shaderArray.GetArrayElementAtIndex(shaderIdx);
                var shader = elem.FindPropertyRelative("first").objectReferenceValue as Shader;
                string shaderPath = AssetDatabase.GetAssetPath(shader);

                if (SlangShaderPaths.Contains(shaderPath))
                {
                    var variants = elem.FindPropertyRelative("second.variants");
                    var variantsArray = new SlangShaderVariant[variants.arraySize];
                    for (int variantIdx = 0; variantIdx < variants.arraySize; variantIdx++)
                    {
                        var keywords = variants.GetArrayElementAtIndex(variantIdx).FindPropertyRelative("keywords").stringValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        variantsArray[variantIdx] = new SlangShaderVariant(keywords.ToHashSet());
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

                CurrentlyLoadedSlangShaderVariants = GetAllSlangShaderVariants();

                foreach ((string path, HashSet<SlangShaderVariant> variants) in CurrentlyLoadedSlangShaderVariants)
                {
                    var importer = AssetImporter.GetAtPath(path) as SlangShaderImporter;
                    var currentSet = new HashSet<SlangShaderVariant>(importer.GeneratedVariants); // TODO
                    if (!currentSet.IsSupersetOf(variants))
                    {
                        importer.GeneratedVariants = variants.ToArray();
                        importer.AddVariantsRequested = true;
                        EditorUtility.SetDirty(importer);
                        importer.SaveAndReimport();
                    }
                }
            }
        }
        #endregion

        #region OpenShaderCombinations based gathering
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
                        result.Add(new SlangShaderVariant(keywords.ToHashSet()));
                    }
                }
            }
            finally
            {
                CodeEditor.Editor.SetCodeEditor(prevEditorPath);
                Debug.unityLogger.filterLogType = LogType.Log;
            }

            // Add stereo variants
            var resultWithStereoVariants = new HashSet<SlangShaderVariant>();
            foreach (var variant in result)
            {
                resultWithStereoVariants.Add(variant);
                resultWithStereoVariants.Add(new SlangShaderVariant(variant.Keywords.Append("STEREO_INSTANCING_ON").ToHashSet()));
                resultWithStereoVariants.Add(new SlangShaderVariant(variant.Keywords.Append("UNITY_SINGLE_PASS_STEREO").ToHashSet()));
            }

            return resultWithStereoVariants;
        }

        private static void GatherSlangVariantsFromScene(HashSet<string> slangShaderPaths, HashSet<SlangShaderImporter> gatheredShaders)
        {
            foreach (string path in slangShaderPaths)
            {
                var importer = AssetImporter.GetAtPath(path) as SlangShaderImporter;
                var currentSet = new HashSet<SlangShaderVariant>(importer.GeneratedVariants); // TODO

                var variants = GetSlangShaderVariantsForShaderFromCurrentScene(AssetDatabase.LoadAssetAtPath<Shader>(path));

                if (!currentSet.IsSupersetOf(variants))
                {
                    currentSet.UnionWith(variants);
                    importer.GeneratedVariants = currentSet.ToArray();
                    importer.AddVariantsRequested = true;
                    EditorUtility.SetDirty(importer);
                    gatheredShaders.Add(importer);
                }
            }
        }

        private static void CompileSlangShaderVariantsFromScenes(IEnumerable<string> scenes)
        {
            // Find all shaders
            var slangShaderPaths = AssetDatabase.FindAssets("t:Shader")
                .Select(x => AssetDatabase.GUIDToAssetPath(x))
                .Where(x => AssetDatabase.GetImporterType(x) == typeof(SlangShaderImporter))
                .ToHashSet();

            // Gather from each scene
            HashSet<SlangShaderImporter> shadersToReimport = new HashSet<SlangShaderImporter>();
            foreach (string path in scenes)
            {
                // No need to re-open if the scene is already open
                if (path != SceneManager.GetActiveScene().path)
                {
                    EditorSceneManager.OpenScene(path);
                }

                GatherSlangVariantsFromScene(slangShaderPaths, shadersToReimport);
            }

            // Reimport all
            foreach (var shader in shadersToReimport)
            {
                shader.SaveAndReimport();
            }
        }
        #endregion
    }
}
