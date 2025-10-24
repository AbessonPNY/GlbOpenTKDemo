using Assimp;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using PrimitiveType = OpenTK.Graphics.OpenGL4.PrimitiveType;

namespace GlbOpenTKDemo.Rendering
{
    public sealed class AssimpGlbManager : IDisposable
    {
        // ==== Constantes ====
        public const int MaxBones = 100;
        private const int BytesPerMat4 = 16 * sizeof(float);

        // ==== Ressources / données du modèle ====
        public Scene? _scene;
        public GLModel _model = new GLModel();
        public Skeleton _skeleton = new Skeleton();

        // shader personnage (avec Bones UBO, uniforms uModel/uView/uProj etc.)
        private readonly CharShader _shader;

        // UBO pour les matrices d'os
        public int _bonesUbo = 0;

        // palette finale envoyée au GPU (100 max)
        private readonly Matrix4[] _bonePalette = new Matrix4[MaxBones];

        // ==== Transform personnage ====
        public Vector3 Position = Vector3.Zero;      // position monde
        public Vector3 EulerDeg = Vector3.Zero;      // rotation locale en degrés (X,Y,Z)
        private float _modelScale = 1.98f;              // échelle uniforme
        private Matrix4 _modelMat = Matrix4.Identity;

        // pivot figé (point local autour duquel on tourne)
        // typiquement les pieds du perso
        private Vector3 _pivotLocalFixed = Vector3.Zero;

        // ==== Animation ====
        public int _animIndex = -1;
        public bool _animPlaying = false;
        public double _animTimeSec = 0.0;
        public double _animDurationSec = 0.0;
        public float _animSpeed = 20.0f; // tu peux ajuster ça

        // ===== CONSTRUCTEUR PRIVÉ =====
        private AssimpGlbManager(CharShader skinningShader)
        {
            _shader = skinningShader ?? throw new ArgumentNullException(nameof(skinningShader));
            EnsureBonesUbo();
        }

        // ===== CHARGEMENT STATIC =====
        public static AssimpGlbManager Load(string glbPath, CharShader skinningShader)
        {
            var ch = new AssimpGlbManager(skinningShader);

            // 1) Charger la scène Assimp
            ch._scene = AssimpLoader.LoadGlb(glbPath);

            // 2) Construire le GLModel (meshes, VAO/VBO/EBO, textures...)
            (ch._model, ch._skeleton) = AssimpLoader.BuildModel(ch._scene, System.IO.Path.GetDirectoryName(glbPath)!);

            // 3) Déterminer un pivot local stable (= pas recalculé à chaque frame)
            //    Ici on prend "les pieds" : XZ milieu, Y = min.Y
            var min = ch._model.BoundsMin;
            var max = ch._model.BoundsMax;

            ch._pivotLocalFixed = new Vector3(
                (min.X + max.X) * 0.5f,   // centre en X
                min.Y,                    // point le plus bas en Y (les pieds)
                (min.Z + max.Z) * 0.5f    // centre en Z
            );

            // 4) Poser le perso au sol (sol = Y=0)
            //    On veut que les pieds soient à Y=0 monde.
            //    Actuellement, les pieds sont _pivotLocalFixed.Y en local.
            //    Donc on translate le perso de -pivotY en Y monde.
            ch.Position = new Vector3(
                0f,
                -ch._pivotLocalFixed.Y,
                0f
            );

            // 5) Orientation initiale corrigée pour glTF -> OpenGL si besoin
            //    (ex: beaucoup de glTF ont besoin d'un -90° autour X)
            //    Ajuste ça selon ton asset:
            // ch.EulerDeg = new Vector3(-90f, 0f, 0f);
            ch.EulerDeg = new Vector3(0f, 0f, 0f);

                    // 7) Animation par défaut
            ch.InitDefaultAnimation();

            Debug.WriteLine(
                $"[AssimpGlbManager] Loaded. Meshes={ch._model.Meshes.Count}, Bones={ch._skeleton.BoneName.Count}, " +
                $"BoundsMin={min}, BoundsMax={max}, PivotFixed={ch._pivotLocalFixed}");

            return ch;
        }

