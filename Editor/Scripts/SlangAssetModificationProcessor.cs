using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnitySlangShader
{
    // When an asset is renamed or deleted, invalid shader entries will be present in the global shader variant collection.
    // If a new asset is created with the same name as one of those invalid shaders, it will inherit that shaders old variants.
    // This is clearly incorrect, so we clear the global variant collection when needed.
    public class SlangAssetModificationProcessor : AssetModificationProcessor
    {
        static AssetDeleteResult OnWillDeleteAsset(string path, RemoveAssetOptions options)
        {
            if (path.EndsWith(".slangshader") || path.EndsWith(".slang"))
            {
                SlangShaderVariantTracker.SlangShaderPaths.Remove(path);
                SlangShaderVariantTracker.ResetTrackedVariants();
            }

            return AssetDeleteResult.DidNotDelete; 
        }

        static AssetMoveResult OnWillMoveAsset(string path, string newPath)
        {
            if (path.EndsWith(".slangshader") || path.EndsWith(".slang"))
            {
                SlangShaderVariantTracker.SlangShaderPaths.Remove(path);
                SlangShaderVariantTracker.SlangShaderPaths.Add(newPath);
                SlangShaderVariantTracker.ResetTrackedVariants();
            }

            return AssetMoveResult.DidNotMove;
        }
    }
}