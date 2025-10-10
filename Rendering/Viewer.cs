using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

using System.Diagnostics;

namespace GlbOpenTKDemo.Rendering;

public class Viewer : GameWindow
{
    private Shader _shader = null!;
    private GLModel _model = new();
    private Matrix4 _view;
    private Matrix4 _proj;

    //Perso GLB animé
    private int _bonesUbo = 0;
    private Skeleton _skeleton = new();
    private AnimationState _anim = new();
    private const int MaxBones = 100; // doit correspondre au shader
    private Assimp.Scene? _scene;
    private Matrix4[] _bonePalette = new Matrix4[MaxBones];
    private Vector3 _modelEulerDeg = new(0f, 0f, 0f); // X,Y,Z en degrés
    //private float _angle;
    private float _modelScale = 56f; // 1 = taille d’origine
    private Matrix4 _modelMat = Matrix4.Identity;
    // Animation courante
    private int _animIndex = -1;
    private bool _animPlaying = false;
    private double _animTimeSec = 0.0;
    private double _animDurationSec = 0.0;
    private float _animSpeed = 20.0f;
    private GlbCharacter _char1 = null!;

    //Camera
    private float _yaw = 0.0f;     // rotation horizontale (degrés)
    private float _pitch = 15.0f;  // rotation verticale (degrés)
    private float _camDistance = 6.0f; // distance au centre
    private Vector3 _camPos = new(0, 1.5f, 6f);
    private Vector3 _modelOffset = Vector3.Zero;   // <-- pas de calcul avec _camPos ici
    private float _near = 0.01f, _far = 500f; // si tu veux t’en servir
    private Vector3 _target = Vector3.Zero; // ce que la caméra regarde (0,0,0 chez toi)


    private int _planeVao;
    private int _planeVbo;
    private Shader? _planeShader;    

    private Vector3 _panOffset = Vector3.Zero; // décalage (pan) world-space

    private float _fitMargin = 1.15f; // 1.0 = serré, 1.15 = un peu d'air
    private float _fovYDeg = 60f;     // ton FOV vertical actuel

    
  
    public Viewer(GameWindowSettings gws, NativeWindowSettings nws) : base(gws, nws) {}
    

    protected override void OnLoad()
    {
        base.OnLoad();

        _panOffset.Y = 77f;

        // Z-buffer
        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Lequal);
        GL.ClearDepth(1.0);

        // Désactive le face culling (pour tester)
        GL.Disable(EnableCap.CullFace);
        // (Si tu veux le garder plus tard :)
        // GL.Enable(EnableCap.CullFace);
        //GL.CullFace(CullFaceMode.Back);
         GL.FrontFace(FrontFaceDirection.Ccw); // la plupart des assets GLTF sont CCW


        GL.ClearColor(0.1f, 0.1f, 0.12f, 1f);

        UpdateCamera();          // positionne _camPos / _view
        ApplySafetyNearPlane();  // peut dépendre de _camPos


        RebuildModelMatrix();    // compose la matrice finale
        UpdateProjectionForCurrentView();


        // OnLoad() personnage venant de ma classe GlbCharacter
        _shader = new Shader("assets/shaders/vertex.glsl", "assets/shaders/fragment.glsl");
        _char1 = GlbCharacter.Load("assets/models/model.glb", _shader);
        _char1.Scale = new Vector3(10f);            // le scale que tu veux
        _char1.EulerDeg = new Vector3(-90f, 0, 0);  // corrige l’orientation si besoin
        _char1.Position = Vector3.Zero;






        // Shaders
        _shader = new Shader("assets/shaders/vertex.glsl", "assets/shaders/fragment.glsl");
        