        // ====== init UBO bones ======
        private void EnsureBonesUbo()
        {
            _bonesUbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.UniformBuffer, _bonesUbo);
            GL.BufferData(BufferTarget.UniformBuffer, MaxBones * BytesPerMat4, IntPtr.Zero, BufferUsageHint.DynamicDraw);

            // Bind sur le point 0 au niveau GL
            GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 0, _bonesUbo);

            // Et dire au shader que son block "Bones" est binding 0
            _shader.BindUniformBlock("Bones", 0);

            // init identité
            for (int i = 0; i < MaxBones; i++) _bonePalette[i] = Matrix4.Identity;
            unsafe
            {
                fixed (Matrix4* ptr = &_bonePalette[0])
                {
                    GL.BufferSubData(BufferTarget.UniformBuffer, IntPtr.Zero,
                        MaxBones * BytesPerMat4, (IntPtr)ptr);
                }
            }
        }

        // ====== PUBLIC: Scale ======
        public void SetScale(float s)
        {
            _modelScale = s;
        }

        public float GetScale() => _modelScale;

        // ====== PUBLIC: Pivot debug ======
        public Vector3 GetPivot() => _pivotLocalFixed;

        // ====== PUBLIC: Anim control ======
        public void PlayPauseAnimation()
        {
            _animPlaying = !_animPlaying;
        }

        public void SetAnimationSpeed(float animSpeed)
        {
            _animSpeed = animSpeed;
        }

        public float GetAnimationSpeed() => _animSpeed;

        public void ResetAnimationTime()
        {
            _animTimeSec = 0;
        }

        public void SetAnimationIndex(int index, bool resetTime = true)
        {
            if (_scene == null || _scene.AnimationCount == 0) return;
            index = Math.Clamp(index, 0, _scene.AnimationCount - 1);

            _animIndex = index;
            if (resetTime) _animTimeSec = 0.0;

            UpdateCurrentAnimationDuration();
        }

        private void InitDefaultAnimation()
        {
            if (_scene == null) return;

            // essaie de trouver une anim nommée "idle" etc
            int idx = FindAnimationIndex(_scene, new[] { "idle", "Idle", "IDLE", "idle_01", "Idle_01" });
            if (idx < 0 && _scene.AnimationCount > 0) idx = 0;

            _animIndex = idx;
            _animPlaying = true;
            _animTimeSec = 0.0;
            UpdateCurrentAnimationDuration();
        }

        private int FindAnimationIndex(Scene scene, string[] hints)
        {
            if (scene == null || scene.AnimationCount == 0) return -1;
            for (int i = 0; i < scene.AnimationCount; i++)
            {
                var n = scene.Animations[i].Name ?? "";
                foreach (var h in hints)
                    if (n.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0)
                        return i;
            }
            return -1;
        }

        public void UpdateCurrentAnimationDuration()
        {
            if (_scene == null || _animIndex < 0)
            {
                _animDurationSec = 0;
                return;
            }

            var a = _scene.Animations[_animIndex];

            double tps = a.TicksPerSecond == 0 ? 1.0 : a.TicksPerSecond;
            double durationTicks = 0;

            foreach (var ch in a.NodeAnimationChannels)
            {
                if (ch.PositionKeyCount > 0) durationTicks = Math.Max(durationTicks, ch.PositionKeys[^1].Time);
                if (ch.RotationKeyCount > 0) durationTicks = Math.Max(durationTicks, ch.RotationKeys[^1].Time);
                if (ch.ScalingKeyCount > 0) durationTicks = Math.Max(durationTicks, ch.ScalingKeys[^1].Time);
            }

            if (durationTicks <= 0.0)
                durationTicks = Math.Max(1.0, a.DurationInTicks);

            // durée en secondes
            _animDurationSec = (tps == 0 ? 1.0 : 1.0 / tps) * durationTicks;
        }

        // ====== UPDATE PAR FRAME ======
        public void Update(double dtSeconds)
        {
            // avance le temps d'anim
            if (_animPlaying && _animIndex >= 0)
                _animTimeSec += dtSeconds * _animSpeed;

            // recalcule la palette de bones
            if (_scene is not null && _skeleton.BoneName.Count > 0)
            {
                AssimpLoader.ComputePoseMatrices(_scene, _skeleton, _animTimeSec, _animIndex, _bonePalette);
            }

            // upload vers UBO
            GL.BindBuffer(BufferTarget.UniformBuffer, _bonesUbo);

            int countBone = Math.Max(1, Math.Min(_skeleton.BoneName.Count, MaxBones));
            unsafe
            {
                fixed (Matrix4* ptr = &_bonePalette[0])
                {
                    GL.BufferSubData(
                        BufferTarget.UniformBuffer,
                        IntPtr.Zero,
                        countBone * BytesPerMat4,
                        (IntPtr)ptr
                    );
                }
            }
        }

        // ====== MATRICE MONDE ======
        public Matrix4 RebuildModelMatrix()
        {
            // rotations locales
            var rx = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(EulerDeg.X));
            var ry = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(EulerDeg.Y));
            var rz = Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(EulerDeg.Z));
            var rot = rz * ry * rx;

            // scale uniforme
            var scl = Matrix4.CreateScale(_modelScale);

            // le pivot local (pieds) qu’on veut comme “centre”
            // on veut déplacer le mesh pour que ce pivot tombe à (0,0,0) AVANT rot/scale
            var toPivot = Matrix4.CreateTranslation(-_pivotLocalFixed);

            // Maintenant on applique : rot * scl * toPivot
            // => ça met le personnage (déjà recentré autour de son pivot) dans son espace tourné et échelonné.
            var local = rot * scl ;

            // Ensuite SEULEMENT on le place dans le monde
            var world = Matrix4.CreateTranslation(Position);

            _modelMat = local * world  ;
            return _modelMat;
        }


        // ====== RENDER ======
        // view = caméra.View
        // proj = caméra.Projection
        // camPos = caméra.Position (pour spéculaire)
        public void Render(Matrix4 view, Matrix4 proj, Vector3 camPos)
        {
            if (_model.Meshes.Count == 0) return;

            // compose la matrice modèle à jour
            _modelMat = RebuildModelMatrix();

            // MVP (order = P * V * M dans ton shader actuel)
            Matrix4 mvp = proj * view * _modelMat;

            // activer le block Bones UBO sur binding 0
            GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 0, _bonesUbo);

            // activer shader
            _shader.Use();

            // envoyer les uniforms de base
            _shader.Set("uView", view);
            _shader.Set("uProj", proj);
            _shader.Set("uMVP", mvp);
            _shader.Set("uModel", _modelMat);

            _shader.Set("uLightPos", new Vector3(3, 5, 2)); // ajuste si tu as des lights monde
            _shader.Set("uCamPos", camPos);

            _shader.Set("uUseSkin", 1);
            _shader.Set("uSpecStrength", 0.1f);
            _shader.Set("uSpecPower", 16f);

            // dessin de chaque mesh
            foreach (var m in _model.Meshes)
            {
                GL.BindVertexArray(m.VAO);

                if (m.DiffuseTexture is int tex && tex != 0)
                {
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, tex);
                    _shader.Set("uTex", 0);
                    _shader.Set("uHasTex", true);
                }
                else
                {
                    _shader.Set("uHasTex", false);
                    _shader.Set("uKdColor", new Vector3(0.7f, 0.7f, 0.7f));
                }

                GL.DrawElements(PrimitiveType.Triangles, m.IndexCount, DrawElementsType.UnsignedInt, 0);
            }

            GL.BindVertexArray(0);
        }

        // ====== DEBUG ======
        public int GetBonesCount()
        {
            return Math.Min(_skeleton.BoneName.Count, MaxBones);
        }

        public Vector3 GetWorldPosApprox()
        {
            // on peut approximer la position monde comme la translation extraite
            var mm = _modelMat;
            return mm.ExtractTranslation();
        }

        // ====== CLEANUP ======
        public void Dispose()
        {
            if (_bonesUbo != 0)
            {
                GL.DeleteBuffer(_bonesUbo);
                _bonesUbo = 0;
            }
        }
    }
}
