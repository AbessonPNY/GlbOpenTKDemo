using Assimp;
using Assimp.Configs;
using Assimp.Unmanaged;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using StbImageSharp;
using System.Diagnostics;
using GLMagFilter = OpenTK.Graphics.OpenGL4.TextureMagFilter;
using GLMinFilter = OpenTK.Graphics.OpenGL4.TextureMinFilter;
using GLTexParam = OpenTK.Graphics.OpenGL4.TextureParameterName;
// Alias pour lever l'ambiguïté avec Assimp
using GLTexWrap = OpenTK.Graphics.OpenGL4.TextureWrapMode;

namespace GlbOpenTKDemo.Rendering
{

    // ===== Types pour le skinning =====
    public class Skeleton
    {
        public Dictionary<string, int> BoneIndex = new();
        public List<string> BoneName = new();
        public List<Matrix4> Offset = new(); // inverse bind pose (bone offset)
    }

    public static class AssimpLoader
    {
        public static Scene LoadGlb(string path)
        {
            var ctx = new AssimpContext();
            ctx.SetConfig(new NormalSmoothingAngleConfig(66.0f));

            var flags =
                PostProcessSteps.Triangulate |
                PostProcessSteps.GenerateNormals |
                PostProcessSteps.FlipUVs |
                PostProcessSteps.JoinIdenticalVertices |
                PostProcessSteps.ImproveCacheLocality |
                PostProcessSteps.OptimizeMeshes |
                PostProcessSteps.OptimizeGraph;

            // Ajoute ceci si disponible dans ta version :
            flags |= PostProcessSteps.SortByPrimitiveType;

            var scene = ctx.ImportFile(path, flags);
            if (scene == null || !scene.HasMeshes)
                throw new Exception("Import GLB failed or scene has no meshes.");

            // --- DEBUG animations ---
            //Debug.WriteLine($"[GLB] Animations: {scene.AnimationCount}");
            if (scene.HasAnimations)
            {
                for (int i = 0; i < scene.AnimationCount; i++)
                {
                    var a = scene.Animations[i];
                    // TicksPerSecond peut être 0 (signifie “par défaut”  souvent 25 ou 30 selon l’export)
                    double tps = (a.TicksPerSecond == 0) ? 25.0 : a.TicksPerSecond;

                    Console.WriteLine(
                        $"  [{i}] \"{(string.IsNullOrEmpty(a.Name) ? "(sans nom)" : a.Name)}\"  " +
                        $"dur={a.DurationInTicks:0.###} ticks  @ {tps:0.###} tps  " +
                        $"nodeChannels={a.NodeAnimationChannelCount}  meshChannels={a.MeshAnimationChannelCount}");

                    // Détail rapide des 3 premiers canaux de nœuds
                    int max = Math.Min(3, a.NodeAnimationChannelCount);
                    for (int c = 0; c < max; c++)
                    {
                        var ch = a.NodeAnimationChannels[c];
                        //Debug.WriteLine(
                          //  $"     - {ch.NodeName}  posKeys={ch.PositionKeyCount}  rotKeys={ch.RotationKeyCount}  scaleKeys={ch.ScalingKeyCount}");
                    }
                }
            }

            return scene;
        }

        // ---- Helpers d'interpolation ----
        private static OpenTK.Mathematics.Vector3 SampleVec(IList<Assimp.VectorKey> keys, double t)
        {
            if (keys == null || keys.Count == 0) return OpenTK.Mathematics.Vector3.Zero;
            if (keys.Count == 1)
                return new((float)keys[0].Value.X, (float)keys[0].Value.Y, (float)keys[0].Value.Z);

            int i1 = 0;
            while (i1 < keys.Count - 1 && t >= keys[i1 + 1].Time) i1++;
            int i2 = Math.Min(i1 + 1, keys.Count - 1);

            double dt = keys[i2].Time - keys[i1].Time;
            float f = dt > 0 ? (float)((t - keys[i1].Time) / dt) : 0f;

            var a = keys[i1].Value;
            var b = keys[i2].Value;
            return new(
                (float)((1 - f) * a.X + f * b.X),
                (float)((1 - f) * a.Y + f * b.Y),
                (float)((1 - f) * a.Z + f * b.Z)
            );
        }

