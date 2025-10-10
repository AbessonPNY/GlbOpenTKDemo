using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using Assimp;

namespace GlbOpenTKDemo.Rendering
{
    /// <summary>
    /// Personnage GLB autonome : charge, anime (y compris crossfade), et rend avec un shader compatible skinning (UBO "Bones").
    /// </summary>
    public sealed class GlbCharacter : IDisposable
    {
        // ---- Consts
        public const int MaxBones = 100;
        private const int BytesPerMat4 = 16 * sizeof(float);

        // ---- GL / ressources
        private readonly Shader _shader;          // shader principal (vertex avec block 'Bones')
        private int _bonesUbo = 0;

        // ---- Asset
        private Scene? _scene;
        private GLModel _model = new GLModel();
        private Skeleton _skeleton = new Skeleton();

        // ---- Animation (état courant + crossfade)
        private int _animIndex = -1;
        private double _animTimeSec = 0.0;
        private float _animSpeed = 1.0f;

        private bool _isCrossfading = false;
        private int _nextAnimIndex = -1;
        private double _nextTimeSec = 0.0;
        private float _crossDur = 0.35f;
        private float _crossT = 0.0f;

        // ---- Palettes d'os
        private readonly Matrix4[] _palette = new Matrix4[MaxBones];    // finale
        private readonly Matrix4[] _palA = new Matrix4[MaxBones];       // source (crossfade)
        private readonly Matrix4[] _palB = new Matrix4[MaxBones];       // cible  (crossfade)

        // ---- Transform (placement dans le monde)
        public Vector3 Position = Vector3.Zero;
        public Vector3 EulerDeg = Vector3.Zero;     // rotation XYZ en degrés
        public Vector3 Scale = Vector3.One;         // scale par axe

        // ---- Infos
        public Vector3 BoundsMin => _model.BoundsMin;
        public Vector3 BoundsMax => _model.BoundsMax;
        public Vector3 Center => _model.Center;
        public float Radius => _model.Radius;

        // ---- ctor
        private GlbCharacter(Shader skinningShader)
        {
            _shader = skinningShader ?? throw new ArgumentNullException(nameof(skinningShader));
            EnsureBonesUbo();
        }

        /// <summary>
        /// Charge un personnage GLB et le prépare pour le rendu (UBO + binding du uniform block "Bones").
        /// </summary>
        public static GlbCharacter Load(string glbPath, Shader skinningShader)
        {
            var ch = new GlbCharacter(skinningShader);

            // 1) Charger la scène
            ch._scene = AssimpLoader.LoadGlb(glbPath);

            // 2) Construire modèle + squelette
            (ch._model, ch._skeleton) = AssimpLoader.BuildModel(ch._scene, System.IO.Path.GetDirectoryName(glbPath)!);

            // 3) Initialiser UBO avec identités
            for (int i = 0; i < MaxBones; i++) ch._palette[i] = Matrix4.Identity;
            GL.BindBuffer(BufferTarget.UniformBuffer, ch._bonesUbo);
            unsafe
            {
                fixed (Matrix4* ptr = &ch._palette[0])
                    GL.BufferSubData(BufferTarget.UniformBuffer, IntPtr.Zero, MaxBones * BytesPerMat4, (IntPtr)ptr);
            }

            // 4) Choisir anim par défaut (Idle si possible)
            ch._animIndex = ch.FindAnimationIndex(new[] { "idle", "idle_01", "idle0" });
            if (ch._animIndex < 0 && ch._scene.AnimationCount > 0)
                ch._animIndex = 0;

            return ch;
        }

        // ---- Public API

        public void SetAnimationByIndex(int index, bool resetTime = true, bool crossfade = true, float fadeDuration = 0.35f)
        {
            if (_scene == null || _scene.AnimationCount == 0) return;
            index = Math.Clamp(index, 0, _scene.AnimationCount - 1);
            if (index == _animIndex && !_isCrossfading) return;

            if (crossfade)
                StartCrossfade(index, fadeDuration);
            else
            {
                _animIndex = index;
                if (resetTime) _animTimeSec = 0.0;
                _isCrossfading = false;
            }
        }

        public bool SetAnimationByName(string name, bool resetTime = true, bool crossfade = true, float fadeDuration = 0.35f)
        {
            if (_scene == null) return false;
            int idx = FindAnimationIndex(new[] { name });
            if (idx < 0) return false;
            SetAnimationByIndex(idx, resetTime, crossfade, fadeDuration);
            return true;
        }

