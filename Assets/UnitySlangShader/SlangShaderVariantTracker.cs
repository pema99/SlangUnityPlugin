using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Reflection;
using System.Linq;

namespace UnitySlangShader
{
    [InitializeOnLoad]
    public class SlangShaderVariantTracker
    {
        private static int totalVariantCount;

        private static readonly Func<int> getCurrentShaderVariantCollectionVariantCountWrapper;
        private static readonly Action<string> saveCurrentShaderVariantCollectionWrapper;

        static SlangShaderVariantTracker()
        {
            totalVariantCount = 0;

            getCurrentShaderVariantCollectionVariantCountWrapper = (Func<int>)typeof(ShaderUtil)
                .GetMethod("GetCurrentShaderVariantCollectionVariantCount", BindingFlags.NonPublic | BindingFlags.Static)
                .CreateDelegate(typeof(Func<int>));

            saveCurrentShaderVariantCollectionWrapper = (Action<string>)typeof(ShaderUtil)
                .GetMethod("SaveCurrentShaderVariantCollection", BindingFlags.NonPublic | BindingFlags.Static)
                .CreateDelegate(typeof(Action<string>));

            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        private static Dictionary<string, HashSet<SlangShaderVariant>> GetSlangShaderVariants()
        {
            string svcPath = "Assets/UnitySlangShader/ShaderVariants.shadervariants";
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

                Dictionary<string, HashSet<SlangShaderVariant>> variantMap = GetSlangShaderVariants();

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
    }
}
