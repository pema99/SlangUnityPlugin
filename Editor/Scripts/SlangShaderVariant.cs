using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnitySlangShader
{
    [Serializable]
    public struct SlangShaderVariant : IEquatable<SlangShaderVariant>
    {
        [SerializeField]
        private string[] SerializedKeywords;

        private HashSet<string> cachedKeywords;
        public HashSet<string> Keywords => cachedKeywords ??= (SerializedKeywords ??= Array.Empty<string>()).ToHashSet();

        // Platform keywords are keywords that are a consequence of the platform,
        // but which we don't want to variant selection based on. Thus we track them separately.
        [SerializeField]
        private string[] SerializedPlatformKeywords;
        public string[] PlatformKeywords => SerializedPlatformKeywords;

        [SerializeField]
        public BuildTarget Platform;

        [SerializeField]
        public GraphicsDeviceType GraphicsBackend;

        public SlangShaderVariant(BuildTarget platform, GraphicsDeviceType graphicsBackend, HashSet<string> keywords)
        {
            Platform = platform;
            GraphicsBackend = graphicsBackend;

            // Platform selection defines
            var allKeywords = new HashSet<string>(keywords);
            (ShaderCompilerPlatform compilerPlatform, string platformKw) = GetShaderCompilerPlatformAndKeyword(graphicsBackend);
            allKeywords.Add(platformKw);

            if (platform == BuildTarget.iOS ||
                platform == BuildTarget.Android ||
                platform == BuildTarget.tvOS)
            {
                allKeywords.Add("SHADER_API_MOBILE");
            }

            if (graphicsBackend == GraphicsDeviceType.OpenGLCore ||
                graphicsBackend == GraphicsDeviceType.OpenGLES2 ||
                graphicsBackend == GraphicsDeviceType.OpenGLES3)
            {
                allKeywords.Add("SHADER_TARGET_GLSL ");
            }

            // Platform keywords
            var builtinDefines = ShaderUtil.GetShaderPlatformKeywordsForBuildTarget(compilerPlatform, platform);
            SerializedPlatformKeywords = builtinDefines.Select(x => Enum.GetName(typeof(BuiltinShaderDefine), x)).ToArray();

            SerializedKeywords = allKeywords.ToArray();
            cachedKeywords = allKeywords;
        }

        private static (ShaderCompilerPlatform, string) GetShaderCompilerPlatformAndKeyword(GraphicsDeviceType type)
        {
            switch (type)
            {
                case GraphicsDeviceType.Direct3D11: return (ShaderCompilerPlatform.D3D, "SHADER_API_D3D11");
                case GraphicsDeviceType.OpenGLES2: return (ShaderCompilerPlatform.GLES20, "SHADER_API_GLES");
                case GraphicsDeviceType.OpenGLES3: return (ShaderCompilerPlatform.GLES3x, "SHADER_API_GLES3");
                case GraphicsDeviceType.PlayStation4: return (ShaderCompilerPlatform.PS4, "SHADER_API_PSSL");
                case GraphicsDeviceType.XboxOne: return (ShaderCompilerPlatform.XboxOneD3D11, "SHADER_API_XBOXONE");
                case GraphicsDeviceType.Metal: return (ShaderCompilerPlatform.Metal, "SHADER_API_METAL");
                case GraphicsDeviceType.OpenGLCore: return (ShaderCompilerPlatform.OpenGLCore, "SHADER_API_GLCORE");
                case GraphicsDeviceType.Direct3D12: return (ShaderCompilerPlatform.D3D, "SHADER_API_D3D12");
                case GraphicsDeviceType.Vulkan: return (ShaderCompilerPlatform.Vulkan, "SHADER_API_VULKAN");
                case GraphicsDeviceType.Switch: return (ShaderCompilerPlatform.Switch, "SHADER_API_SWITCH");
                case GraphicsDeviceType.XboxOneD3D12: return (ShaderCompilerPlatform.XboxOneD3D12, "SHADER_API_XBOXONE");
                case GraphicsDeviceType.GameCoreXboxOne: return (ShaderCompilerPlatform.GameCoreXboxOne, "SHADER_API_XBOXONE");
                case GraphicsDeviceType.GameCoreXboxSeries: return (ShaderCompilerPlatform.GameCoreXboxSeries, "SHADER_API_XBOXONE");
                case GraphicsDeviceType.PlayStation5: return (ShaderCompilerPlatform.PS5, "SHADER_API_PS5");
                case GraphicsDeviceType.PlayStation5NGGC: return (ShaderCompilerPlatform.PS5NGGC, "SHADER_API_PS5");
                default: return (ShaderCompilerPlatform.D3D, "SHADER_API_D3D11");
            }
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(GraphicsBackend, HashSet<string>.CreateSetComparer().GetHashCode(Keywords));
        }

        public bool Equals(SlangShaderVariant other)
        {
            return GraphicsBackend == other.GraphicsBackend &&
                Keywords.SetEquals(other.SerializedKeywords);
        }

        public override string ToString() => $"SlangShaderVariant[{Platform}, {GraphicsBackend}]({string.Join(", ", Keywords)})";
        public override bool Equals(object obj) => obj is SlangShaderVariant v && Equals(v);
    }
}