        // === UBO Bones : création + binding au point 0 ===
        _bonesUbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.UniformBuffer, _bonesUbo);

        // 100 * sizeof(mat4) = 100 * 64 bytes
        int boneBytes = MaxBones * (16 * sizeof(float));
        GL.BufferData(BufferTarget.UniformBuffer, boneBytes, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        // Lier ce buffer au binding point 0
        GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 0, _bonesUbo);

        // Relier le block "Bones" du shader au binding point 0
        _shader.BindUniformBlock("Bones", 0);

        // (Optionnel) Initialiser à identité
        unsafe
        {
            var identities = new OpenTK.Mathematics.Matrix4[MaxBones];
            for (int i = 0; i < MaxBones; i++) identities[i] = OpenTK.Mathematics.Matrix4.Identity;
            fixed (OpenTK.Mathematics.Matrix4* ptr = &identities[0])
                GL.BufferSubData(BufferTarget.UniformBuffer, IntPtr.Zero, boneBytes, (IntPtr)ptr);
        }

        // Camera
        _view = Matrix4.LookAt(_camPos, Vector3.Zero, Vector3.UnitY);
        // near assez petit, far assez grand
        _proj = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(60f),
            Size.X / (float)Size.Y,
            0.01f,   // near
            500f     // far (ou plus si tu t’éloignes beaucoup)
        );


        string modelPath = "assets/models/model.glb";
        if (File.Exists(modelPath))
        {
            try
            {
                _scene = AssimpLoader.LoadGlb(modelPath);
                (_model, _skeleton) = AssimpLoader.BuildModel(_scene, Path.GetDirectoryName(modelPath)!);
                Console.WriteLine($"Loaded model with {_model.Meshes.Count} mesh(es). Bones: {_skeleton.BoneName.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to load GLB: " + ex.Message);
                Console.WriteLine("Falling back to a simple cube.");
                _model = GLModel.CreateUnitCube();
                _scene = null;
                _skeleton = new Skeleton();
            }
        }
        else
        {
            Console.WriteLine("No GLB found at assets/models/model.glb. Showing a simple cube.");
            _model = GLModel.CreateUnitCube();
            _scene = null;
            _skeleton = new Skeleton();
        }

       


        // Centre et rayon du modèle
        var center = _model.Center;
        var radius = MathF.Max(_model.Radius, 1e-3f);
    
        // ordre conseillé : Rotation * Scale * Translation
        var rot = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(-90f)); // ton 90° droite (change l’axe si besoin)
        var scale = Matrix4.CreateScale(_modelScale);
        var toOrigin = Matrix4.CreateTranslation(-center);

        _modelMat = rot * scale * toOrigin;

      


        // Recalc projection (déjà fait) et positionne la caméra pour voir l’objet entier.
        // On se met devant l’objet, un peu au-dessus, à une distance proportionnelle
        float fov = MathHelper.DegreesToRadians(60f);
        float fit = 1.2f; // marge pour respirer
        float halfH = radius; // approx pour couvrir la plus grande dimension
        float dist = (halfH * fit) / MathF.Tan(fov * 0.5f);

        // Caméra regardant le centre
        var eye = center + new Vector3(0, radius * 0.5f, dist * 2.0f);
        _camPos = eye;
        //_view = Matrix4.LookAt(_camPos, center, Vector3.UnitY);

        UpdateCamera();

        // === PLANS DE CLIPPING ===
        _planeShader = new Shader("assets/shaders/plane.glsl", "assets/shaders/plane_frag.glsl");

        // Rectangle en XY (2x2)
                float[] planeVerts = {
            -1f, -1f, 0f,
             1f, -1f, 0f,
             1f,  1f, 0f,
            -1f,  1f, 0f
        };
        uint[] indices = { 0, 1, 2, 0, 2, 3 };

        _planeVao = GL.GenVertexArray();
        _planeVbo = GL.GenBuffer();
        int ebo = GL.GenBuffer();

        GL.BindVertexArray(_planeVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _planeVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, planeVerts.Length * sizeof(float), planeVerts, BufferUsageHint.StaticDraw);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.BindVertexArray(0);


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



        UpdateWindowTitle();

        
    }

    void UpdateProjectionForCurrentView()
    {
        float fovY = MathHelper.DegreesToRadians(60f);
        float aspect = Size.X / (float)Size.Y;

        // distance scène
        float radius = MathF.Max(1e-3f, GetScaledRadius());     // ton rayon modèle (après scale)
        float dist = MathF.Max(1e-3f, _camDistance);

        // near ni trop petit ni trop grand, lié à la scène
        _near = Math.Clamp(dist * 0.05f, 0.03f, 0.5f);          // ~5% de la distance
        _far = Math.Max(_near + radius * 10f, dist + radius * 4f);

        _proj = Matrix4.CreatePerspectiveFieldOfView(fovY, aspect, _near, _far);
    }

    private int FindAnimationIndex(Assimp.Scene scene, string[] hints)
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

    private void UpdateCurrentAnimationDuration()
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


    private void ApplySafetyNearPlane()
    {
        float safety = 0.05f; // marge minimale
        float distToCam = (_camPos - _modelOffset).Length;
        if (distToCam < safety)
            _modelOffset = _camPos - Vector3.Normalize(_camPos) * safety;
    }


    private float GetScaledRadius()
    {
        // rayon “brut” du modèle (déjà calculé par tes bounds)
        float r = _model.Radius;

        // si tu utilises un scale uniforme :
        float s = _modelScale;

        // si tu utilises un scale non uniforme, remplace par:
        // float s = MathF.Max(_modelScale3.X, MathF.Max(_modelScale3.Y, _modelScale3.Z));

        return MathF.Max(1e-5f, r * s);
    }

    private void FitCameraToModel()
    {
        // 1) Rayon effectif du modèle (après scale)
        float radius = GetScaledRadius();

        // 2) On s’assure de couvrir verticalement ET horizontalement
        float fovY = MathHelper.DegreesToRadians(_fovYDeg);
        float aspect = Size.X / (float)Size.Y;
        float fovX = 2f * MathF.Atan(MathF.Tan(fovY * 0.5f) * aspect);

        // distance nécessaire pour couvrir le diamètre (2R) : d = (R * fit) / tan(FOV/2)
        float dVert = (radius * _fitMargin) / MathF.Tan(fovY * 0.5f);
        float dHorz = (radius * _fitMargin) / MathF.Tan(fovX * 0.5f);
        float dist = MathF.Max(dVert, dHorz);

        // 3) Place la caméra à cette distance, dans ta logique orbitale (yaw/pitch conservés)
        float yawRad = MathHelper.DegreesToRadians(_yaw);
        float pitchRad = MathHelper.DegreesToRadians(_pitch);

        Vector3 dir;
        dir.X = MathF.Cos(pitchRad) * MathF.Sin(yawRad);
        dir.Y = MathF.Sin(pitchRad);
        dir.Z = MathF.Cos(pitchRad) * MathF.Cos(yawRad);
        dir = Vector3.Normalize(dir);

        _camDistance = dist;
        _camPos = dir * _camDistance;

        // 4) Recalcule la vue et, tant qu’on y est, ajuste near/far proprement
        float near = MathF.Max(0.01f, dist * 0.02f); // near ≈ 2% de la distance
        float far = MathF.Max(near + 10f, dist + radius * 8f);

        _near = near; _far = far;
        _view = Matrix4.LookAt(_camPos, Vector3.Zero, Vector3.UnitY);
        _proj = Matrix4.CreatePerspectiveFieldOfView(fovY, aspect, _near, _far);
    }


    private void UpdateWindowTitle()
    {
        // Si tu suis mon montage: centre du modèle en world = _modelOffset.
        // (Sinon, alternative: var modelPos = new Vector3(_modelMat.M41, _modelMat.M42, _modelMat.M43);)
        var modelPos = _modelOffset;
        var camPos = _camPos;

        float dist = (camPos - modelPos).Length;

        //Title = $"CAM:NUM8/NUM2 | < Model > Cam: {V3(camPos)}  |  Model: {V3(modelPos)}  |  Dist: {dist:0.###}  |  Near: {_near:0.###}";
        //var play = _idlePlaying ? "▶" : "⏸";
        //var t = _idleTimeSec;
        //Title = $"Idle {play}  t={t:0.00}s / {_idleDurationSec:0.00}s  speed×{_idleSpeed:0.##}";
    }

    private void PlaceModelAtCameraPosition(float extraForward = 0f)
    {
        // direction "caméra -> cible"
        var forward = Vector3.Normalize(_target - _camPos);

        // 0 = exactement sur la caméra (risque: rien ne se voit); sinon avance un peu
        _modelOffset = _camPos + forward * extraForward;

        RebuildModelMatrix(); // reconstruit : T_world * R * S * T(-center)
    }

    private void PlaceModelOnNearPlane(float epsilon = 0.001f)
    {
        var forward = Vector3.Normalize(_target - _camPos);
        float d = MathF.Max(_near + epsilon, _near); // au niveau du plan rouge, mais juste devant
        _modelOffset = _camPos + forward * d;
        RebuildModelMatrix();
    }

    private void RebuildModelMatrix()
    {

        var rx = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(_modelEulerDeg.X));
        var ry = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(_modelEulerDeg.Y));
        var rz = Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(_modelEulerDeg.Z));

        var rotFix = rz * ry * rx; // ordre Z*Y*X, simple et suffisant ici
        var scale = Matrix4.CreateScale(_modelScale); // ou _modelScale3 si non uniforme

        // Tourner "sur lui-même" autour de son centre → T_world * rotFix * rotSpin * S * T(-center)
        _modelMat = Matrix4.CreateTranslation(_modelOffset)
              * rotFix
              * scale
              * Matrix4.CreateTranslation(-_model.Center);


        //Debug.WriteLine($"[Model] scale = {_modelScale:0.###}");
       
    }



    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

        // 1) Lecture des entrées (clavier/souris)
        var kb = KeyboardState;
        if (kb.IsKeyPressed(Keys.Escape)) Close();



        // Play / Pause
        if (_animIndex >= 0 && kb.IsKeyPressed(Keys.Space))
            _animPlaying = !_animPlaying;

        // Vitesse (J/K)
        if (kb.IsKeyDown(Keys.J)) _animSpeed = MathF.Max(0.1f, _animSpeed - 0.5f * (float)args.Time);
        if (kb.IsKeyDown(Keys.K)) _animSpeed = MathF.Min(4.0f, _animSpeed + 0.5f * (float)args.Time);



        // Remise à zéro (R)
        if (kb.IsKeyPressed(Keys.R)) _animTimeSec = 0;

        // Sélection: ← / → pour précédente / suivante
        if (_scene?.AnimationCount > 0)
        {
            if (kb.IsKeyPressed(Keys.V))
            {
                _animIndex = (_animIndex - 1 + _scene.AnimationCount) % _scene.AnimationCount;
                _animTimeSec = 0; UpdateCurrentAnimationDuration();
            }
            if (kb.IsKeyPressed(Keys.B))
            {
                _animIndex = (_animIndex + 1) % _scene.AnimationCount;
                _animTimeSec = 0; UpdateCurrentAnimationDuration();
            }
        }

        // (Option) sélection directe par chiffres 1..9
        if (_scene?.AnimationCount > 0)
        {
            for (int i = 0; i < Math.Min(9, _scene.AnimationCount); i++)
            {
                var key = Keys.D1 + i; // D1..D9
                if (kb.IsKeyPressed(key))
                {
                    _animIndex = i; _animTimeSec = 0; UpdateCurrentAnimationDuration();
                }
            }
        }

        // Vitesse de base en fonction de la taille de la scène
        float sceneScale = MathF.Max(0.5f, _model.Radius); // >= 0.5 pour éviter 0   
        float baseUnitsPerSec = sceneScale * 2.5f; // ajuste si tu veux plus/moins rapide



        // vitesse en degrés/s (ajuste à ton goût)
        float spinSpeed = 90f * (float)args.Time;

        float speed = 50f * (float)args.Time; // vitesse rotation degrés/s
        //float zoomSpeed = 1.2f; // facteur zoom

        //  Rotation orbitale avec les flèches
        if (kb.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Left))
            _modelEulerDeg.Y += 0.1f;
        if (kb.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Right))
            _modelEulerDeg.Y -= 0.1f;
        if (kb.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Up))
            _pitch += speed;
        if (kb.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Down))
            _pitch -= speed;


        if (kb.IsKeyPressed(OpenTK.Windowing.GraphicsLibraryFramework.Keys.F))
            FitCameraToModel();

        if (kb.IsKeyPressed(Keys.C)) // C = Camera position
            PlaceModelAtCameraPosition(0f);

        if (kb.IsKeyPressed(Keys.N)) // N = Near plane
            PlaceModelOnNearPlane(0.001f);


        // Option: avancer/reculer le perso à partir du near plane
        if (kb.IsKeyDown(Keys.Insert))   // éloigner un peu du near
            PlaceModelAtCameraPosition(_near + 0.05f);
        if (kb.IsKeyDown(Keys.Delete))   // rapprocher (attention au clipping)
            PlaceModelAtCameraPosition(_near + 0.005f);



        // Limite l’inclinaison (pitch)
        _pitch = Math.Clamp(_pitch, -89f, 89f);


        float modifier =
      (kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift)) ? 5f :
      (kb.IsKeyDown(Keys.LeftControl) || kb.IsKeyDown(Keys.RightControl)) ? 0.2f : 1f;



        float lift = baseUnitsPerSec * modifier * (float)args.Time;
        // Monte/descend la caméra (pan vertical) — pavé numérique
        if (kb.IsKeyDown(Keys.KeyPad8))
        {
            _panOffset.Y += lift;
        }
        if (kb.IsKeyDown(Keys.KeyPad2))
            _panOffset.Y -= lift;

        // X axis
        if (kb.IsKeyPressed(Keys.KeyPad7)) { _modelEulerDeg.X += 90f; RebuildModelMatrix(); }
        if (kb.IsKeyPressed(Keys.KeyPad1)) { _modelEulerDeg.X -= 90f; RebuildModelMatrix(); }

        // Y axis
        if (kb.IsKeyPressed(Keys.KeyPad9)) { _modelEulerDeg.Y += 90f; RebuildModelMatrix(); }
        if (kb.IsKeyPressed(Keys.KeyPad3)) { _modelEulerDeg.Y -= 90f; RebuildModelMatrix(); }



        if (kb.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Equal)) // +
            _modelScale *= 1.01f;
        if (kb.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Minus)) // -
            _modelScale /= 1.01f;


        float moveSpeed = baseUnitsPerSec * modifier * (float)args.Time;

        // 2) Mises à jour caméra/orbite/zoom/pan
        UpdateCamera();
        UpdateProjectionForCurrentView(); // ton recalcul near/far dynamique

        // 3) Mise à jour des animations/personnages (dt en secondes)
        _char1.Update(args.Time);
        // _char2.Update(args.Time); // si tu en as plusieurs

        // 4) (option) MAJ du titre
        UpdateWindowTitle();
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, Size.X, Size.Y);
        _proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60f), Size.X / (float)Size.Y, 0.1f, 100f);
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _char1.Update(args.Time);

        var kb = KeyboardState;


        // Avance du temps
        if (_animPlaying && _animIndex >= 0)
            _animTimeSec += args.Time * _animSpeed;



       

       


        // Direction "vers la caméra"
        var camForward = Vector3.Normalize(Vector3.Zero - _camPos); // regard -> origine
        var towardCamera = -camForward;


        // Option: empêche de rentrer dans le near plane
        float safety = 0.02f; // marge min
        float distToCam = (_camPos - _modelOffset).Length;

        if (distToCam < safety)
            _modelOffset = _camPos - Vector3.Normalize(_camPos) * safety;


        _camDistance = Math.Clamp(_camDistance, 1.0f, 50.0f);

        UpdateCamera();
        ApplySafetyNearPlane();  // clamp si besoin
        RebuildModelMatrix();    // recompose le Model avec le nouvel offset
        UpdateProjectionForCurrentView();

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


        _shader.Use();
        _shader.Set("uView", _view);
        _shader.Set("uProj", _proj);
        _shader.Set("uModel", _modelMat);
        _shader.Set("uLightPos", new Vector3(3, 5, 2));
        _shader.Set("uCamPos", _camPos);

        // === Upload palette de bones vers le UBO ===
        GL.BindBuffer(BufferTarget.UniformBuffer, _bonesUbo);

        int count = Math.Min(_skeleton.BoneName.Count, MaxBones);
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


        // === VISUALISATION CLIPPING ===
        _planeShader!.Use();
        _planeShader.Set("uView", _view);
        _planeShader.Set("uProj", _proj);

        // Calcule les positions des plans near/far dans l'espace caméra
        float near = 0.01f; // même valeur que ta projection
        float far = 1000f;

        // Définis leurs distances
        Vector3 nearPos = new(0, 0, -near);
        Vector3 farPos = new(0, 0, -far);

        GL.Disable(EnableCap.DepthTest);
        GL.BindVertexArray(_planeVao);

        // near plane : rouge semi-transparent
        Matrix4 nearMat = Matrix4.CreateTranslation(nearPos);
        _planeShader.Set("uView", _view * nearMat);
        _planeShader.Set("uColor", new Vector4(1f, 0f, 0f, 0.3f));
        GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);

        // far plane : bleu semi-transparent
        Matrix4 farMat = Matrix4.CreateTranslation(farPos);
        _planeShader.Set("uView", _view * farMat);
        _planeShader.Set("uColor", new Vector4(0f, 0f, 1f, 0.3f));
        GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);

        GL.Enable(EnableCap.DepthTest);
        GL.BindVertexArray(0);


        _char1.Render(_view, _proj);

        UpdateWindowTitle();

        SwapBuffers();
    }

    private void UpdateCamera()
    {
        // direction orbitale depuis yaw/pitch
        float yawRad = MathHelper.DegreesToRadians(_yaw);
        float pitchRad = MathHelper.DegreesToRadians(_pitch);



        Vector3 dir;
        dir.X = MathF.Cos(pitchRad) * MathF.Sin(yawRad);
        dir.Y = MathF.Sin(pitchRad);
        dir.Z = MathF.Cos(pitchRad) * MathF.Cos(yawRad);
        dir = Vector3.Normalize(dir);

        // applique le pan offset au target ET à la position caméra
        var pivot = _target + _panOffset;
        _camPos = pivot + dir * _camDistance;

        _view = Matrix4.LookAt(_camPos, pivot, Vector3.UnitY);
    }



    protected override void OnUnload()
    {
        base.OnUnload();
        foreach (var m in _model.Meshes) m.Dispose();
        if (_bonesUbo != 0) GL.DeleteBuffer(_bonesUbo);
        _shader?.Dispose();
    }
}
