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
            public List<string> Diagnostics = new List<string>();

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

            public void HandleProgramBlock(HLSLProgramBlock programBlock)
            {
                string fullCodeWithLineStart = $"#line {programBlock.Span.Start.Line - 1}\n{programBlock.FullCode}";
                var pragmas = ExtractPragmasFromCode(fullCodeWithLineStart);
                var entryPoints = FindEntryPointPragmas(pragmas);

                StringBuilder allVariants = new StringBuilder();
                foreach (var variant in variantsToGenerate)
                {
                    allVariants.AppendLine(CompileVariant(fullCodeWithLineStart, variant.Keywords, entryPoints));
                }

                Edit(programBlock.Span, $"HLSLPROGRAM\n{allVariants.ToString()}\nENDHLSL");
            }

            private string CompileVariant(string fullCode, string[] keywords, List<(string stage, string entryName)> entryPoints)
            {
                using SlangSession session = new SlangSession();
                using CompileRequest request = session.CreateCompileRequest();

                request.SetCodeGenTarget(SlangCompileTarget.SLANG_HLSL);
                request.SetMatrixLayoutMode(SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR);
                request.SetTargetFlags(SlangTargetFlags.SLANG_TARGET_FLAG_GENERATE_WHOLE_PROGRAM);
                request.SetTargetLineDirectiveMode(SlangLineDirectiveMode.SLANG_LINE_DIRECTIVE_MODE_NONE);

                request.AddSearchPath($"{EditorApplication.applicationContentsPath}/CGIncludes");

                // Base defines
                request.AddPreprocessorDefine("SHADER_API_D3D11", "1"); // TODO: Base these on the current state of the editor
                request.AddPreprocessorDefine("UNITY_COMPILER_DXC", "1");
                request.AddPreprocessorDefine("UNITY_UNIFIED_SHADER_PRECISION_MODEL", "1");
                request.AddPreprocessorDefine("min16float", "float");
                request.AddPreprocessorDefine("min16float1", "float1");
                request.AddPreprocessorDefine("min16float2", "float2");
                request.AddPreprocessorDefine("min16float3", "float3");
                request.AddPreprocessorDefine("min16float4", "float4");

                // Define keywords
                foreach (var keyword in keywords)
                {
                    request.AddPreprocessorDefine(keyword, "1");
                }

                request.ProcessCommandLineArguments(new string[] { "-no-mangle", "-no-hlsl-binding", "-no-hlsl-pack-constant-buffer-elements" });

                request.OverrideDiagnosticSeverity(15205, SlangSeverity.SLANG_SEVERITY_DISABLED); // undefined identifier in preprocessor expression will evaluate to 0
                request.OverrideDiagnosticSeverity(15400, SlangSeverity.SLANG_SEVERITY_DISABLED); // redefinition of macro
                request.OverrideDiagnosticSeverity(15601, SlangSeverity.SLANG_SEVERITY_DISABLED); // ignoring unknown directive
                request.OverrideDiagnosticSeverity(39019, SlangSeverity.SLANG_SEVERITY_DISABLED); // implicitly global shader parameter with no uniform keyword

                int translationUnitIndex = request.AddTranslationUnit(SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_HLSL, "Main Translation Unit");
                request.AddTranslationUnitSourceString(translationUnitIndex, filePath, fullCode);

                // Handle #pragma style entry point syntax, to avoid confusing the user.
                foreach (var entryPoint in entryPoints)
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
                    Diagnostics.Add($"Shader uses #pragma syntax for specifying entry point '{entryPoint.entryName}'. " +
                        $"Please consider annotating the entry point with the [shader(\"{entryPoint.stage}\")] attribute instead.");
                    request.AddEntryPoint(translationUnitIndex, entryPoint.entryName, stage);
                }

                SlangResult result = request.Compile();

                StringBuilder codeBuilder = new StringBuilder();
                AppendKeywordCombinationDirective(codeBuilder, keywords);
                if (result.IsOk)
                {
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
                            case SlangStage.SLANG_STAGE_VERTEX: codeBuilder.AppendLine($"#pragma vertex {name}"); break;
                            case SlangStage.SLANG_STAGE_HULL: codeBuilder.AppendLine($"#pragma hull {name}"); break;
                            case SlangStage.SLANG_STAGE_DOMAIN: codeBuilder.AppendLine($"#pragma domain {name}"); break;
                            case SlangStage.SLANG_STAGE_GEOMETRY: codeBuilder.AppendLine($"#pragma geometry {name}"); break;
                            case SlangStage.SLANG_STAGE_FRAGMENT: codeBuilder.AppendLine($"#pragma fragment {name}"); break;
                            case SlangStage.SLANG_STAGE_COMPUTE: codeBuilder.AppendLine($"#pragma kernel {name}"); break;
                            default: break;
                        }
                    }

                    // Get the output code
                    string rawHlslCode = request.GetCompileRequestedCode();

                    // Strip the #pragma pack_matrix directive from it - we don't need it
                    rawHlslCode = rawHlslCode.Replace("#pragma pack_matrix(column_major)\n", "");

                    // Apply some semantic post processing to it
                    var decls = ShaderParser.ParseTopLevelDeclarations(rawHlslCode);
                    HLSLSlangEditor hlslEditor = new HLSLSlangEditor(rawHlslCode, decls.SelectMany(x => x.Tokens).ToList());
                    string processedHlslCode = hlslEditor.ApplyEdits(decls);

                    // Replace the code
                    codeBuilder.Append(processedHlslCode);
                }
                codeBuilder.AppendLine("#endif");

                // TODO: Handle error case (make pink shader)

                Diagnostics.AddRange(request.GetCollectedDiagnostics());

                return codeBuilder.ToString();
            }

            private void AppendKeywordCombinationDirective(StringBuilder sb, string[] keywords)
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
        }

        [SerializeField]
        public string GeneratedSourceCode;

        [SerializeField]
        public SlangShaderVariant[] GeneratedVariants;

        [SerializeField]
        public SlangShaderDiagnostic[] Diagnostics;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var newDiags = new List<SlangShaderDiagnostic>();

            if (GeneratedVariants == null || GeneratedVariants.Length == 0)
            {
                GeneratedVariants = new SlangShaderVariant[] { new SlangShaderVariant(Array.Empty<string>()) };
            }

            string shaderSource = File.ReadAllText(ctx.assetPath);

            ShaderLabParserConfig config = new ShaderLabParserConfig
            {
                ParseEmbeddedHLSL = false
            };
            var shaderNode = ShaderParser.ParseUnityShader(shaderSource, config, out var diagnostics);
            foreach (var parserDiag in diagnostics)
            {
                LogDiagnostic(newDiags, parserDiag.Text, parserDiag.Span.FileName, parserDiag.Location.Line, parserDiag.Kind.HasFlag(DiagnosticFlags.Warning));
            }

            ShaderLabSlangEditor editor = new ShaderLabSlangEditor(ctx.assetPath, GeneratedVariants, shaderSource, shaderNode.Tokens);
            GeneratedSourceCode = editor.ApplyEdits(shaderNode);
            foreach (var slangDiag in editor.Diagnostics)
            {
                string errorLine = slangDiag.Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries).First();
                var match = Regex.Match(errorLine, @"(.*)\(([0-9]+)\): (.*)$");
                if (match.Success)
                {
                    LogDiagnostic(newDiags, match.Groups[3].Value, match.Groups[1].Value, int.Parse(match.Groups[2].Value), !slangDiag.Contains("error"));
                }
                else
                {
                    LogDiagnostic(newDiags, errorLine, "", 0, !slangDiag.Contains("error"));
                }
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

            Diagnostics = newDiags.ToArray();
            foreach (var diag in Diagnostics)
            {
                if (!diag.Warning)
                {
                    ctx.LogImportError($"{ctx.assetPath}({diag.Line}): {diag.Text}");
                }
            }

            ctx.AddObjectToAsset("Generated shader", shaderAsset);
            ctx.SetMainObject(shaderAsset);
        }

        private static void LogDiagnostic(List<SlangShaderDiagnostic> diags, string message, string file, int line, bool warning)
        {
            diags.Add(new SlangShaderDiagnostic { Text = message, File = file, Line = line, Warning = warning });
        }
    }
}