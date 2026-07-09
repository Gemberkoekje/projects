using Vortice.Dxc;

namespace RayTracer.Gpu;

/// <summary>
/// Compiles HLSL to DXIL at runtime via the DXC compiler shipped with
/// Vortice.Dxc. Runtime compilation keeps iteration fast and sets up the
/// shader hot-reload path noted in the plan (pitfall P11).
/// </summary>
internal static class ShaderCompiler
{
    /// <summary>
    /// Compiles a compute shader to DXIL bytecode. Throws with the DXC
    /// diagnostics if compilation fails.
    /// </summary>
    public static byte[] CompileCompute(string source, string entryPoint, string sourceName)
    {
        // RayQuery (inline ray tracing) requires Shader Model 6.5.
        var options = new DxcCompilerOptions
        {
            ShaderModel = new DxcShaderModel(6, 5),
        };

        using IDxcResult result = DxcCompiler.Compile(
            DxcShaderStage.Compute,
            source,
            entryPoint,
            options,
            fileName: sourceName);

        if (result.GetStatus().Failure)
        {
            string errors = result.GetErrors();
            throw new InvalidOperationException(
                $"Shader compilation failed for '{sourceName}' (entry '{entryPoint}'):\n{errors}");
        }

        return result.GetObjectBytecodeArray();
    }
}