        private static OpenTK.Mathematics.Vector3 SampleVecDefault(IList<Assimp.VectorKey> keys, double t, OpenTK.Mathematics.Vector3 defaultValue)
        {
            if (keys == null || keys.Count == 0)
                return defaultValue;

            if (keys.Count == 1)
                return new((float)keys[0].Value.X, (float)keys[0].Value.Y, (float)keys[0].Value.Z);

            int i1 = 0;
            while (i1 < keys.Count - 1 && t >= keys[i1 + 1].Time) i1++;
            int i2 = Math.Min(i1 + 1, keys.Count - 1);

            double dt = keys[i2].Time - keys[i1].Time;
            float f = dt > 0 ? (float)((t - keys[i1].Time) / dt) : 0f;

            var a = keys[i1].Value;
            var b = keys[i2].Value;
            return new(
                (float)((1 - f) * a.X + f * b.X),
                (float)((1 - f) * a.Y + f * b.Y),
                (float)((1 - f) * a.Z + f * b.Z)
            );
        }

        private static OpenTK.Mathematics.Vector3 SamplePosition(IList<Assimp.VectorKey> keys, double t)
    => SampleVecDefault(keys, t, OpenTK.Mathematics.Vector3.Zero);

        private static OpenTK.Mathematics.Vector3 SampleScale(IList<Assimp.VectorKey> keys, double t)
    => SampleVecDefault(keys, t, new OpenTK.Mathematics.Vector3(1f, 1f, 1f));

        private static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);


        private static Assimp.Quaternion SampleQuat(IList<Assimp.QuaternionKey> keys, double t)
        {
            if (keys == null || keys.Count == 0) return new Assimp.Quaternion(0, 0, 0, 1);
            if (keys.Count == 1) return keys[0].Value;
            int i1 = 0;
            while (i1 < keys.Count - 1 && t >= keys[i1 + 1].Time) i1++;
            int i2 = Math.Min(i1 + 1, keys.Count - 1);
            double dt = keys[i2].Time - keys[i1].Time;
            float f = dt > 0 ? (float)((t - keys[i1].Time) / dt) : 0f;
            return Assimp.Quaternion.Slerp(keys[i1].Value, keys[i2].Value, f);
        }


        private static OpenTK.Mathematics.Vector3 SampleVec(Assimp.VectorKey[] keys, double t)
        {
            if (keys == null || keys.Length == 0) return OpenTK.Mathematics.Vector3.Zero;
            if (keys.Length == 1) return new((float)keys[0].Value.X, (float)keys[0].Value.Y, (float)keys[0].Value.Z);
            int i1 = 0; while (i1 < keys.Length - 1 && t >= keys[i1 + 1].Time) i1++;
            int i2 = Math.Min(i1 + 1, keys.Length - 1);
            double dt = keys[i2].Time - keys[i1].Time;
            float f = dt > 0 ? (float)((t - keys[i1].Time) / dt) : 0f;
            var a = keys[i1].Value; var b = keys[i2].Value;
            return new((float)((1 - f) * a.X + f * b.X),
                       (float)((1 - f) * a.Y + f * b.Y),
                       (float)((1 - f) * a.Z + f * b.Z));
        }



        private static OpenTK.Mathematics.Matrix4 ToMat4(Assimp.Matrix4x4 m)
        {
            return new OpenTK.Mathematics.Matrix4(
                m.A1, m.B1, m.C1, m.D1,
                m.A2, m.B2, m.C2, m.D2,
                m.A3, m.B3, m.C3, m.D3,
                m.A4, m.B4, m.C4, m.D4);
        }

