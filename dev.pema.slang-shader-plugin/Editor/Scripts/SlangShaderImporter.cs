using UnityEditor;
using UnityEngine;
using UnityEditor.AssetImporters;
using System.IO;
using UnityShaderParser.ShaderLab;
using UnityShaderParser.HLSL;
using UnityShaderParser.Common;
using System.Collections.Generic;
using System.Diagnostics;
using System;
using System.Text.RegularExpressions;
using System.Text;
using UnitySlangShader.SlangAPI;
using SLTokenKind = UnityShaderParser.ShaderLab.TokenKind;
using HLSLTokenKind = UnityShaderParser.HLSL.TokenKind;
using SLToken = UnityShaderParser.Common.Token<UnityShaderParser.ShaderLab.TokenKind>;
using HLSLToken = UnityShaderParser.Common.Token<UnityShaderParser.HLSL.TokenKind>;
using System.Linq;
using UnityEditor.Rendering;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using UnityEngine.Rendering;
using System.Threading;

namespace UnitySlangShader
{
    [Serializable]
    public struct SlangShaderDiagnostic
    {
        public string Text;
        public string File;
        public int Line;
        public bool Warning;
    }

    [ScriptedImporter(1, "slangshader")]
    public class SlangShaderImporter : ScriptedImporter
    {
        // Makes edits to HLSL code resulting from Slang compilation to make it unity-compatible
        private class HLSLSlangEditor : HLSLEditor
        {
            // Renaming samplers
            public HashSet<string> BrokenTextureFields = new HashSet<string>();

            public HLSLSlangEditor(string source, List<HLSLToken> tokens)
                : base(source, tokens) { }

            public override void VisitVariableDeclarationStatementNode(VariableDeclarationStatementNode node)
            {
                if (node.Declarators.Count == 1 && node.Kind is PredefinedObjectTypeNode obj)
                {
                    string declName = node.Declarators[0].Name;
                    switch (obj.Kind)
                    {
                        // Keep track of declared textures, and rename problematic ones
                        case PredefinedObjectType.Texture:
                        case PredefinedObjectType.Texture1D:
                        case PredefinedObjectType.Texture1DArray:
                        case PredefinedObjectType.Texture2D:
                        case PredefinedObjectType.Texture2DArray:
                        case PredefinedObjectType.Texture3D:
                        case PredefinedObjectType.TextureCube:
                        case PredefinedObjectType.TextureCubeArray:
                        case PredefinedObjectType.Texture2DMS:
                        case PredefinedObjectType.Texture2DMSArray:
                            if (declName.EndsWith("_t_0"))
                            {
                                string newName = declName.Replace("_t_0", "");
                                node.Declarators[0].Name = newName;
                                Edit(node.Declarators[0], node.Declarators[0].GetPrettyPrintedCode());

                                BrokenTextureFields.Add(newName);
                            }
                            break;

                        // Replace names of declared samplers with the appropriate unity convention
                        case PredefinedObjectType.SamplerState:
                            if (declName.EndsWith("_s_0"))
                            {
                                string correspondingTextureName = declName.Replace("_s_0", "");
                                if (BrokenTextureFields.Contains(correspondingTextureName))
                                {
                                    node.Declarators[0].Name = $"sampler_{correspondingTextureName}";
                                    Edit(node.Declarators[0], node.Declarators[0].GetPrettyPrintedCode());
                                }
                            }
                            break;

                        default:
                            break;
                    }
                }

                base.VisitVariableDeclarationStatementNode(node);
            }

            public override void VisitIdentifierExpressionNode(IdentifierExpressionNode node)
            {
                // Replace names of used samplers with the appropriate unity convention
                if (node.Name.EndsWith("_s_0"))
                {
                    string correspondingTextureName = node.Name.Replace("_s_0", "");
                    if (BrokenTextureFields.Contains(correspondingTextureName))
                    {
                        Edit(node, $"sampler_{correspondingTextureName}");
                    }
                }
                // Replace names of used textures, removing the suffix
                else if (node.Name.EndsWith("_t_0"))
                {
                    string newName = node.Name.Replace("_t_0", "");
                    if (BrokenTextureFields.Contains(newName))
                    {
                        Edit(node, newName);
                    }
                }

                base.VisitIdentifierExpressionNode(node);
            }
        }

