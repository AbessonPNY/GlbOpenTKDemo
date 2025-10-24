
using Assimp;
using OpenTK.Compute.OpenCL;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using static System.Formats.Asn1.AsnWriter;
using PrimitiveType = OpenTK.Graphics.OpenGL4.PrimitiveType;

namespace GlbOpenTKDemo.Rendering{

	public sealed class AssimpGlbManager : IDisposable
	{

		// ---- Asset
		public Scene? _scene;
		public GLModel _model = new GLModel();
		public Skeleton _skeleton = new Skeleton();
        private float _modelScale = 1f; // 1 = taille d’origine
        private Matrix4 _modelMat = Matrix4.Identity;
        // ---- GL / ressources
        private readonly CharShader _shader;          // shader principal (vertex avec block 'Bones')
        public int _bonesUbo = 0;

        // ---- Consts
        public const int MaxBones = 100;
        private const int BytesPerMat4 = 16 * sizeof(float);

        // ---- Transform (placement dans le monde)
        public Vector3 Position = Vector3.Zero;
        public Vector3 EulerDeg = Vector3.Zero;     // rotation XYZ en degrés
        private Vector3 _pivotLocalFixed = Vector3.Zero;

        private float _radius = 0 ;

        // ---- Palettes d'os
        private readonly Matrix4[] _palette = new Matrix4[MaxBones];    // finale
        private readonly Matrix4[] _palA = new Matrix4[MaxBones];       // source (crossfade)
        private readonly Matrix4[] _palB = new Matrix4[MaxBones];       // cible  (crossfade)
                                                                        //Perso GLB animé
        public enum PivotMode { None, Center, Bottom, Custom }
        public PivotMode Pivot { get; set; } = PivotMode.Bottom; // pieds par défaut
        public Vector3 CustomPivot { get; set; } = Vector3.Zero; // si Custom


        private AnimationState _anim = new();
   
   
        public Matrix4[] _bonePalette = new Matrix4[MaxBones];
        public Vector3 _modelEulerDeg = new(0f, 0f, 0f); // X,Y,Z en degrés
                                                          //private float _angle;


        // Animation courante
        public int _animIndex           = -1;
        public bool _animPlaying        = false;
        public double _animTimeSec      = 0.0;
        public double _animDurationSec  = 0.0;
        public float _animSpeed         = 20.0f;



        private AssimpGlbManager(CharShader skinningShader)
		{
			_shader = skinningShader ?? throw new ArgumentNullException(nameof(skinningShader));
			EnsureBonesUbo();
		}

		
		public static AssimpGlbManager Load(string glbPath, CharShader skinningShader)
		{
			var ch = new AssimpGlbManager(skinningShader);

			// 1) Charger la scène
			ch._scene = AssimpLoader.LoadGlb(glbPath);
			(ch._model, ch._skeleton) = AssimpLoader.BuildModel(ch._scene, Path.GetDirectoryName(glbPath)!);

            var(min, max) = (ch._model.BoundsMin, ch._model.BoundsMax);

            // pivot bas du corps (centre en XZ, min en Y)
            ch._pivotLocalFixed = new Vector3(
                (min.X + max.X) * 0.5f,
                min.Y,
                (min.Z + max.Z) * 0.5f
            );

            // Option : recaler le perso au sol sans offset manuel :
            // On veut que les pieds soient visuellement à Y=0 => on descend Position.Y
            ch.Position = new Vector3(0f, -ch._pivotLocalFixed.Y, 0f);

            return ch;
        }


        private void EnsureBonesUbo()
        {
            // Crée UBO
            _bonesUbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.UniformBuffer, _bonesUbo);
            GL.BufferData(BufferTarget.UniformBuffer, MaxBones * BytesPerMat4, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            //GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 0, _bonesUbo);


            // Lier le block "Bones" du shader au binding point 0
            _shader.BindUniformBlock("Bones", 0);
        }


        public void UpdateCurrentAnimationDuration()
        {
            if (_scene == null || _animIndex < 0) { _animDurationSec = 0; return; }
            var a = _scene.Animations[_animIndex];
            // durée robuste (max des dernières clés)
            double tps = a.TicksPerSecond == 0 ? 1.0 : a.TicksPerSecond;
            double durationTicks = 0;
            foreach (var ch in a.NodeAnimationChannels)
            {
                if (ch.PositionKeyCount > 0) durationTicks = Math.Max(durationTicks, ch.PositionKeys[^1].Time);
                if (ch.RotationKeyCount > 0) durationTicks = Math.Max(durationTicks, ch.RotationKeys[^1].Time);
                if (ch.ScalingKeyCount > 0) durationTicks = Math.Max(durationTicks, ch.ScalingKeys[^1].Time);
            }
            if (durationTicks <= 0.0) durationTicks = Math.Max(1.0, a.DurationInTicks);
            _animDurationSec = (tps == 0 ? 1.0 : 1.0 / tps) * durationTicks;
        }