        // ---- Évalue la pose et remplit outBoneMats ----
        public static void ComputePoseMatrices(Assimp.Scene scene, Skeleton skel, double timeSec, int animIndex, OpenTK.Mathematics.Matrix4[] outBoneMats)
        {

            var rootLocal = ToMat4(scene.RootNode.Transform);
            Matrix4.Invert(rootLocal, out var globalInverse); // (Root)^{-1}

            // Init identité
            for (int i = 0; i < outBoneMats.Length; i++) outBoneMats[i] = OpenTK.Mathematics.Matrix4.Identity;

            if (scene == null || skel.BoneName.Count == 0)
                return;

           

            if (animIndex < 0 || animIndex >= scene.AnimationCount)
            {
                // Pose bind: global = node.Transform, final = global * offset (ou offset * global selon rig)
                var globals = new Dictionary<string, Matrix4>();
                void WalkBind(Node n, Matrix4 parent)
                {
                    var local = ToMat4(n.Transform);
                    var g = parent * local;         // parent * local  (IMPORTANT)
                    globals[n.Name] = g;
                    foreach (var c in n.Children) WalkBind(c, g);
                }
                WalkBind(scene.RootNode, OpenTK.Mathematics.Matrix4.Identity);
                for (int i = 0; i < skel.BoneName.Count && i < outBoneMats.Length; i++)
                {
                    string name = skel.BoneName[i];
                    var g = globals.TryGetValue(name, out var M) ? M : Matrix4.Identity;
                    outBoneMats[i] = globalInverse * g * skel.Offset[i];   // << clé
                }
                return;
            }

            var anim = scene.Animations[animIndex];
            // 1) Facteur "ticks par seconde" (glTF: souvent 0 => secondes)
            double tps = anim.TicksPerSecond == 0 ? 1.0 : anim.TicksPerSecond; // glTF: clés en secondes

            // 2) Durée fiable en *ticks* = max des dernières clés de tous les channels
            double durationTicks = 0.0;
            foreach (var ch in anim.NodeAnimationChannels)
            {
                if (ch.PositionKeyCount > 0) durationTicks = Math.Max(durationTicks, ch.PositionKeys[^1].Time);
                if (ch.RotationKeyCount > 0) durationTicks = Math.Max(durationTicks, ch.RotationKeys[^1].Time);
                if (ch.ScalingKeyCount > 0) durationTicks = Math.Max(durationTicks, ch.ScalingKeys[^1].Time);
            }
            // fallback si l’anim n’a pas de clés (rare)
            if (durationTicks <= 0.0) durationTicks = Math.Max(1.0, anim.DurationInTicks);
          


            //patch vitesse 

            // 3) Temps courant en ticks, à partir du temps en secondes
            double tTicks = (timeSec * tps) % durationTicks;

            // (option debug)
#if DEBUG
            //System.Diagnostics.Debug.WriteLine(
              //  $"[AnimDbg] name='{anim.Name}' tps={tps:0.###} durationTicks={durationTicks:0.###}  tTicks={tTicks:0.###}  (~{durationTicks / (tps == 0 ? 1 : tps):0.###}s)");
#endif


            // Map NodeName -> Channel
            var chmap = new Dictionary<string, NodeAnimationChannel>(anim.NodeAnimationChannels.Count);
            foreach (var ch in anim.NodeAnimationChannels) chmap[ch.NodeName] = ch;

            var globalsAnim = new Dictionary<string, Matrix4>();

            void WalkAnim(Node n, Matrix4 parent)
            {
                var local = ToMat4(n.Transform); // valeur par défaut
                if (chmap.TryGetValue(n.Name, out var ch))
                {

                    var T = Matrix4.CreateTranslation(SamplePosition(ch.PositionKeys, tTicks));
                    var q = SampleQuat(ch.RotationKeys, tTicks);
                    var R = Matrix4.CreateFromQuaternion(new OpenTK.Mathematics.Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W));
                    var S = Matrix4.CreateScale(SampleScale(ch.ScalingKeys, tTicks));
                    var baseLocal = ToMat4(n.Transform);
                    local =  (S * R * T);    // TRS*/
                   

                    /*var T = Matrix4.CreateTranslation(SamplePosition(ch.PositionKeys, t));
                    var q = SampleQuat(ch.RotationKeys, t);
                    var R = Matrix4.CreateFromQuaternion(new OpenTK.Mathematics.Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W));
                    var S = Matrix4.CreateScale(SampleScale(ch.ScalingKeys, t));
                    local = S * R * T;    // TRS*/


                }
                var g =  local * parent;            // parent * local
                globalsAnim[n.Name] = g;
                foreach (var c in n.Children) WalkAnim(c, g);
            }
            WalkAnim(scene.RootNode, Matrix4.Identity);