        // Traverses ShaderLab code and replaces Slang blocks with HLSL blocks
        private class ShaderLabSlangEditor : ShaderLabEditor
        {
            public HashSet<string> Diagnostics = new HashSet<string>();
            public HashSet<string> DependencyFiles = new HashSet<string>();

            private string filePath = string.Empty;

            private SlangShaderVariant[] variantsToGenerate;
            private HashSet<string> allKeywords;

            public ShaderLabSlangEditor(string filePath, SlangShaderVariant[] variantsToGenerate, string source, List<SLToken> tokens)
                : base(source, tokens)
            {
                this.filePath = filePath;
                this.variantsToGenerate = variantsToGenerate;

                // Find the entire space of keywords we care about
                allKeywords = variantsToGenerate.SelectMany(x => x.Keywords).ToHashSet();

                // Delete all include blocks
                foreach (var token in tokens)
                {
                    if (token.Kind == SLTokenKind.IncludeBlock)
                    {
                        Edit(token, string.Empty);
                    }
                }
            }

            public override void VisitShaderCodePassNode(ShaderCodePassNode node)
            {
                node.ProgramBlocks.ForEach(HandleProgramBlock);
                base.VisitShaderCodePassNode(node);
            }

            public override void VisitSubShaderNode(SubShaderNode node)
            {
                node.ProgramBlocks.ForEach(HandleProgramBlock);
                base.VisitSubShaderNode(node);
            }

            private List<string[]> ExtractPragmasFromCode(string fullCode)
            {
                List<string[]> pragmas = new List<string[]>();
                var matches = Regex.Matches(fullCode, @"#pragma (.+)$", RegexOptions.Multiline);
                foreach (Match pragma in matches)
                {
                    string trimmed = pragma.Groups[0].Value.Trim();
                    if (trimmed == string.Empty)
                        continue;

                    string[] parts = trimmed.Split(' ');
                    pragmas.Add(parts.Skip(1).ToArray());
                }

                return pragmas;
            }

            private List<(string stage, string entryName)> FindEntryPointPragmas(List<string[]> pragmas)
            {
                var entryPoints = new List<(string stage, string entryName)>();
                foreach (var pragma in pragmas)
                {
                    if (pragma.Length <= 1)
                        continue;

                    switch (pragma[0])
                    {
                        case "fragment":
                        case "vertex":
                        case "geometry":
                        case "hull":
                        case "domain":
                            entryPoints.Add((pragma[0], pragma[1]));
                            break;
                        default:
                            break;
                    }
                }
                return entryPoints;
            }

            // Which pragmas to passthrough into the final shader?
            private string GetPassthroughPragmas(List<string[]> pragmas)
            {
                StringBuilder sb = new StringBuilder();

                foreach (string[] pragma in pragmas)
                {
                    if (pragma.Length == 0)
                        continue;

                    if (pragma[0].StartsWith("multi_compile") ||
                        pragma[0] == "shader_feature" ||
                        pragma[0] == "skip_variants")
                    {
                        sb.AppendLine($"#pragma {string.Join(" ", pragma)}");
                    }
                }

                return sb.ToString();
            }

