using Assimp.Unmanaged;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Runtime.InteropServices;

namespace GlbOpenTKDemo.Rendering;

[StructLayout(LayoutKind.Sequential)]

public struct Vertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 Texcoord;
    public Vector4i BoneIds; // location=3
    public Vector4 BoneW;   // location=4
}


public sealed class GLMesh : IDisposable
{
    public int VAO, VBO, EBO;
    private int _vao, _vbo, _ebo;
    public int IndexCount;
    public int? DiffuseTexture;
    private int _indexCount;
    private int? _texture; // id OpenGL de la texture diffuse (ou null)


    public void Dispose()
    {
        if (EBO != 0) GL.DeleteBuffer(EBO);
        if (VBO != 0) GL.DeleteBuffer(VBO);
        if (VAO != 0) GL.DeleteVertexArray(VAO);
        if (DiffuseTexture.HasValue && DiffuseTexture.Value != 0) GL.DeleteTexture(DiffuseTexture.Value);
    }

    public static GLMesh FromArrays(Vertex[] vertices, int[] indices, int? texture)
    {
        var mesh = new GLMesh();
        mesh.IndexCount = indices.Length;

        mesh.VAO = GL.GenVertexArray(); GL.BindVertexArray(mesh.VAO);
        mesh.VBO = GL.GenBuffer(); GL.BindBuffer(BufferTarget.ArrayBuffer, mesh.VBO);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * Marshal.SizeOf<Vertex>(), vertices, BufferUsageHint.StaticDraw);

        mesh.EBO = GL.GenBuffer(); GL.BindBuffer(BufferTarget.ElementArrayBuffer, mesh.EBO);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(int), indices, BufferUsageHint.StaticDraw);

        int stride = Marshal.SizeOf<Vertex>();

        //Position
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);

        // normal
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 12);

        // texcoord
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 24);

        // bone IDs (entiers)
        GL.EnableVertexAttribArray(3);
        GL.VertexAttribIPointer(3, 4, VertexAttribIntegerType.Int, stride, 32);

        // bone weights
        GL.EnableVertexAttribArray(4);
        GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, stride, 48);

        mesh.DiffuseTexture = texture ?? 0;

        GL.BindVertexArray(0);
        return mesh;
    }



    public void Draw()
    {
        // Active la texture si présente (sur l’unité 0)
        if (_texture.HasValue)
        {
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _texture.Value);
            // Ton shader doit avoir 'uniform sampler2D uTex;' déjà fixé à 0 :
            // _shader.Set("uTex", 0); (à faire une seule fois dans OnLoad)
        }

        GL.BindVertexArray(_vao);
        GL.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, 0);
        GL.BindVertexArray(0);
    }

}

public class GLModel
{

    public List<GLMesh> Meshes { get; } = new();

    // NEW: bounding box in model space
    public OpenTK.Mathematics.Vector3 BoundsMin { get; set; } = new(float.PositiveInfinity);
    public OpenTK.Mathematics.Vector3 BoundsMax { get; set; } = new(float.NegativeInfinity);



    public void Dispose()
    {
        foreach (var m in Meshes) m.Dispose();
        Meshes.Clear();
    }


    public OpenTK.Mathematics.Vector3 Center =>
       (BoundsMin + BoundsMax) * 0.5f;

    public float Radius =>
        (BoundsMax - BoundsMin).Length * 0.5f;

    // Simple cube fallback if no GLB is present
    public static GLModel CreateUnitCube()
    {
        var model = new GLModel();

        // Position, Normal (approx), UV
        var verts = new List<Vertex>
        {
            // Front
            new() { Position = new( -0.5f, -0.5f,  0.5f), Normal=new(0,0,1), Texcoord=new(0,0) },
            new() { Position = new(  0.5f, -0.5f,  0.5f), Normal=new(0,0,1), Texcoord=new(1,0) },
            new() { Position = new(  0.5f,  0.5f,  0.5f), Normal=new(0,0,1), Texcoord=new(1,1) },
            new() { Position = new( -0.5f,  0.5f,  0.5f), Normal=new(0,0,1), Texcoord=new(0,1) },
            // Back
            new() { Position = new( -0.5f, -0.5f, -0.5f), Normal=new(0,0,-1), Texcoord=new(1,0) },
            new() { Position = new(  0.5f, -0.5f, -0.5f), Normal=new(0,0,-1), Texcoord=new(0,0) },
            new() { Position = new(  0.5f,  0.5f, -0.5f), Normal=new(0,0,-1), Texcoord=new(0,1) },
            new() { Position = new( -0.5f,  0.5f, -0.5f), Normal=new(0,0,-1), Texcoord=new(1,1) },
            // Left
            new() { Position = new( -0.5f, -0.5f, -0.5f), Normal=new(-1,0,0), Texcoord=new(0,0) },
            new() { Position = new( -0.5f, -0.5f,  0.5f), Normal=new(-1,0,0), Texcoord=new(1,0) },
            new() { Position = new( -0.5f,  0.5f,  0.5f), Normal=new(-1,0,0), Texcoord=new(1,1) },
            new() { Position = new( -0.5f,  0.5f, -0.5f), Normal=new(-1,0,0), Texcoord=new(0,1) },
            // Right
            new() { Position = new( 0.5f, -0.5f, -0.5f), Normal=new(1,0,0), Texcoord=new(1,0) },
            new() { Position = new( 0.5f, -0.5f,  0.5f), Normal=new(1,0,0), Texcoord=new(0,0) },
            new() { Position = new( 0.5f,  0.5f,  0.5f), Normal=new(1,0,0), Texcoord=new(0,1) },
            new() { Position = new( 0.5f,  0.5f, -0.5f), Normal=new(1,0,0), Texcoord=new(1,1) },
            // Top
            new() { Position = new( -0.5f,  0.5f,  0.5f), Normal=new(0,1,0), Texcoord=new(0,1) },
            new() { Position = new(  0.5f,  0.5f,  0.5f), Normal=new(0,1,0), Texcoord=new(1,1) },
            new() { Position = new(  0.5f,  0.5f, -0.5f), Normal=new(0,1,0), Texcoord=new(1,0) },
            new() { Position = new( -0.5f,  0.5f, -0.5f), Normal=new(0,1,0), Texcoord=new(0,0) },
            // Bottom
            new() { Position = new( -0.5f, -0.5f,  0.5f), Normal=new(0,-1,0), Texcoord=new(0,0) },
            new() { Position = new(  0.5f, -0.5f,  0.5f), Normal=new(0,-1,0), Texcoord=new(1,0) },
            new() { Position = new(  0.5f, -0.5f, -0.5f), Normal=new(0,-1,0), Texcoord=new(1,1) },
            new() { Position = new( -0.5f, -0.5f, -0.5f), Normal=new(0,-1,0), Texcoord=new(0,1) },
        };

        var idx = new int[]
        {
            0,1,2, 0,2,3,       // Front
            4,6,5, 4,7,6,       // Back
            8,9,10, 8,10,11,    // Left
            12,14,13, 12,15,14, // Right
            16,17,18, 16,18,19, // Top
            20,22,21, 20,23,22  // Bottom
        };

        var mesh = GLMesh.FromArrays(verts.ToArray(), idx, null);
        model.Meshes.Add(mesh);
        return model;
    }
}