        /// <summary>À appeler chaque frame (avance le temps et calcule la palette des bones, puis upload UBO).</summary>

        public void Update(double dtSeconds)
        {

            // Avance du temps
            if (_animPlaying && _animIndex >= 0)
                _animTimeSec += dtSeconds * _animSpeed;

            // === Bones palette ===
            if (_scene is not null && _skeleton.BoneName.Count > 0)
            {
                AssimpLoader.ComputePoseMatrices(_scene, _skeleton, _animTimeSec, _animIndex, _bonePalette);
            }

            // Upload UBO
            GL.BindBuffer(BufferTarget.UniformBuffer, _bonesUbo);
         
            unsafe
            {
                int countBone = Math.Max(1, Math.Min(_skeleton.BoneName.Count, MaxBones));
                fixed (OpenTK.Mathematics.Matrix4* ptr = &_bonePalette[0])
                {
                    GL.BufferSubData(BufferTarget.UniformBuffer, IntPtr.Zero, countBone * (16 * sizeof(float)), (IntPtr)ptr);
                }
            }
        }

        public void PlayPauseAnimation(int animIndex = 3)
        {
            _animIndex = animIndex;
            _animPlaying = !_animPlaying;
        }

        public void SetAnimationSpeed(float animSpeed)
        {
            _animSpeed = animSpeed;    
        }

        public float GetAnimationSpeed()
        {
            return _animSpeed;
        }


        public void ResetAnimation()
        {
            _animTimeSec = 0;
        }

        public void SetAnimationByIndex(int index, bool resetTime = true)
        {
            if (_scene == null || _scene.AnimationCount == 0) return;
            index = Math.Clamp(index, 0, _scene.AnimationCount - 1);          

           
            _animIndex = index;
            if (resetTime) _animTimeSec = 0.0;         
            
        }


        public void CreateBinding()
        {

            // === UBO Bones : création + binding au point 0 ===

            //GL.BindBuffer(BufferTarget.UniformBuffer, _bonesUbo);

            // 100 * sizeof(mat4) = 100 * 64 bytes
            int boneBytes = MaxBones * (16 * sizeof(float));
            GL.BufferData(BufferTarget.UniformBuffer, boneBytes, IntPtr.Zero, BufferUsageHint.DynamicDraw);

            // Lier ce buffer au binding point 0
            GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 0, _bonesUbo);

            RebuildModelMatrix();
          




            for (int i = 0; i < MaxBones; i++) _bonePalette[i] = Matrix4.Identity;

            // Upload d’identités (premier remplissage)
            GL.BindBuffer(BufferTarget.UniformBuffer, _bonesUbo);
            unsafe
            {
                fixed (Matrix4* ptr = &_bonePalette[0])
                {
                    GL.BufferSubData(BufferTarget.UniformBuffer, IntPtr.Zero, boneBytes, (IntPtr)ptr);
                }
            }

            if (_scene != null)
            {
                // Essaie "Idle", sinon prends la 1ère
                _animIndex = FindAnimationIndex(_scene!, new[] { "idle", "idle_01", "idle0" });
                if (_animIndex < 0 && _scene?.AnimationCount > 0) _animIndex = 0;
                UpdateCurrentAnimationDuration();
            }

        }



        /*public Matrix4 BuildModelMatrix(GLModel _model)
		{

            int boneBytes = MaxBones * (16 * sizeof(float));
            GL.BufferData(BufferTarget.UniformBuffer, boneBytes, IntPtr.Zero, BufferUsageHint.DynamicDraw);

            // Lier ce buffer au binding point 0
            GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 0, _bonesUbo);


            // Centre et rayon du modèle
            var center = _model.Center;
            _radius = MathF.Max(_model.Radius, 1e-3f);

            // ordre conseillé : Rotation * Scale * Translation
            var rot = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(-90f)); // ton 90° droite (change l’axe si besoin)
            var scale = Matrix4.CreateScale(Scale);
            var toPivot = Matrix4.CreateTranslation(-GetLocalPivot());

            _modelMat = Matrix4.CreateTranslation(Position) * rot * scale ;

            return _modelMat;
        }*/


        public float GetRadius()
        {
            return _radius;
        }

        public float GetScaledRadius()
        {
            // rayon “brut” du modèle (déjà calculé par tes bounds)
            float r = _model.Radius;

            // si tu utilises un scale uniforme :
            float s = _modelScale;

            // si tu utilises un scale non uniforme, remplace par:
            // float s = MathF.Max(_modelScale3.X, MathF.Max(_modelScale3.Y, _modelScale3.Z));

            return MathF.Max(1e-5f, r * s);
        }