            public void HandleProgramBlock(HLSLProgramBlock programBlock)
            {
                string preamble = SlangShaderSnippets.SlangSupportPreamble;
                string fullCodeWithLineStart = $"{preamble}\n#line {programBlock.Span.Start.Line - 1}\n{programBlock.FullCode}";

                // Setup
                var pragmas = ExtractPragmasFromCode(fullCodeWithLineStart);
                var entryPoints = FindEntryPointPragmas(pragmas);
                Dictionary<string, string> predefinedDirectives = GetPredefinedDirectives();
                string cgIncludePath = $"{EditorApplication.applicationContentsPath}/CGIncludes";
                string basePath = Directory.GetParent(Application.dataPath).FullName;
                string[] includePaths = new string[] { cgIncludePath, basePath };

                // Compile each variant
                var perThreadResult = new string[variantsToGenerate.Length];
                var perThreadDiagnostic = new List<string>[variantsToGenerate.Length];
                var perThreadEntryPoints = new HashSet<(string stage, string entryName)>[variantsToGenerate.Length];
                var perThreadDependencyFiles = new string[variantsToGenerate.Length][];
                int doneVariants = 0;
                var compileTask = Task.Run(() =>
                {
                    // We estimate that each thread needs to compile about 4 variants for parallelization to be worth it
                    float saturation = (float)variantsToGenerate.Length / (Environment.ProcessorCount * 4);
                    int threadCount = Mathf.Clamp(Mathf.CeilToInt(saturation * Environment.ProcessorCount), 1, Environment.ProcessorCount);

                    ConcurrentQueue<int> remainingWork = new ConcurrentQueue<int>(Enumerable.Range(0, variantsToGenerate.Length));
                    Thread[] threads = new Thread[threadCount];
                    for (int i = 0; i < threadCount; i++)
                    {
                        threads[i] = new Thread(() =>
                        {
                            using SlangSession session = new SlangSession();
                            while (!remainingWork.IsEmpty)
                            {
                                if (!remainingWork.TryDequeue(out int variantIdx))
                                    continue;

                                perThreadResult[variantIdx] = CompileVariant(
                                    session,
                                    fullCodeWithLineStart,
                                    includePaths,
                                    predefinedDirectives,
                                    variantsToGenerate[variantIdx].Keywords,
                                    entryPoints,
                                    out perThreadDiagnostic[variantIdx],
                                    out perThreadEntryPoints[variantIdx],
                                    out perThreadDependencyFiles[variantIdx]);

                                Interlocked.Increment(ref doneVariants);
                            }
                        });
                        threads[i].Start();
                    }
                    for (int i = 0; i < threadCount; i++)
                    {
                        threads[i].Join();
                    }
                });

                // Show progress if there are enough variants to justify it
                if (variantsToGenerate.Length > 16)
                {
                    while (!compileTask.IsCompleted)
                    {
                        EditorUtility.DisplayProgressBar(
                            "Slang Shader compilation",
                            $"Compiling variant ({doneVariants} / {variantsToGenerate.Length})",
                            (float)doneVariants / (float)variantsToGenerate.Length);
                        Thread.Sleep(10);
                    }
                    EditorUtility.ClearProgressBar();
                }
                else
                {
                    compileTask.Wait();
                }

                // Gather the result of each compilation
                string allVariants = string.Join("\n", perThreadResult);
                Diagnostics.UnionWith(perThreadDiagnostic.SelectMany(x => x));
                DependencyFiles.UnionWith(perThreadDependencyFiles.SelectMany(x => x));

                // Find all entry points from each variant, make pragmas from them. Add vert or frag if missing.
                var allEntryPoints = CollectEntryPoints(perThreadEntryPoints);
                string entryPointPragmas = $"{string.Join("\n", allEntryPoints.Select(x => $"#pragma {x.stage} {x.entryName}"))}\n";

                // Some pragmas like multi_compile should just be passed through directly
                string passthroughPragmas = GetPassthroughPragmas(pragmas);

                // In case a request variant is missing or failed to compile, provide a fallback variant 
                string fallbackVariant = GetFallbackVariant(allEntryPoints);

                Edit(programBlock.Span, $"HLSLPROGRAM\n{entryPointPragmas}{passthroughPragmas}{allVariants}{fallbackVariant}\nENDHLSL");
            }

