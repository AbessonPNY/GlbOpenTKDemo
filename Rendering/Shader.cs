using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Diagnostics;


namespace GlbOpenTKDemo.Rendering;

public sealed class CharShader : IDisposable
{
    private readonly int _program;

    public CharShader(string vertPath, string fragPath)
    {
        string vert = File.ReadAllText(vertPath);
        string frag = File.ReadAllText(fragPath);

        int v = Compile(ShaderType.VertexShader, vert);
        int f = Compile(ShaderType.FragmentShader, frag);

        _program = GL.CreateProgram();
        GL.AttachShader(_program, v);
        GL.AttachShader(_program, f);
        GL.LinkProgram(_program);
        GL.GetProgram(_program, GetProgramParameterName.LinkStatus, out int status);
        if (status == 0)
            throw new Exception("Shader link error: " + GL.GetProgramInfoLog(_program));

        GL.DetachShader(_program, v);
        GL.DetachShader(_program, f);
        GL.DeleteShader(v);
        GL.DeleteShader(f);
    }


    public int GetBlockIndex(string blockName) => GL.GetUniformBlockIndex(_program, blockName);

    public void DebugCheckUniformBlock(string blockName, int expectedBinding)
    {
        Debug.WriteLine("DebugCheckUniformBlock");
        int idx = GL.GetUniformBlockIndex(_program, blockName);
        Console.WriteLine($"[Shader] Block '{blockName}' index={idx}");
        if (idx < 0) { Console.WriteLine(" introuvable (nom, #version, link)"); return; }

        GL.GetActiveUniformBlock(_program, idx, ActiveUniformBlockParameter.UniformBlockBinding, out int binding);
        GL.GetActiveUniformBlock(_program, idx, ActiveUniformBlockParameter.UniformBlockDataSize, out int size);
        Debug.WriteLine($"  binding={binding}, dataSize={size} bytes");
    }

    private static int Compile(ShaderType type, string src)
    {
        int s = GL.CreateShader(type);
        GL.ShaderSource(s, src);
        GL.CompileShader(s);
        GL.GetShader(s, ShaderParameter.CompileStatus, out int ok);
        if (ok == 0) throw new Exception($"{type} compile error: {GL.GetShaderInfoLog(s)}");
        return s;
    }

    public void BindUniformBlock(string blockName, int bindingPoint)
    {
        int index = GL.GetUniformBlockIndex(_program, blockName);
        if (index >= 0) GL.UniformBlockBinding(_program, index, bindingPoint);
    }


    public void Use() => GL.UseProgram(_program);
    public void Set(string name, int v) => GL.Uniform1(GL.GetUniformLocation(_program, name), v);
    public void Set(string name, bool v) => GL.Uniform1(GL.GetUniformLocation(_program, name), v ? 1 : 0);
    public void Set(string name, float v) => GL.Uniform1(GL.GetUniformLocation(_program, name), v);
    public void Set(string name, Vector3 v) => GL.Uniform3(GL.GetUniformLocation(_program, name), v);
    public void Set(string name, Vector4 v) => GL.Uniform4(GL.GetUniformLocation(_program, name), v); 
    public void Set(string name, Matrix4 m) => GL.UniformMatrix4(GL.GetUniformLocation(_program, name), false, ref m);

 


    public void Dispose()
    {
        GL.DeleteProgram(_program);
    }
}
