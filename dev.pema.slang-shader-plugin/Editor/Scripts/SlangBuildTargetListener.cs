using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace UnitySlangShader
{
    public class SlangBuildTargetListener : IActiveBuildTargetChanged
    {
        public int callbackOrder { get { return 0; } }

        public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget)
        {
            SlangShaderVariantTracker.ResetTrackedVariants();
        }
    }
}