            private string CompileVariant(
                SlangSession slangSession,
                string fullCode,
                string[] includePaths,
                Dictionary<string, string> predefinedDirectives,
                HashSet<string> keywords,
                List<(string stage, string entryName)> knownEntryPoints,
                out List<string> diagnostics,
                out HashSet<(string stage, string entryName)> outEntryPoints,
                out string[] dependencyFiles)
            {
                diagnostics = new List<string>();
                outEntryPoints = new HashSet<(string stage, string entryName)>();
                dependencyFiles = new string[0];

                using CompileRequest request = slangSession.CreateCompileRequest();

                request.SetCodeGenTarget(SlangCompileTarget.SLANG_HLSL);
                request.SetMatrixLayoutMode(SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR);
                request.SetTargetFlags(SlangTargetFlags.SLANG_TARGET_FLAG_GENERATE_WHOLE_PROGRAM);
                request.SetTargetLineDirectiveMode(SlangLineDirectiveMode.SLANG_LINE_DIRECTIVE_MODE_NONE);

                foreach (string includePath in includePaths)
                {
                    request.AddSearchPath(includePath);
                }

                // Define user keywords
                foreach (var keyword in keywords)
                {
                    request.AddPreprocessorDefine(keyword, "1");
                }

                // Define directives
                foreach ((string key, string val) in predefinedDirectives)
                {
                    request.AddPreprocessorDefine(key, val);
                }

                // Some stuff to make slang output something closer to Unity style
                request.ProcessCommandLineArguments(new string[] { "-no-mangle", "-no-hlsl-binding", "-no-hlsl-pack-constant-buffer-elements" });
                request.OverrideDiagnosticSeverity(15205, SlangSeverity.SLANG_SEVERITY_DISABLED); // undefined identifier in preprocessor expression will evaluate to 0
                request.OverrideDiagnosticSeverity(15400, SlangSeverity.SLANG_SEVERITY_DISABLED); // redefinition of macro
                request.OverrideDiagnosticSeverity(15601, SlangSeverity.SLANG_SEVERITY_DISABLED); // ignoring unknown directive
                request.OverrideDiagnosticSeverity(39019, SlangSeverity.SLANG_SEVERITY_DISABLED); // implicitly global shader parameter with no uniform keyword
                request.OverrideDiagnosticSeverity(30056, SlangSeverity.SLANG_SEVERITY_DISABLED); // non-short-circuiting ternary

                int translationUnitIndex = request.AddTranslationUnit(SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_SLANG, "Main Translation Unit");
                request.AddTranslationUnitSourceString(translationUnitIndex, filePath, fullCode);

                // Handle #pragma style entry point syntax, to avoid confusing the user.
                foreach (var entryPoint in knownEntryPoints)
                {
                    SlangStage stage = SlangStage.SLANG_STAGE_NONE;
                    switch (entryPoint.stage)
                    {
                        case "fragment": stage = SlangStage.SLANG_STAGE_FRAGMENT; break;
                        case "vertex": stage = SlangStage.SLANG_STAGE_VERTEX; break;
                        case "geometry": stage = SlangStage.SLANG_STAGE_GEOMETRY; break;
                        case "hull": stage = SlangStage.SLANG_STAGE_HULL; break;
                        case "domain": stage = SlangStage.SLANG_STAGE_DOMAIN; break;
                        default: break;
                    }
                    diagnostics.Add($"Shader uses #pragma syntax for specifying entry point '{entryPoint.entryName}'. " +
                        $"Annotate the entry point with the [shader(\"{entryPoint.stage}\")] attribute instead.");
                    request.AddEntryPoint(translationUnitIndex, entryPoint.entryName, stage);
                }

                SlangResult result = request.Compile();

                StringBuilder codeBuilder = new StringBuilder();
                AppendKeywordCombinationDirective(codeBuilder, keywords);
                if (result.IsOk)
                {
                    codeBuilder.AppendLine("#define SLANG_SHADER_VARIANT_FOUND 1");

                    // Get the name of each entry point, annotate them as Unity pragmas
                    SlangReflection refl = request.GetReflection();
                    uint entryPointCount = refl.GetEntryPointCount();
                    for (uint entryPointIdx = 0; entryPointIdx < entryPointCount; entryPointIdx++)
                    {
                        SlangReflectionEntryPoint entryPoint = refl.GetEntryPointByIndex(entryPointIdx);
                        SlangStage stage = entryPoint.GetStage();
                        string name = entryPoint.GetName();

                        switch (stage)
                        {
                            case SlangStage.SLANG_STAGE_VERTEX: outEntryPoints.Add(("vertex", name)); break;
                            case SlangStage.SLANG_STAGE_HULL: outEntryPoints.Add(("hull", name)); break;
                            case SlangStage.SLANG_STAGE_DOMAIN: outEntryPoints.Add(("domain", name)); break;
                            case SlangStage.SLANG_STAGE_GEOMETRY: outEntryPoints.Add(("geometry", name)); break;
                            case SlangStage.SLANG_STAGE_FRAGMENT: outEntryPoints.Add(("fragment", name)); break;
                            case SlangStage.SLANG_STAGE_COMPUTE: outEntryPoints.Add(("kernel", name)); break;
                            default: break;
                        }
                    }

                    dependencyFiles = request.GetDependencyFiles();

                    // Get the output code
                    string rawHlslCode = request.GetCompileRequestedCode();

                    // TODO: Optimize
                    // Strip some stuff Slang emit's which we don't care about
                    rawHlslCode = rawHlslCode
                        .Replace("#pragma pack_matrix(column_major)\n", "")
                        .Replace("#ifdef SLANG_HLSL_ENABLE_NVAPI\n#include \"nvHLSLExtns.h\"\n#endif\n", "")
                        .Replace("#pragma warning(disable: 3557)\n", "");

                    // Apply some semantic post processing to it
                    var decls = ShaderParser.ParseTopLevelDeclarations(rawHlslCode);
                    HLSLSlangEditor hlslEditor = new HLSLSlangEditor(rawHlslCode, decls.SelectMany(x => x.Tokens).ToList());
                    string processedHlslCode = hlslEditor.ApplyEdits(decls);

                    // Replace the code
                    codeBuilder.Append(processedHlslCode);
                }
                codeBuilder.AppendLine("#endif");

                diagnostics.AddRange(request.GetCollectedDiagnostics());

                return codeBuilder.ToString();
            }

