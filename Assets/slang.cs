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

    public sealed class CompileRequest : IDisposable
    {
        private readonly IntPtr request;

        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern IntPtr spCreateCompileRequest(IntPtr session);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void spDestroyCompileRequest(IntPtr request);
        [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void spSetCodeGenTarget(IntPtr request, SlangCompileTarget target);
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

        public CompileRequest(IntPtr session)
        {
            request = spCreateCompileRequest(session);
        }

        public void SetCodeGenTarget(SlangCompileTarget target) => spSetCodeGenTarget(request, target);
        public void SetTargetFlags(SlangTargetFlags flags) => spSetTargetFlags(request, 0, flags);
        public void SetTargetLineDirectiveMode(SlangLineDirectiveMode mode) => spSetTargetLineDirectiveMode(request, 0, mode);
        public int AddTranslationUnit(SlangSourceLanguage language, string name) => spAddTranslationUnit(request, language, name);
        public void AddTranslationUnitSourceFile(int translationUnitIndex, string path) => spAddTranslationUnitSourceFile(request, translationUnitIndex, path);
        public void AddTranslationUnitSourceString(int translationUnitIndex, string path, string source) => spAddTranslationUnitSourceString(request, translationUnitIndex, path, source);
        public void AddSearchPath(string searchDir) => spAddSearchPath(request, searchDir);
        public void AddPreprocessorDefine(string key, string val) => spAddPreprocessorDefine(request, key, val);

        public SlangResult Compile() => spCompile(request);

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

        public string GetDiagnosticOutput()
        {
            IntPtr strPtr = spGetDiagnosticOutput(request);
            return Marshal.PtrToStringAnsi(strPtr);
        }

        public void Dispose()
        {
            spDestroyCompileRequest(request);
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