            for (int i = 0; i < skel.BoneName.Count && i < outBoneMats.Length; i++)
            {
                string name = skel.BoneName[i];
                var g = globalsAnim.TryGetValue(name, out var M) ? M : Matrix4.Identity;
                outBoneMats[i] = globalInverse * skel.Offset[i] * g; // << clé
            }

#if DEBUG
            int matched = 0, missing = 0;
            var firstMissing = new System.Text.StringBuilder();
            for (int i = 0; i < skel.BoneName.Count; i++)
            {
                string name = skel.BoneName[i];
                if (globalsAnim.TryGetValue(name, out _)) matched++;
                else
                {
                    missing++;
                    if (firstMissing.Length < 400) firstMissing.AppendLine(name);
                }
            }
            //System.Diagnostics.Debug.WriteLine($"[AnimDbg] bones={skel.BoneName.Count} matched={matched} missing={missing}");
            //if (missing > 0) System.Diagnostics.Debug.WriteLine($"[AnimDbg] first missing:\n{firstMissing}");
#endif


            var rootM = ToMat4(scene.RootNode.Transform);
            var rootScale = new Vector3(rootM.Row0.Xyz.Length, rootM.Row1.Xyz.Length, rootM.Row2.Xyz.Length);
            //Debug.WriteLine($"Root scale: {rootScale}");

        }




        public static Skeleton BuildSkeleton(Scene scene)
        {
            var skel = new Skeleton();

            // Parcourt toutes les meshes pour collecter tous les bones
            foreach (var mesh in scene.Meshes)
            {
                foreach (var b in mesh.Bones)
                {
                    if (!skel.BoneIndex.ContainsKey(b.Name))
                    {
                        int idx = skel.BoneName.Count;
                        skel.BoneIndex[b.Name] = idx;
                        skel.BoneName.Add(b.Name);
                        skel.Offset.Add(ToMat4(b.OffsetMatrix));
                    }
                }
            }
            return skel;
        }


        public static (GLModel model, Skeleton skel) BuildModel(Scene scene, string modelDir)
        {
            var model = new GLModel();
            var skel = BuildSkeleton(scene);

            // Bounds init
            var min = new Vector3(float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity);

            for (int mi = 0; mi < scene.MeshCount; mi++)
            {
                var aMesh = scene.Meshes[mi];

                // --- Vertices init
                var vertices = new Vertex[aMesh.VertexCount];
                for (int v = 0; v < aMesh.VertexCount; v++)
                {
                    var p = aMesh.Vertices[v];
                    var n = aMesh.HasNormals ? aMesh.Normals[v] : new Assimp.Vector3D(0, 0, 1);
                    var uv = aMesh.HasTextureCoords(0) ? aMesh.TextureCoordinateChannels[0][v] : new Assimp.Vector3D();

                    vertices[v].Position = new Vector3(p.X, p.Y, p.Z);
                    vertices[v].Normal = new Vector3(n.X, n.Y, n.Z);
                    vertices[v].Texcoord = new Vector2(uv.X, uv.Y);
                    vertices[v].BoneIds = new Vector4i(0, 0, 0, 0);
                    vertices[v].BoneW = new Vector4(0, 0, 0, 0);

                    // Accumuler bounds
                    min = Vector3.ComponentMin(min, vertices[v].Position);
                    max = Vector3.ComponentMax(max, vertices[v].Position);
                }

                // --- Indices
                var indices = new List<int>(aMesh.FaceCount * 3);
                foreach (var f in aMesh.Faces) if (f.IndexCount == 3) indices.AddRange(f.Indices);

                // --- Poids d’os (max 4 influences par sommet)
                if (aMesh.HasBones && skel.BoneName.Count > 0)
                {
                    foreach (var bone in aMesh.Bones)
                    {
                        if (!skel.BoneIndex.TryGetValue(bone.Name, out int bId))
                            continue;

                        foreach (var vw in bone.VertexWeights)
                        {
                            int vi = vw.VertexID;
                            float w = (float)vw.Weight;

                            // place dans le premier slot libre, sinon remplace le plus petit
                            ref var ids = ref vertices[vi].BoneIds;
                            ref var ws = ref vertices[vi].BoneW;

                            for (int k = 0; k < 4; k++)
                            {
                                if (ws[k] == 0f) { ids[k] = bId; ws[k] = w; goto nextVW; }
                            }
                            int minK = 0;
                            for (int k = 1; k < 4; k++) if (ws[k] < ws[minK]) minK = k;
                            if (w > ws[minK]) { ids[minK] = bId; ws[minK] = w; }
                        nextVW:;
                        }
                    }

                    // normaliser les poids
                    for (int v = 0; v < vertices.Length; v++)
                    {
                        float s = vertices[v].BoneW.X + vertices[v].BoneW.Y + vertices[v].BoneW.Z + vertices[v].BoneW.W;
                        if (s > 1e-6f) vertices[v].BoneW /= s; else vertices[v].BoneW.W = 1f;
                    }
                }
                else
                {
                    // pas de bones : poids = (0,0,0,1) par défaut (fallback shader : skin = identité)
                    for (int v = 0; v < vertices.Length; v++)
                        vertices[v].BoneW.W = 1f;
                }

                // --- Texture diffuse/BaseColor si dispo
                int? tex = LoadDiffuseTextureForMesh(aMesh, scene, modelDir);

                // --- Création GLMesh
                var glMesh = GLMesh.FromArrays(vertices, indices.ToArray(), tex);
                model.Meshes.Add(glMesh);
            }

            model.BoundsMin = min;
            model.BoundsMax = max;

            return (model, skel);
        }

        private static int? LoadDiffuseTextureForMesh(Assimp.Mesh aMesh, Scene scene, string modelDir)
        {
            if (!scene.HasMaterials) return null;
            var mat = scene.Materials[aMesh.MaterialIndex];

            // GLTF2: BaseColor ; fallback: Diffuse
            if (!mat.GetMaterialTexture(TextureType.BaseColor, 0, out var texSlot))
                mat.GetMaterialTexture(TextureType.Diffuse, 0, out texSlot);

            if (texSlot.FilePath == null || texSlot.FilePath.Length == 0)
                return null;

            byte[] bytes = GetTextureBytesFromSlot(texSlot.FilePath, scene, modelDir);
            if (bytes == null || bytes.Length == 0)
                return null;

            return CreateGLTextureFromBytes(bytes);
        }

        private static byte[] GetTextureBytesFromSlot(string filePath, Scene scene, string modelDir)
        {
            // Cas 1 : texture embarquée -> "*0", "*1", ...
            if (filePath[0] == '*')
            {
                if (int.TryParse(filePath.AsSpan(1), out int idx) && idx >= 0 && idx < scene.Textures.Count)
                {
                    var tex = scene.Textures[idx];
                    if (tex.IsCompressed && tex.CompressedData != null)
                        return tex.CompressedData; // PNG/JPG/etc.
                    if (!tex.IsCompressed && tex.NonCompressedData != null)
                    {
                        // Données brutes (RGBA). On pourrait les convertir vers PNG, mais OpenGL accepte déjà des pixels bruts.
                        // Ici on renvoie NULL pour laisser CreateGLTextureFromBytes (qui attend PNG/JPG) s'en charger autrement.
                        // -> Option: créer une autre fonction CreateGLTextureFromRaw(tex) pour ce cas rarissime.
                        return null!;
                    }
                }
                return null!;
            }

            // Cas 2 : data URI (quelques .gltf l'utilisent)
            if (filePath.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                // Format attendu: data:<mime>;base64,<payload>
                int comma = filePath.IndexOf(',');
                if (comma > 0 && filePath.Contains(";base64", StringComparison.OrdinalIgnoreCase))
                {
                    var b64 = filePath[(comma + 1)..];
                    try { return Convert.FromBase64String(b64); } catch { return null!; }
                }
                return null!;
            }

            // Cas 3 : fichier à côté du modèle (.gltf)
            var full = Path.Combine(modelDir, filePath);
            if (File.Exists(full))
                return File.ReadAllBytes(full);

            return null!;
        }


        private static int CreateGLTextureFromBytes(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            var img = ImageResult.FromStream(ms, ColorComponents.RedGreenBlueAlpha);

            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                img.Width, img.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, img.Data);

            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLTexWrap.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLTexWrap.Repeat);

            return tex;
        }
    }

}