            private void AppendKeywordCombinationDirective(StringBuilder sb, HashSet<string> keywords)
            {
                HashSet<string> remaining = new HashSet<string>(allKeywords);
                remaining.ExceptWith(keywords);

                sb.Append("#if ");
                foreach (string keyword in keywords)
                {
                    sb.Append($"defined({keyword}) && ");
                }
                foreach (string keyword in remaining)
                {
                    sb.Append($"!defined({keyword}) && ");
                }
                sb.AppendLine("1");
            }

            private static (ShaderCompilerPlatform, string) GetShaderCompilerPlatformAndKeyword()
            {
                switch (SystemInfo.graphicsDeviceType)
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

            private Dictionary<string, string> GetPredefinedDirectives()
            {
                Dictionary<string, string> directives = new Dictionary<string, string>();

                // Base defines
                directives.Add("SHADER_TARGET", "50"); // sm 5.0 assumed

                // Platform defines
                (ShaderCompilerPlatform compilerPlatform, string platformKw) = GetShaderCompilerPlatformAndKeyword();
                directives.Add(platformKw, "1");
                
                var builtinDefines = ShaderUtil.GetShaderPlatformKeywordsForBuildTarget(compilerPlatform, EditorUserBuildSettings.activeBuildTarget);
                foreach (BuiltinShaderDefine builtinDefine in builtinDefines)
                {
                    directives.Add(Enum.GetName(typeof(BuiltinShaderDefine), builtinDefine), "1");
                }

                if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS ||
                    EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android ||
                    EditorUserBuildSettings.activeBuildTarget == BuildTarget.tvOS)
                {
                    directives.Add("SHADER_API_MOBILE", "1");
                }

                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore ||
                    SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES2 ||
                    SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3)
                {
                    directives.Add("SHADER_TARGET_GLSL ", "1");
                }