        public void Play() => _animSpeed = Math.Max(_animSpeed, 0.0001f);
        public void Pause() => _animSpeed = 0f;
        public void SetSpeed(float speed) => _animSpeed = Math.Clamp(speed, 0f, 8f);

        public int AnimationCount => _scene?.AnimationCount ?? 0;
        public string AnimationName(int index)
        {
            if (_scene == null || index < 0 || index >= _scene.AnimationCount) return "";
            return _scene.Animations[index].Name ?? $"Anim{index}";
        }

        /// <summary>À appeler chaque frame (avance le temps et calcule la palette des bones, puis upload UBO).</summary>
        public void Update(double dtSeconds)
        {
            if (_scene == null || _skeleton.BoneName.Count == 0) return;

            // Avancer horloges
            if (_animIndex >= 0 && _animSpeed > 0f)
                _animTimeSec += dtSeconds * _animSpeed;

            if (_isCrossfading)
            {
                if (_animSpeed > 0f) _nextTimeSec += dtSeconds * _animSpeed;
                _crossT += (float)dtSeconds;
                if (_crossT >= _crossDur)
                {
                    _animIndex = _nextAnimIndex;
                    _animTimeSec = _nextTimeSec;
                    _isCrossfading = false;
                    _crossT = 0f;
                }
            }

            // Calcul palette
            if (_isCrossfading)
            {
                AssimpLoader.ComputePoseMatrices(_scene, _skeleton, _animTimeSec, _animIndex, _palA);
                AssimpLoader.ComputePoseMatrices(_scene, _skeleton, _nextTimeSec, _nextAnimIndex, _palB);

                float alpha = Math.Clamp(_crossT / _crossDur, 0f, 1f);
                int count = Math.Min(_skeleton.BoneName.Count, MaxBones);
                for (int i = 0; i < count; i++)
                    _palette[i] = TRSBlend(_palA[i], _palB[i], alpha);
            }
            else
            {
                AssimpLoader.ComputePoseMatrices(_scene, _skeleton, _animTimeSec, _animIndex, _palette);
            }

            // Upload UBO
            GL.BindBuffer(BufferTarget.UniformBuffer, _bonesUbo);
            unsafe
            {
                int countBytes = Math.Max(1, Math.Min(_skeleton.BoneName.Count, MaxBones)) * BytesPerMat4;
                fixed (Matrix4* ptr = &_palette[0])
                    GL.BufferSubData(BufferTarget.UniformBuffer, IntPtr.Zero, countBytes, (IntPtr)ptr);
            }
        }

        /// <summary>Rend le personnage avec le shader fourni au constructeur (uModel/uView/uProj + Bones UBO binding).</summary>
        public void Render(Matrix4 view, Matrix4 proj)
        {
            var model = BuildModelMatrix();

            _shader.Use();
            _shader.Set("uView", view);
            _shader.Set("uProj", proj);
            _shader.Set("uModel", model);

            // (Le UBO 'Bones' est déjà bindé à binding point 0 et le block est lié au shader)
            // Dessin des meshes
            foreach (var m in _model.Meshes)
                m.Draw();
        }

        public void Dispose()
        {
            if (_bonesUbo != 0) { GL.DeleteBuffer(_bonesUbo); _bonesUbo = 0; }
            // Le Shader n’est pas disposé ici (géré par l’appelant)
            // _model / GLMesh ont leur propre Dispose si nécessaire
        }

        // ---- Internals

        private void EnsureBonesUbo()
        {
            // Crée UBO
            _bonesUbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.UniformBuffer, _bonesUbo);
            GL.BufferData(BufferTarget.UniformBuffer, MaxBones * BytesPerMat4, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 0, _bonesUbo);

            // Lier le block "Bones" du shader au binding point 0
            _shader.BindUniformBlock("Bones", 0);
        }

