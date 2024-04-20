using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor.Build;
using UnityEditor.Rendering;

public class TestShaderPreProcessor : IPreprocessShaders
{
    // Use callbackOrder to set when Unity calls this shader preprocessor. Unity starts with the preprocessor that has the lowest callbackOrder value.
    public int callbackOrder { get { return 0; } }

    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
    {

        Debug.Log("OnProcessShader: " + shader.name + " " + snippet.passType + " " + snippet.passName);
    }
}