                string unityVersion = Application.unityVersion.Replace(".", "");
                for (int i = 0; i < unityVersion.Length; i++)
                {
                    if (!char.IsDigit(unityVersion[i]))
                    {
                        unityVersion = unityVersion[..i];
                        break;
                    }
                }
                directives.Add("UNITY_VERSION ", unityVersion);

                return directives;
            }

            private static HashSet<(string stage, string entryName)> CollectEntryPoints(IEnumerable<HashSet<(string stage, string entryName)>> perVariantEntryPoints)
            {
                var allEntryPoints = new HashSet<(string stage, string entryName)>();
                foreach (var variantEntryPoints in perVariantEntryPoints)
                {
                    allEntryPoints.UnionWith(variantEntryPoints);
                }
                // If vertex or fragment is missing, add it
                if (allEntryPoints.Count(x => x.stage.ToLower() == "vertex") == 0)
                    allEntryPoints.Add(("vertex", "vert"));
                if (allEntryPoints.Count(x => x.stage.ToLower() == "fragment") == 0)
                    allEntryPoints.Add(("fragment", "frag"));
                return allEntryPoints;
            }

            private static string GetFallbackVariant(IEnumerable<(string stage, string entryName)> entryPoints)
            {
                StringBuilder fallbackVariantBuilder = new StringBuilder();
                fallbackVariantBuilder.AppendLine("#ifndef SLANG_SHADER_VARIANT_FOUND");
                foreach ((string stage, string entryName) in entryPoints)
                {
                    if (stage.ToLower() == "fragment")
                    {
                        fallbackVariantBuilder.AppendLine($"float4 {entryName}(float4 pos : SV_Position) : SV_Target {{ return float4(0,1,1,1); }}");
                    }
                    else if (stage.ToLower() == "vertex")
                    {
                        fallbackVariantBuilder.AppendLine("#include \"UnityCG.cginc\"");
                        fallbackVariantBuilder.AppendLine($"float4 {entryName}(float4 pos : POSITION) : SV_Position {{ return UnityObjectToClipPos(pos); }}");
                    }
                    else
                    {
                        fallbackVariantBuilder.AppendLine($"void {entryName}() {{}}");
                    }
                }
                fallbackVariantBuilder.AppendLine("#endif");
                return fallbackVariantBuilder.ToString();
            }
        }

        [SerializeField]
        public string GeneratedSourceCode;

        [SerializeField]
        public SlangShaderVariant[] GeneratedVariants;

        [SerializeField]
        public SlangShaderDiagnostic[] Diagnostics;

        [SerializeField]
        public bool AddVariantsRequested = false;