        private Matrix4 BuildModelMatrix()
        {
            var rx = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(EulerDeg.X));
            var ry = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(EulerDeg.Y));
            var rz = Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(EulerDeg.Z));
            var rot = rz * ry * rx;
            var scl = Matrix4.CreateScale(Scale);
            var toCenter = Matrix4.CreateTranslation(-_model.Center);
            var world = Matrix4.CreateTranslation(Position);

            // T_world * R * S * T(-center)
            return world * rot * scl * toCenter;
        }

        private void StartCrossfade(int toIndex, float durationSec)
        {
            if (_scene == null || toIndex < 0 || toIndex >= _scene.AnimationCount) return;
            if (toIndex == _animIndex) return;

            _nextAnimIndex = toIndex;
            _nextTimeSec = 0.0;
            _crossDur = Math.Max(0.05f, durationSec);
            _crossT = 0f;
            _isCrossfading = true;
        }

        private int FindAnimationIndex(string[] hints)
        {
            if (_scene == null || _scene.AnimationCount == 0) return -1;
            for (int i = 0; i < _scene.AnimationCount; i++)
            {
                var n = _scene.Animations[i].Name ?? "";
                foreach (var h in hints)
                    if (n.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0) return i;
            }
            return -1;
        }

        // ---- TRS blend util

        private static Matrix4 TRSBlend(in Matrix4 A, in Matrix4 B, float t)
        {
            DecomposeTRS(A, out var Ta, out var Ra, out var Sa);
            DecomposeTRS(B, out var Tb, out var Rb, out var Sb);
            var T = Vector3.Lerp(Ta, Tb, t);
            var R = SlerpShortest(Ra, Rb, t);
            var S = Vector3.Lerp(Sa, Sb, t);
            return ComposeTRS(T, R, S);
        }

        // GlbCharacter.cs
        private static void DecomposeTRS(in Matrix4 m, out Vector3 T, out OpenTK.Mathematics.Quaternion R, out Vector3 S)
        {
            // assignations par défaut (garanties)
            T = new Vector3(m.M41, m.M42, m.M43);
            R = OpenTK.Mathematics.Quaternion.Identity;
            S = Vector3.One;

            // colonnes (axes)
            var c0 = new Vector3(m.M11, m.M12, m.M13);
            var c1 = new Vector3(m.M21, m.M22, m.M23);
            var c2 = new Vector3(m.M31, m.M32, m.M33);

            float sx = c0.Length;
            float sy = c1.Length;
            float sz = c2.Length;

            const float eps = 1e-8f;
            if (sx < eps || sy < eps || sz < eps)
            {
                // matrice dégénérée : on garde R=identity, S>0 pour éviter /0
                S = new Vector3(MathF.Max(sx, eps), MathF.Max(sy, eps), MathF.Max(sz, eps));
                return;
            }

            S = new Vector3(sx, sy, sz);

            // enlever le scale
            var r0 = c0 / sx;
            var r1 = c1 / sy;
            var r2 = c2 / sz;

            // corriger une éventuelle réflexion (det<0)
            float det =
                r0.X * (r1.Y * r2.Z - r1.Z * r2.Y) -
                r0.Y * (r1.X * r2.Z - r1.Z * r2.X) +
                r0.Z * (r1.X * r2.Y - r1.Y * r2.X);
            if (det < 0f)
            {
                // on retourne l’axe Z et le signe de S.Z
                r2 = -r2;
                S.Z = -S.Z;
            }

            var rot3 = new Matrix3(
                r0.X, r0.Y, r0.Z,
                r1.X, r1.Y, r1.Z,
                r2.X, r2.Y, r2.Z
            );

            R = OpenTK.Mathematics.Quaternion.FromMatrix(rot3);
            R.Normalize();

            // garde-fou si NaN
            if (float.IsNaN(R.X) || float.IsNaN(R.Y) || float.IsNaN(R.Z) || float.IsNaN(R.W))
                R = OpenTK.Mathematics.Quaternion.Identity;
        }


        private static Matrix4 ComposeTRS(in Vector3 T, in OpenTK.Mathematics.Quaternion R, in Vector3 S)
        {
            return Matrix4.CreateScale(S) * Matrix4.CreateFromQuaternion(R) * Matrix4.CreateTranslation(T);
        }

       

        private static OpenTK.Mathematics.Quaternion SlerpShortest(OpenTK.Mathematics.Quaternion a, OpenTK.Mathematics.Quaternion b, float t)
        {
            // Normalise (au cas où)
            a = NormalizeSafe(a);
            b = NormalizeSafe(b);

            // produit scalaire (dot)
            float dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

            // Assure le chemin court : si dot<0, inverse tous les composants de b
            if (dot < 0f)
                b = new OpenTK.Mathematics.Quaternion(-b.X, -b.Y, -b.Z, -b.W);

            // Slerp standard
            return OpenTK.Mathematics.Quaternion.Slerp(a, b, t);
        }

        private static OpenTK.Mathematics.Quaternion NormalizeSafe(OpenTK.Mathematics.Quaternion q)
        {
            // OpenTK a q.LengthSquared et q.Normalize()
            if (q.LengthSquared > 0f)
            {
                q.Normalize();
                return q;
            }
            return OpenTK.Mathematics.Quaternion.Identity;
        }

    }
}
