using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace UnitySlangShader.SlangAPI
{
    public readonly struct SlangResult
    {
        public readonly int Code;

        public SlangResult(int code)
        {
            this.Code = code;
        }

        public bool IsOk => Code >= 0;
        public bool IsError => Code < 0;

        public static implicit operator SlangResult(int code) => new SlangResult(code);
    }

    public enum SlangCompileTarget : int
    {
        SLANG_TARGET_UNKNOWN,
        SLANG_TARGET_NONE,
        SLANG_GLSL,
        SLANG_GLSL_VULKAN,
        SLANG_GLSL_VULKAN_ONE_DESC,
        SLANG_HLSL,
        SLANG_SPIRV,
        SLANG_SPIRV_ASM,
        SLANG_DXBC,
        SLANG_DXBC_ASM,
        SLANG_DXIL,
        SLANG_DXIL_ASM,
        SLANG_C_SOURCE,
        SLANG_CPP_SOURCE,
        SLANG_HOST_EXECUTABLE,
        SLANG_SHADER_SHARED_LIBRARY,
        SLANG_SHADER_HOST_CALLABLE,
        SLANG_CUDA_SOURCE,
        SLANG_PTX,
        SLANG_CUDA_OBJECT_CODE,
        SLANG_OBJECT_CODE,
        SLANG_HOST_CPP_SOURCE,
        SLANG_HOST_HOST_CALLABLE,
        SLANG_CPP_PYTORCH_BINDING,
        SLANG_TARGET_COUNT_OF,
    }

    public enum SlangLineDirectiveMode : uint
    {
        SLANG_LINE_DIRECTIVE_MODE_DEFAULT = 0,
        SLANG_LINE_DIRECTIVE_MODE_NONE,       
        SLANG_LINE_DIRECTIVE_MODE_STANDARD,   
        SLANG_LINE_DIRECTIVE_MODE_GLSL,       
        SLANG_LINE_DIRECTIVE_MODE_SOURCE_MAP, 
    };

    [Flags]
    public enum SlangTargetFlags : uint
    {
        SLANG_TARGET_FLAG_PARAMETER_BLOCKS_USE_REGISTER_SPACES = 1 << 4,
        SLANG_TARGET_FLAG_GENERATE_WHOLE_PROGRAM = 1 << 8,
        SLANG_TARGET_FLAG_DUMP_IR = 1 << 9,
        SLANG_TARGET_FLAG_GENERATE_SPIRV_DIRECTLY = 1 << 10,
    }

    public enum SlangSourceLanguage : int
    {
        SLANG_SOURCE_LANGUAGE_UNKNOWN,
        SLANG_SOURCE_LANGUAGE_SLANG,
        SLANG_SOURCE_LANGUAGE_HLSL,
        SLANG_SOURCE_LANGUAGE_GLSL,
        SLANG_SOURCE_LANGUAGE_C,
        SLANG_SOURCE_LANGUAGE_CPP,
        SLANG_SOURCE_LANGUAGE_CUDA,
        SLANG_SOURCE_LANGUAGE_SPIRV,
        SLANG_SOURCE_LANGUAGE_COUNT_OF,
    };

    public enum SlangSeverity : int
    {
        SLANG_SEVERITY_DISABLED = 0,
        SLANG_SEVERITY_NOTE,        
        SLANG_SEVERITY_WARNING,     
        SLANG_SEVERITY_ERROR,       
        SLANG_SEVERITY_FATAL,       
        SLANG_SEVERITY_INTERNAL,    
    }

    public enum SlangStage : uint
    {
        SLANG_STAGE_NONE,
        SLANG_STAGE_VERTEX,
        SLANG_STAGE_HULL,
        SLANG_STAGE_DOMAIN,
        SLANG_STAGE_GEOMETRY,
        SLANG_STAGE_FRAGMENT,
        SLANG_STAGE_COMPUTE,
        SLANG_STAGE_RAY_GENERATION,
        SLANG_STAGE_INTERSECTION,
        SLANG_STAGE_ANY_HIT,
        SLANG_STAGE_CLOSEST_HIT,
        SLANG_STAGE_MISS,
        SLANG_STAGE_CALLABLE,
        SLANG_STAGE_MESH,
        SLANG_STAGE_AMPLIFICATION,
        SLANG_STAGE_PIXEL = SLANG_STAGE_FRAGMENT,
    }

    public enum SlangMatrixLayoutMode : uint
    {
        SLANG_MATRIX_LAYOUT_MODE_UNKNOWN = 0,
        SLANG_MATRIX_LAYOUT_ROW_MAJOR,
        SLANG_MATRIX_LAYOUT_COLUMN_MAJOR,
    }

    public sealed class CompileRequest : IDisposable
    {
        #region Bindings
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern IntPtr spCreateCompileRequest(IntPtr session);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void spDestroyCompileRequest(IntPtr request);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void spSetCodeGenTarget(IntPtr request, SlangCompileTarget target);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void spSetMatrixLayoutMode(IntPtr request, SlangMatrixLayoutMode mode);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void spSetTargetFlags(IntPtr request, int targetIndex, SlangTargetFlags flags);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void spSetTargetLineDirectiveMode(IntPtr request, int targetIndex, SlangLineDirectiveMode mode);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int spAddTranslationUnit(IntPtr request, SlangSourceLanguage language, [MarshalAs(UnmanagedType.LPStr)] string name);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void spAddTranslationUnitSourceFile(IntPtr request, int translationUnitIndex, [MarshalAs(UnmanagedType.LPStr)] string path);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void spAddTranslationUnitSourceString(IntPtr request, int translationUnitIndex, [MarshalAs(UnmanagedType.LPStr)] string path, [MarshalAs(UnmanagedType.LPStr)] string source);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int spAddEntryPoint(IntPtr request, int translationUnitIndex, [MarshalAs(UnmanagedType.LPStr)] string name, SlangStage stage);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void spAddSearchPath(IntPtr request, [MarshalAs(UnmanagedType.LPStr)] string searchDir);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void spAddPreprocessorDefine(IntPtr request, [MarshalAs(UnmanagedType.LPStr)] string key, [MarshalAs(UnmanagedType.LPStr)] string val);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int spProcessCommandLineArguments(IntPtr request, [In] string[] args, int argCount);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int spCompile(IntPtr request);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern IntPtr spGetCompileRequestCode(IntPtr request, [Out] out nuint size);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern IntPtr spGetDiagnosticOutput(IntPtr request);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int spGetDependencyFileCount(IntPtr request);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern IntPtr spGetDependencyFilePath(IntPtr request, int index);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void spOverrideDiagnosticSeverity(IntPtr request, int messageID, SlangSeverity overrideSeverity);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void spSetDiagnosticCallback(IntPtr request, [MarshalAs(UnmanagedType.FunctionPtr)] SlangDiagnosticCallback callback, IntPtr userData);
        private delegate void SlangDiagnosticCallback(IntPtr message, IntPtr userData);
        #endregion

        private readonly IntPtr request = IntPtr.Zero;
        private readonly GCHandle handle;
        private readonly List<string> diagnostics = new List<string>();

        public CompileRequest(IntPtr session)
        {
            handle = GCHandle.Alloc(this);
            request = spCreateCompileRequest(session);
            spSetDiagnosticCallback(request, DiagnosticCallback, (IntPtr)handle);
        }

        private static readonly string[] ignoreDiagnosticsFrom = new string[]
        {
            "Data/CGIncludes/UnityCG.cginc", "Data/CGIncludes/HLSLSupport.cginc", "Data/CGIncludes/UnityShaderVariables.cginc",
            "Data/CGIncludes/UnityShadowLibrary.cginc", "Data/CGIncludes/UnityStandardUtils.cginc", "Data/CGIncludes/UnityStandardShadow.cginc",
            "Data/CGIncludes/UnityStandardBRDF.cginc", "Data/CGIncludes/UnityImageBasedLighting.cginc", "Data/CGIncludes/UnityGlobalIllumination.cginc",
            "Data/CGIncludes/UnityStandardConfig.cginc", "Data/CGIncludes/UnityGBuffer.cginc", "Data/CGIncludes/UnityDeprecated.cginc", "Data/CGIncludes/Lighting.cginc",
            "Data/CGIncludes/AutoLight.cginc"
        };

        private static void DiagnosticCallback(IntPtr message, IntPtr userData)
        {
            string messageStr = Marshal.PtrToStringAnsi(message);
            string[] splits = messageStr.Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            bool ignored =
                splits.Length > 0 &&
                ignoreDiagnosticsFrom.Any(x => splits[0].Contains(x)) &&
                !splits[0].Contains("error");
            // Filter out warnings from builtin files
            if (!ignored)
            {
                GCHandle selfHandle = (GCHandle)userData;
                CompileRequest self = selfHandle.Target as CompileRequest;
                self?.diagnostics.Add(messageStr);
            }
        }

        public void SetCodeGenTarget(SlangCompileTarget target) => spSetCodeGenTarget(request, target);
        public void SetMatrixLayoutMode(SlangMatrixLayoutMode mode) => spSetMatrixLayoutMode(request, mode);
        public void SetTargetFlags(SlangTargetFlags flags) => spSetTargetFlags(request, 0, flags);
        public void SetTargetLineDirectiveMode(SlangLineDirectiveMode mode) => spSetTargetLineDirectiveMode(request, 0, mode);
        public int AddTranslationUnit(SlangSourceLanguage language, string name) => spAddTranslationUnit(request, language, name);
        public void AddTranslationUnitSourceFile(int translationUnitIndex, string path) => spAddTranslationUnitSourceFile(request, translationUnitIndex, path);
        public void AddTranslationUnitSourceString(int translationUnitIndex, string path, string source) => spAddTranslationUnitSourceString(request, translationUnitIndex, path, source);
        public int AddEntryPoint(int translationUnitIndex, string name, SlangStage stage) => spAddEntryPoint(request, translationUnitIndex, name, stage);
        public void AddSearchPath(string searchDir) => spAddSearchPath(request, searchDir);
        public void AddPreprocessorDefine(string key, string val) => spAddPreprocessorDefine(request, key, val);
        public void OverrideDiagnosticSeverity(int messageID, SlangSeverity overrideSeverity) => spOverrideDiagnosticSeverity(request, messageID, overrideSeverity);

        public SlangResult Compile() => spCompile(request);
        public List<string> GetCollectedDiagnostics() => diagnostics;
        public SlangReflection GetReflection() => new SlangReflection(request);

        public void ProcessCommandLineArguments(IEnumerable<string> args)
        {
            string[] argsArray = args.ToArray();
            spProcessCommandLineArguments(request, argsArray, argsArray.Length);
        }
        
        public string GetCompileRequestedCode()
        {
            IntPtr strPtr = spGetCompileRequestCode(request, out nuint size);
            return Marshal.PtrToStringAnsi(strPtr, (int)size);
        }

        public string[] GetDependencyFiles()
        {
            int count = spGetDependencyFileCount(request);
            string[] result = new string[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = Marshal.PtrToStringAnsi(spGetDependencyFilePath(request, i));
            }
            return result;
        }

        public void Dispose()
        {
            spSetDiagnosticCallback(request, null, IntPtr.Zero);
            spDestroyCompileRequest(request);
            handle.Free();
        }
    }

    public sealed class SlangReflection
    {
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern IntPtr spGetReflection(IntPtr request);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern uint spReflection_getEntryPointCount(IntPtr reflection);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern IntPtr spReflection_getEntryPointByIndex(IntPtr reflection, uint index);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern IntPtr spReflection_findEntryPointByName(IntPtr reflection, [MarshalAs(UnmanagedType.LPStr)] string name);

        private IntPtr reflection = IntPtr.Zero;

        public SlangReflection(IntPtr request)
        {
            reflection = spGetReflection(request);
        }

        public uint GetEntryPointCount() => spReflection_getEntryPointCount(reflection);
        public SlangReflectionEntryPoint GetEntryPointByIndex(uint index) => new SlangReflectionEntryPoint(spReflection_getEntryPointByIndex(reflection, index));
        public SlangReflectionEntryPoint FindEntryPointByName(string name) => new SlangReflectionEntryPoint(spReflection_findEntryPointByName(reflection, name));
    }

    public sealed class SlangReflectionEntryPoint
    {
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern SlangStage spReflectionEntryPoint_getStage(IntPtr entryPoint);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern IntPtr spReflectionEntryPoint_getName(IntPtr entryPoint);

        private IntPtr entryPoint = IntPtr.Zero;

        public SlangReflectionEntryPoint(IntPtr entryPoint)
        {
            this.entryPoint = entryPoint;
        }

        public SlangStage GetStage() => spReflectionEntryPoint_getStage(entryPoint);

        public string GetName()
        {
            IntPtr strPtr = spReflectionEntryPoint_getName(entryPoint);
            return Marshal.PtrToStringAnsi(strPtr);
        }
    }

    public sealed class SlangSession : IDisposable
    {
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern IntPtr spCreateSession([MarshalAs(UnmanagedType.LPStr)] string lpString = null);

        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void spDestroySession(IntPtr session);

        private readonly IntPtr session;

        public SlangSession()
        {
            session = spCreateSession();
        }

        public void Dispose()
        {
            spDestroySession(session);
        }

        public CompileRequest CreateCompileRequest()
        {
            return new CompileRequest(session);
        }
    }
}