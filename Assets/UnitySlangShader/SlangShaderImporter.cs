using UnityEditor;
using UnityEngine;
using UnityEditor.AssetImporters;
using System.IO;
using UnityShaderParser.ShaderLab;
using UnityShaderParser.Common;
using System.Collections.Generic;
using System.Diagnostics;
using System;
using System.Text.RegularExpressions;
using System.Text;
using UnitySlangShader.SlangAPI;

namespace UnitySlangShader
{
    [ScriptedImporter(1, "slangshader")]
    public class SlangShaderImporter : ScriptedImporter
    {
        private class ShaderLabSlangEditor : ShaderLabEditor
        {
            public List<string> Diagnostics = new List<string>();

            public ShaderLabSlangEditor(string source, List<Token<TokenKind>> tokens)
                : base(source, tokens)
            {
                // Delete all include blocks
                foreach (var token in tokens)
                {
                    if (token.Kind == TokenKind.IncludeBlock)
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

                var matches = Regex.Matches(fullCode, @"#pragma (.+)$");
                foreach (Match pragma in matches)
                {
                    string trimmed = pragma.Groups[0].Value.Trim();
                    if (trimmed == string.Empty)
                        continue;

                    // TODO: Strip from source
                    string[] parts = trimmed.Split(' ');
                    pragmas.Add(parts);
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
                var pragmas = ExtractPragmasFromCode(programBlock.FullCode);
                var entryPoints = FindEntryPointPragmas(pragmas);

                using SlangSession session = new SlangSession();
                using CompileRequest request = session.CreateCompileRequest();
                request.SetCodeGenTarget(SlangCompileTarget.SLANG_HLSL);
                request.SetTargetFlags(SlangTargetFlags.SLANG_TARGET_FLAG_GENERATE_WHOLE_PROGRAM);
                request.SetTargetLineDirectiveMode(SlangLineDirectiveMode.SLANG_LINE_DIRECTIVE_MODE_NONE);
                request.AddSearchPath(@"C:\Program Files\6000.0.0b15\Editor\Data\CGIncludes");
                request.AddPreprocessorDefine("SHADER_API_D3D11", "1"); // TODO: Base these on the current state of the editor
                request.AddPreprocessorDefine("UNITY_COMPILER_DXC", "1");
                request.AddPreprocessorDefine("UNITY_UNIFIED_SHADER_PRECISION_MODEL", "1");
                request.AddPreprocessorDefine("min16float", "float");
                request.AddPreprocessorDefine("min16float1", "float1");
                request.AddPreprocessorDefine("min16float2", "float2");
                request.AddPreprocessorDefine("min16float3", "float3");
                request.AddPreprocessorDefine("min16float4", "float4");
                request.ProcessCommandLineArguments(new string[] { "-no-mangle", "-no-hlsl-binding" });
                int translationUnitIndex = request.AddTranslationUnit(SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_HLSL, "Main Translation Unit");
                request.AddTranslationUnitSourceString(translationUnitIndex, "", programBlock.FullCode);
                SlangResult result = request.Compile();
                if (result.IsOk)
                {
                    Edit(programBlock.Span, $"HLSLPROGRAM\n{request.GetCompileRequestedCode()}\nENDHLSL"); // TODO: Indent
                }
                else
                {
                    Diagnostics.Add(request.GetDiagnosticOutput());
                }
            }
        }

        [SerializeField]
        public string GeneratedSourceCode;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            string shaderSource = File.ReadAllText(ctx.assetPath);

            ShaderLabParserConfig config = new ShaderLabParserConfig
            {
                ParseEmbeddedHLSL = false
            };
            var shaderNode = ShaderParser.ParseUnityShader(shaderSource, config, out var diagnostics);
            foreach (var parserDiag in diagnostics)
            {
                if (parserDiag.Kind.HasFlag(DiagnosticFlags.Warning))
                {
                    ctx.LogImportWarning(parserDiag.ToString());
                }
                else
                {
                    ctx.LogImportError(parserDiag.ToString());
                }
            }

            ShaderLabSlangEditor editor = new ShaderLabSlangEditor(shaderSource, shaderNode.Tokens);
            GeneratedSourceCode = editor.ApplyEdits(shaderNode);
            foreach (var slangDiag in editor.Diagnostics)
            {
                if (slangDiag.Contains("error"))
                {
                    ctx.LogImportError(slangDiag);
                }
                else
                {
                    ctx.LogImportWarning(slangDiag);
                }
            }

            var shaderAsset = ShaderUtil.CreateShaderAsset(ctx, GeneratedSourceCode, true);

            if (ShaderUtil.ShaderHasError(shaderAsset))
            {
                var errors = ShaderUtil.GetShaderMessages(shaderAsset);
                foreach (var error in errors)
                {
                    ctx.LogImportError(error.message + $" on line {error.line} in {ctx.assetPath}");
                }
            }
            else
            {
                ShaderUtil.ClearShaderMessages(shaderAsset);
            }

            ctx.AddObjectToAsset("Generated shader", shaderAsset);
            ctx.SetMainObject(shaderAsset);
        }
    }
}