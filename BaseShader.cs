using OpenTK.Graphics.OpenGL4;
using System;
using System.IO;

namespace GlbOpenTKDemo
{
    public static class BaseShader
    {
        public static int CompileFromFiles(string vertPath, string fragPath)
        {
            string v = File.ReadAllText(vertPath);
            string f = File.ReadAllText(fragPath);
            return Compile(v, f);
        }

        public static int Compile(string vertexSource, string fragmentSource)
        {
            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, vertexSource);
            GL.CompileShader(vs);
            GL.GetShader(vs, ShaderParameter.CompileStatus, out int vstatus);
            if (vstatus == 0) throw new Exception("Vertex compile error: " + GL.GetShaderInfoLog(vs));

            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, fragmentSource);
            GL.CompileShader(fs);
            GL.GetShader(fs, ShaderParameter.CompileStatus, out int fstatus);
            if (fstatus == 0) throw new Exception("Fragment compile error: " + GL.GetShaderInfoLog(fs));

            int program = GL.CreateProgram();
            GL.AttachShader(program, vs);
            GL.AttachShader(program, fs);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linkstatus);
            if (linkstatus == 0) throw new Exception("Program link error: " + GL.GetProgramInfoLog(program));

            GL.DetachShader(program, vs);
            GL.DetachShader(program, fs);
            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
            return program;
        }
    }
}