        private static HashSet<SlangShaderVariant> GetInitialVariants(AssetImportContext ctx, string assetPath, string shaderSource, ShaderNode shaderNode)
        {
            ShaderLabSlangEditor editor = new ShaderLabSlangEditor(assetPath, new SlangShaderVariant[] { new SlangShaderVariant(new HashSet<string>()) }, shaderSource, shaderNode.Tokens);
            string proxySourceCode = editor.ApplyEdits(shaderNode);

            Shader variantInfoProxyShader = ShaderUtil.CreateShaderAsset(ctx, proxySourceCode, false);
            HashSet<SlangShaderVariant> requestedVariants = SlangShaderVariantTracker.GetSlangShaderVariantsForShaderFromCurrentScene(variantInfoProxyShader);
            ShaderUtil.ClearShaderMessages(variantInfoProxyShader);

            if (Application.isPlaying)
                Destroy(variantInfoProxyShader);
            else
                DestroyImmediate(variantInfoProxyShader);

            return requestedVariants;
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            SlangShaderVariantTracker.SlangShaderPaths.Add(ctx.assetPath);

            string shaderSource = File.ReadAllText(ctx.assetPath);

            var newDiags = new List<SlangShaderDiagnostic>();
            ShaderLabParserConfig config = new ShaderLabParserConfig
            {
                ParseEmbeddedHLSL = false
            };
            var shaderNode = ShaderParser.ParseUnityShader(shaderSource, config, out var diagnostics);
            foreach (var parserDiag in diagnostics)
            {
                LogDiagnostic(newDiags, parserDiag.Text, parserDiag.Span.FileName, parserDiag.Location.Line, parserDiag.Kind.HasFlag(DiagnosticFlags.Warning));
            }

            // If we are trying to add variants, don't overwrite with initial ones.
            if (AddVariantsRequested)
            {
                AddVariantsRequested = false;
            }
            else
            {
                // If we already have some variants tracked, used those as initial variants
                if (SlangShaderVariantTracker.CurrentlyLoadedSlangShaderVariants.TryGetValue(ctx.assetPath, out var variants))
                {
                    GeneratedVariants = variants.ToArray();
                }
                // Otherwise use the null variant (TODO)
                else
                {
                    GeneratedVariants = new SlangShaderVariant[] { };
                }
            }
            EditorUtility.SetDirty(this);

            ShaderLabSlangEditor editor = new ShaderLabSlangEditor(ctx.assetPath, GeneratedVariants, shaderSource, shaderNode.Tokens);
            GeneratedSourceCode = editor.ApplyEdits(shaderNode);
            foreach (var slangDiag in editor.Diagnostics)
            {
                string errorLine = slangDiag.Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries).First();
                var match = Regex.Match(errorLine, @"(.*)\(([0-9]+)\): (.*)$");
                if (match.Success)
                {
                    if (!match.Groups[3].Value.StartsWith("note"))
                        LogDiagnostic(newDiags, match.Groups[3].Value, match.Groups[1].Value, int.Parse(match.Groups[2].Value), !slangDiag.Contains("error"));
                }
                else
                {
                    LogDiagnostic(newDiags, errorLine, "", 0, !slangDiag.Contains("error"));
                }
            }

            foreach (string dependencyFile in editor.DependencyFiles)
            {
                ctx.DependsOnSourceAsset(dependencyFile);
            }

            var shaderAsset = ShaderUtil.CreateShaderAsset(ctx, GeneratedSourceCode, true);

            var messages = ShaderUtil.GetShaderMessages(shaderAsset);
            if (messages.Length > 0)
            {
                foreach (var message in messages)
                {
                    LogDiagnostic(newDiags, message.message, message.file, message.line, message.severity == ShaderCompilerMessageSeverity.Warning);
                }
            }
            ShaderUtil.ClearShaderMessages(shaderAsset);

            ctx.AddObjectToAsset("Generated shader", shaderAsset);
            ctx.SetMainObject(shaderAsset);

            // Check if any generated variants use keywords that don't exist (for example beacause they were removed from the shader).
            // In this case, we have no choice but to reset to variant tracker, which will trigger a reimport.
            HashSet<string> allKeywords = Shader.globalKeywords
                .Select(x => x.name)
                .Union(shaderAsset.keywordSpace.keywords.Select(x => x.name))
                .ToHashSet();
            foreach (SlangShaderVariant variant in GeneratedVariants)
            {
                if (variant.Keywords.Except(allKeywords).Any())
                {
                    SlangShaderVariantTracker.ResetTrackedVariants();
                    return; // Early out so we don't print diagnostics from this invalid compilation.
                }
            }

            Diagnostics = newDiags.ToArray();
            foreach (var diag in Diagnostics)
            {
                if (!diag.Warning)
                {
                    ctx.LogImportError($"{ctx.assetPath}({diag.Line}): {diag.Text}");
                }
            }
        }

        private static void LogDiagnostic(List<SlangShaderDiagnostic> diags, string message, string file, int line, bool warning)
        {
            diags.Add(new SlangShaderDiagnostic { Text = message, File = file, Line = line, Warning = warning });
        }
    }
}