        /* Mets à jour la variable modelMat, c'est à dire la matrice de projection du personnage*/
        public Matrix4 RebuildModelMatrix()
        {

            /* var rx = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(EulerDeg.X));
             var ry = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(EulerDeg.Y));
             var rz = Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(EulerDeg.Z));

              var rotFix = rz * ry * rx; // ordre Z*Y*X, simple et suffisant ici
              var scale = Matrix4.CreateScale(_modelScale); // ou _modelScale3 si non uniforme

              // Tourner "sur lui-même" autour de son centre → T_world * rotFix * rotSpin * S * T(-center)
              _modelMat = Matrix4.CreateTranslation(Position)
                    * rotFix
                    * scale
                    * Matrix4.CreateTranslation(-_model.Center);*/

            
            var rx = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(EulerDeg.X));
            var ry = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(EulerDeg.Y));
            var rz = Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(EulerDeg.Z));
            var rot = rz * ry * rx;
            var scl = Matrix4.CreateScale(_modelScale);

            // translation monde
            var world = Matrix4.CreateTranslation(Position);

         

            // recentrage du mesh autour du pivot choisi (pieds)
            var toPivot = Matrix4.CreateTranslation(-_pivotLocalFixed);

            _modelMat = world * rot * scl ;

            return _modelMat;
        }

        public int FindAnimationIndex(Assimp.Scene scene, string[] hints)
        {
            if (scene == null || scene.AnimationCount == 0) return -1;
            for (int i = 0; i < scene.AnimationCount; i++)
            {
                var n = scene.Animations[i].Name ?? "";
                foreach (var h in hints)
                    if (n.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0) return i;
            }
            return -1;
        }


        /// <summary>Rend le personnage avec le shader fourni au constructeur (uModel/uView/uProj + Bones UBO binding).</summary>
        public void Render(Matrix4 view, Matrix4 proj, Vector3 camPos) // pas besoin de camPos
        {
            if (_model.Meshes.Count == 0) return;

            _modelMat = RebuildModelMatrix();

            GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 0, _bonesUbo);

            _shader.Use();
            _shader.Set("uView", view);
            _shader.Set("uProj", proj);
            _shader.Set("uModel", _modelMat);

            _shader.Set("uLightPos", new Vector3(3, 5, 2));
            _shader.Set("uCamPos", camPos);

            

            // === Upload palette de bones vers le UBO ===
            GL.BindBuffer(BufferTarget.UniformBuffer, _bonesUbo);

            //On recupere le nombre de Bones
            var count = GetBonesCount();

            unsafe
            {
                if (count > 0)
                {

                    fixed (OpenTK.Mathematics.Matrix4* ptr = &_bonePalette[0])
                    {                       
                        int updateBytes = Math.Max(1, count) * (16 * sizeof(float));
                        GL.BufferSubData(BufferTarget.UniformBuffer, IntPtr.Zero, updateBytes, (IntPtr)ptr);
                    }
                }
                else
                {
                    // Si pas de bones : identité (facultatif si ton shader a déjà un fallback)
                    var id = OpenTK.Mathematics.Matrix4.Identity;
                    GL.BufferSubData(BufferTarget.UniformBuffer, IntPtr.Zero, 16 * sizeof(float), ref id);
                }
            }


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
                }
                GL.DrawElements(PrimitiveType.Triangles, m.IndexCount, DrawElementsType.UnsignedInt, 0);
            }

            GL.Disable(EnableCap.DepthTest);            

            GL.Enable(EnableCap.DepthTest);
            GL.BindVertexArray(0);

        }

        public int GetBonesCount()
        {
            int count = Math.Min(_skeleton.BoneName.Count, MaxBones);         

            return count;
        }

        private Vector3 GetLocalPivot()
        {
            var min = _model.BoundsMin; // calculés dans BuildModel()
            var max = _model.BoundsMax;

            switch (Pivot)
            {
                case PivotMode.Center:
                    return (min + max) * 0.5f;
                case PivotMode.Bottom:
                    return new Vector3((min.X + max.X) * 0.5f, min.Y, (min.Z + max.Z) * 0.5f);
                case PivotMode.Custom:
                    return CustomPivot;
                case PivotMode.None:
                default:
                    return Vector3.Zero; // n’applique aucun -pivot
            }
        }



        public void Dispose()
		{
            //if (_bonesUbo != 0) { GL.DeleteBuffer(_bonesUbo); _bonesUbo = 0; }
            // Le Shader n’est pas disposé ici (géré par l’appelant)
            // _model / GLMesh ont leur propre Dispose si nécessaire
            if (_bonesUbo != 0) GL.DeleteBuffer(_bonesUbo);
        }



    }
}