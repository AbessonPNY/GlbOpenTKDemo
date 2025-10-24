using GlbOpenTKDemo.Rendering;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Diagnostics;
using GLPrimitiveType = OpenTK.Graphics.OpenGL4.PrimitiveType;
using GLQuaternion = OpenTK.Mathematics.Quaternion;
using PrimitiveType = OpenTK.Graphics.OpenGL4.PrimitiveType;

namespace GlbOpenTKDemo.Rendering;

public class Viewer : GameWindow
{
    private CharShader _shader = null!;
    private CharShader _shader2 = null!;

    int _bgVao, _bgVbo, _bgEbo, _bgShader, _bgTexture;
    bool _visibleBackground = true;

    private Matrix4 _view;
    private Matrix4 _proj;

    CameraFixed _camera = new();

    // Grille
    private int _gridVao, _gridVbo, _gridCount;
    private CharShader _gridShader;
    private bool _showGrid = true;   // toggle avec 'G'

    private float gridY = 0;

    private float _gridYOffset = -1.0f;     // décalage vertical (Y) manuel de la grille
    private bool _gridFollowChar = false;  // si true, la grille suit la hauteur du perso
    private float _gridStepY = 0.1f;       // pas de réglage fin

    // ==== Grille paramétrable ====

    // vitesse rotation en degrés/seconde
    float _char2TurnSpeedDeg = 120f;

    // vitesse de déplacement en unités/seconde
    float _char2MoveSpeed = 2.1f;


    // mètres si 1 unité = 1 m
    private float _gridHalf = 500f;  // demi-taille => grille 1000m x 1000m
    private float _gridStep = 1f;    // écart des lignes en mètres (1 m)

    //Personnage venant de ma classe
    private AssimpGlbManager _char2 = null!;
    private AssimpGlbManager _char3 = null!;

    //Camera
    private float _yaw = 0.0f;     // rotation horizontale (degrés)
    private float _pitch = 15.0f;  // rotation verticale (degrés)
    private float _camDistance = 6.0f; // distance au centre
    private Vector3 _camPos = new(0, 1.5f, 6f);
    private Vector3 _modelOffset = Vector3.Zero;   // <-- pas de calcul avec _camPos ici
    private float _near = 0.01f, _far = 500f; // si tu veux t’en servir
    private Vector3 _target = Vector3.Zero; // ce que la caméra regarde (0,0,0 chez toi)

    // Grille
 

    private Vector2 _gridOffsetXZ = Vector2.Zero; // offset manuel en X/Z
    private Vector3 _panOffset = Vector3.Zero; // décalage (pan) world-space

    // --- Cube mètre étalon ---

    // Cube mètre
    int _mCubeVao = 0, _mCubeVbo = 0, _mCubeEbo = 0, _mCubeIndexCount = 0;
    bool _showMeterCube = true;
    Vector3 _meterCubePos = new Vector3(23.056711f, -0.712176f, -40.241173f); //(23.056711, -0.712176, -40.241173)
    private CharShader _meshShader;


    public Viewer(GameWindowSettings gws, NativeWindowSettings nws) : base(gws, nws) { }


    protected override void OnLoad()
    {
        base.OnLoad();


        //background Settings
        float[] vertsbg = {
                // pos    // uv
                -1f, -1f, 0f, 0f,
                 1f, -1f, 1f, 0f,
                 1f,  1f, 1f, 1f,
                -1f,  1f, 0f, 1f
            };
        uint[] idx = { 0, 1, 2, 2, 3, 0 };

        _bgVao = GL.GenVertexArray();
        _bgVbo = GL.GenBuffer();
        _bgEbo = GL.GenBuffer();

        GL.BindVertexArray(_bgVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _bgVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertsbg.Length * sizeof(float), vertsbg, BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _bgEbo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Length * sizeof(uint), idx, BufferUsageHint.StaticDraw);

        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));

        _bgShader = BaseShader.CompileFromFiles("assets/shaders/background.vert", "assets/shaders/background.frag");
        _bgTexture = TextureLoader.LoadTexture2D("assets/685fcb846f6c9e52f82d02ac0c9841ba.jpg", out int w, out int h);



        //Camera settings  
        _camera.Near = 0.5f;      // pas trop petit
        _camera.Far = 20000f;    // assez grand pour tes coordonnées
        _camera.Aspect = Size.X / (float)Size.Y;
        _camera.FovY = MathHelper.DegreesToRadians(35f);
        _camera.Position = new Vector3(0f, 1.6f, 4f); // fixed camera
        _camera.Target = new Vector3(0f, 1.0f, 0f);


        var pos = new Vector3(25.486748f, 1.578219f, -26.507668f); 
        var fwd = new Vector3(-0.475215f, -0.071145f, -0.876988f);
        var up = new Vector3(-0.033896f, 0.997466f, -0.062552f);
        _camera.FromBlenderOpenGL(pos, fwd, up, 0.927295218f, 1.777778f, 0.1f, 100f);

        // Position et vecteurs de la caméra (issus de l'export Blender opengl.*)
        var camPos = new Vector3(25.486748f, 1.578219f, -26.507668f);
        var forward = new Vector3(-0.475215f, -0.071145f, -0.876988f);
                                                                      
        //
        // PATCH MANUEL de rotation de la camera pour alignement grille 
        //-----------------------------------

        _camera.Target.X = 24.90f;
        _camera.Target.Y = 1.51f;
        _camera.Target.Z = -27.31f;

        //-----------------------------------
        // FIN PATCH 
        //

        _panOffset.Y = 77f;

        // Z-buffer
        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Lequal);
        GL.ClearDepth(1.0);

        // Désactive le face culling (pour tester)
        GL.Disable(EnableCap.CullFace);

        GL.FrontFace(FrontFaceDirection.Ccw); // la plupart des assets GLTF sont CCW


        GL.ClearColor(0.1f, 0.1f, 0.12f, 1f);

        UpdateCamera();          // positionne _camPos / _view
        ApplySafetyNearPlane();  // peut dépendre de _camPos


        // === shader grille ===
        _gridShader = new CharShader("assets/shaders/grid.vert", "assets/shaders/grid.frag");

        // === géométrie de la grille ===
        // paramètres
        float half = 50f;      // demi-taille de la grille en unités
        float step = 1f;       // taille d’une case
        float y = 0f;       // hauteur de la grille (sol)

       
        RebuildGridGeometry(_gridHalf, _gridStep, 0f);


        // crée des segments de lignes parallèles X et Z
        var verts = new List<float>();
        int lines = (int)MathF.Floor(half / step);
        for (int i = -lines; i <= lines; i++)
        {
            float t = i * step;

            // lignes parallèles à X (varie en Z)
            verts.Add(-half); verts.Add(y); verts.Add(t);
            verts.Add(+half); verts.Add(y); verts.Add(t);

            // lignes parallèles à Z (varie en X)
            verts.Add(t); verts.Add(y); verts.Add(-half);
            verts.Add(t); verts.Add(y); verts.Add(+half);
        }

        // axes (X rouge, Z bleu) au centre — optionnel
        // X axis
        verts.Add(-half); verts.Add(y); verts.Add(0f);
        verts.Add(+half); verts.Add(y); verts.Add(0f);
        // Z axis
        verts.Add(0f); verts.Add(y); verts.Add(-half);
        verts.Add(0f); verts.Add(y); verts.Add(+half);

        _gridCount = verts.Count / 3;

        _gridVao = GL.GenVertexArray();
        _gridVbo = GL.GenBuffer();

        GL.BindVertexArray(_gridVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _gridVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Count * sizeof(float), verts.ToArray(), BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.BindVertexArray(0);


        _meshShader = new CharShader("assets/shaders/cube.vert", "assets/shaders/cube.frag");
        _meshShader.Use();
        _meshShader.Set("uTex", 0); // le sampler sur l’unité 0


        // PERSONNAGE DEJA DANS LA SCENE //
        _shader = new CharShader("assets/shaders/vertex.glsl", "assets/shaders/fragment.glsl");
        _shader2 = new CharShader("assets/shaders/vertex.glsl", "assets/shaders/fragment.glsl");

        // Camera
        /* _view = Matrix4.LookAt(_camPos, Vector3.Zero, Vector3.UnitY);
         // near assez petit, far assez grand
         _proj = Matrix4.CreatePerspectiveFieldOfView(
             MathHelper.DegreesToRadians(60f),
             Size.X / (float)Size.Y,
             0.01f,   // near
             500f     // far (ou plus si tu t’éloignes beaucoup)
         );*/

        _view = _camera.View;
        _proj = _camera.Projection;


        //Chargement du perso avec AssimpGlbManager
        try
        {
            string modelPath = "assets/models/model.glb";
            _char2 = AssimpGlbManager.Load(modelPath, _shader);
     
            _char2.RebuildModelMatrix(); 
            // compose la matrice finale
            //UpdateProjectionForCurrentView();
            //Fait l'association Model Binding 0
      


            _char3 = AssimpGlbManager.Load(modelPath, _shader2);


            _char3.RebuildModelMatrix();
            // compose la matrice finale          
            //Fait l'association Model Binding 0
          //  _char3.CreateBinding();

            Console.WriteLine($"Loaded model with {_char2._model.Meshes.Count} mesh(es). Bones: {_char2._skeleton.BoneName.Count}");
        }
        catch (Exception ex)
        {
           
        }


        // Recalc projection (déjà fait) et positionne la caméra pour voir l’objet entier.
        // On se met devant l’objet, un peu au-dessus, à une distance proportionnelle
        float fov = MathHelper.DegreesToRadians(60f);
        float fit = 1.2f; // marge pour respirer

        //float dist = (halfH * fit) / MathF.Tan(fov * 0.5f);
        // Caméra regardant le centre
        // var eye = _char2._model.Center + new Vector3(0, halfH * 0.5f, dist * 2.0f);
        //_camPos = eye;


        Debug.WriteLine(_char2.Position);

        _char2.Position = new Vector3(25f, -1f, -40.241173f);  //(-0.00925827, 0.28872776, -91.57314) //new Vector3 (25f, 0f, 55.5f);  //(-0.00925827, 0.28872776, -91.57314)

        _char2.EulerDeg.Y = -180f;
        _char3.EulerDeg.Y = -180f;

        //_char2.Position = new Vector3(5f, 0f, 0f);
        _char3.Position = new Vector3(25f, -1f, -42.241173f);   //(-0.00925827, 0.28872776, -91.57314)
        _char2.RebuildModelMatrix();
        _char3.RebuildModelMatrix();
        // par ex. dans OnLoad, après avoir positionné _char2 :
        // _meterCubePos = new Vector3(_char2.Position.X, _char2.Position.Y, _char2.Position.Z);

        // --- cube 1m (positions + normales + UV pour réutiliser ton shader mesh) ---
        float[] v = {
    // pos                // nrm         // uv
    // bas (y=-0.5)
    -0.5f,-0.5f,-0.5f,    0,-1,0,        0,0,
     0.5f,-0.5f,-0.5f,    0,-1,0,        1,0,
     0.5f,-0.5f, 0.5f,    0,-1,0,        1,1,
    -0.5f,-0.5f, 0.5f,    0,-1,0,        0,1,
    // haut (y=+0.5)
    -0.5f, 0.5f,-0.5f,    0, 1,0,        0,0,
     0.5f, 0.5f,-0.5f,    0, 1,0,        1,0,
     0.5f, 0.5f, 0.5f,    0, 1,0,        1,1,
    -0.5f, 0.5f, 0.5f,    0, 1,0,        0,1,
    // face -Z
    -0.5f,-0.5f,-0.5f,    0,0,-1,        0,0,
     0.5f,-0.5f,-0.5f,    0,0,-1,        1,0,
     0.5f, 0.5f,-0.5f,    0,0,-1,        1,1,
    -0.5f, 0.5f,-0.5f,    0,0,-1,        0,1,
    // face +Z
    -0.5f,-0.5f, 0.5f,    0,0, 1,        0,0,
     0.5f,-0.5f, 0.5f,    0,0, 1,        1,0,
     0.5f, 0.5f, 0.5f,    0,0, 1,        1,1,
    -0.5f, 0.5f, 0.5f,    0,0, 1,        0,1,
    // face -X
    -0.5f,-0.5f,-0.5f,   -1,0,0,         0,0,
    -0.5f,-0.5f, 0.5f,   -1,0,0,         1,0,
    -0.5f, 0.5f, 0.5f,   -1,0,0,         1,1,
    -0.5f, 0.5f,-0.5f,   -1,0,0,         0,1,
    // face +X
     0.5f,-0.5f,-0.5f,    1,0,0,         0,0,
     0.5f,-0.5f, 0.5f,    1,0,0,         1,0,
     0.5f, 0.5f, 0.5f,    1,0,0,         1,1,
     0.5f, 0.5f,-0.5f,    1,0,0,         0,1,
};
        uint[] idxs = {
    0,1,2, 2,3,0,       // bas
    4,5,6, 6,7,4,       // haut
    8,9,10, 10,11,8,    // -Z
    12,13,14, 14,15,12, // +Z
    16,17,18, 18,19,16, // -X
    20,21,22, 22,23,20  // +X
};
        _mCubeIndexCount = idxs.Length;

        _mCubeVao = GL.GenVertexArray();
        _mCubeVbo = GL.GenBuffer();
        _mCubeEbo = GL.GenBuffer();

        GL.BindVertexArray(_mCubeVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _mCubeVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, v.Length * sizeof(float), v, BufferUsageHint.StaticDraw);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _mCubeEbo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, idxs.Length * sizeof(uint), idxs, BufferUsageHint.StaticDraw);

        int stride = 8 * sizeof(float);
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));

        GL.BindVertexArray(0);

   

        // model posé au sol : centre du cube à Y=+0.5
        var Cubemodel = Matrix4.CreateTranslation(_meterCubePos + new Vector3(0f, 0.5f, 0f));
        var cubeMVP = _camera.GetMVP(Cubemodel);

        _meshShader.Use();
        _meshShader.Set("uMVP", cubeMVP);
        _meshShader.Set("uModel", Cubemodel);
        _meshShader.Set("uKdColor", new Vector3(0.0f, 1.0f, 0.0f)); // vert
        _meshShader.Set("uHasTex", false);
        _meshShader.Set("uCamPos", _camera.Position);
        _meshShader.Set("uLightPos", new Vector3(25.31f, 3f, -31.27f)); // comme ta scène
        _meshShader.Set("uSpecStrength", 0.1f);
        _meshShader.Set("uSpecPower", 16f);

        GL.BindVertexArray(_mCubeVao);
        GL.DrawElements(PrimitiveType.Triangles, _mCubeIndexCount, DrawElementsType.UnsignedInt, 0);
        GL.BindVertexArray(0);





        UpdateCamera();

        UpdateWindowTitle();


    }

    void UpdateProjectionForCurrentView()
    {
        float fovY = MathHelper.DegreesToRadians(60f);
        float aspect = Size.X / (float)Size.Y;

        // distance scène
        //float radius = MathF.Max(1e-3f, _char2.GetScaledRadius());     // ton rayon modèle (après scale)
        float dist = MathF.Max(1e-3f, _camDistance);

        // near ni trop petit ni trop grand, lié à la scène
        _near = Math.Clamp(dist * 0.05f, 0.03f, 0.5f);          // ~5% de la distance
        _far = Math.Max(_near * 10f, dist * 4f);

        _proj = Matrix4.CreatePerspectiveFieldOfView(fovY, aspect, _near, _far);
    }

    

    private void ApplySafetyNearPlane()
    {
        float safety = 0.05f; // marge minimale
        float distToCam = (_camPos - _modelOffset).Length;
        if (distToCam < safety)
            _modelOffset = _camPos - Vector3.Normalize(_camPos) * safety;
    }

      
   

    private void UpdateWindowTitle()
    {
        // Si tu suis mon montage: centre du modèle en world = _modelOffset.
        // (Sinon, alternative: var modelPos = new Vector3(_modelMat.M41, _modelMat.M42, _modelMat.M43);)
        var modelPos = _modelOffset;
        var camPos = _camPos;

        float dist = (camPos - modelPos).Length;

        // Title = $"CAM:NUM8/NUM2 | < Model > Cam: {V3(camPos)}  |  Model: {V3(modelPos)}  |  Dist: {dist:0.###}  |  Near: {_near:0.###}";
        //var play = _idlePlaying ? "▶" : "⏸";
        //var t = _idleTimeSec;
        Title = $"Idle {modelPos}";
    } 



    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);
        var kb = KeyboardState;


        float modifier =
 (kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift)) ? 5f :
 (kb.IsKeyDown(Keys.LeftControl) || kb.IsKeyDown(Keys.RightControl)) ? 0.2f : 1f;

        // --- Contrôles grille ---
        // Shift = plus rapide, Ctrl = plus lent
        float mul =
            (KeyboardState.IsKeyDown(Keys.LeftShift) || KeyboardState.IsKeyDown(Keys.RightShift)) ? 10f :
            (KeyboardState.IsKeyDown(Keys.LeftControl) || KeyboardState.IsKeyDown(Keys.RightControl)) ? 0.2f : 1f;

        if (KeyboardState.IsKeyDown(Keys.PageUp)) { _gridYOffset += _gridStepY * mul; }
        if (KeyboardState.IsKeyDown(Keys.PageDown)) { _gridYOffset -= _gridStepY * mul; }

 

         
      

        // Log rapide (option)
        if (KeyboardState.IsKeyPressed(Keys.F6))
        {
            _showGrid = (_showGrid == false ) ? true : false; 
            System.Diagnostics.Debug.WriteLine($"[grid] follow={_gridFollowChar} baseY={(_char2?.Position.Y ?? 0f):F3} offset={_gridYOffset:F3}");
        }

      
        float stepXZ = 0.2f * mul;  // pas XZ
        float stepY = 0.1f * mul;  // pas Y

        // Hauteur
        if (KeyboardState.IsKeyDown(Keys.PageUp)) _gridYOffset += stepY;
        if (KeyboardState.IsKeyDown(Keys.PageDown)) _gridYOffset -= stepY;

        // Déplacement X/Z : J/L (X- / X+), I/K (Z+ / Z-)
        if (KeyboardState.IsKeyDown(Keys.J)) _gridOffsetXZ.X -= stepXZ;   // -X
        if (KeyboardState.IsKeyDown(Keys.L)) _gridOffsetXZ.X += stepXZ;   // +X
        if (KeyboardState.IsKeyDown(Keys.I)) _gridOffsetXZ.Y += stepXZ;   // +Z
        if (KeyboardState.IsKeyDown(Keys.K)) _gridOffsetXZ.Y -= stepXZ;   // -Z

      
     
       
        //Debug.WriteLine($"[grid] follow={_gridFollowChar} offsetXZ=({_gridOffsetXZ.X:F2},{_gridOffsetXZ.Y:F2}) offsetY={_gridYOffset:F2}");

   
        float yawSpeed = 180f * (float)args.Time; // degrés/s
      
        float d = yawSpeed * mul;

  

        // Turn-in-place (A/D)
        if (kb.IsKeyDown(Keys.A)) _camera.YawInPlace(+d);
        if (kb.IsKeyDown(Keys.D)) _camera.YawInPlace(-d);


        // 1) Lecture des entrées (clavier/souris)

        if (kb.IsKeyPressed(Keys.Escape)) Close();

        // Play / Pause
        if (_char2._animIndex >= 0 && kb.IsKeyPressed(Keys.Space))            
            _char2.PlayPauseAnimation();

        if (_char3._animIndex >= 0 && kb.IsKeyPressed(Keys.Space))
            _char3.PlayPauseAnimation();

        // Vitesse (J/K)
        if (kb.IsKeyDown(Keys.J))
        {
            _char2.SetAnimationSpeed(MathF.Max(0.1f, _char2.GetAnimationSpeed() - 0.5f * (float)args.Time));
        }
        if (kb.IsKeyDown(Keys.K))
            _char2.SetAnimationSpeed(MathF.Min(4.0f, _char2.GetAnimationSpeed() + 0.5f * (float)args.Time));

 

        if (kb.IsKeyPressed(Keys.P))
            _char2.PlayPauseAnimation();

        if (kb.IsKeyPressed(Keys.A))
            gridY = 15f;


        //_char2.Position.Z += 0.00085f;

        float dt = (float)args.Time;

        // 1) Rotation gauche/droite avec Left / Right
        if (kb.IsKeyDown(Keys.Left))
        {
            _char2.EulerDeg.Y += _char2TurnSpeedDeg * dt;
        }
        if (kb.IsKeyDown(Keys.Right))
        {
            _char2.EulerDeg.Y -= _char2TurnSpeedDeg * dt;
        }


        // 2) Déplacement avant/arrière avec Up / Down
        //    On convertit l'angle Yaw (en degrés) du perso en direction avant dans le plan XZ
        float yawRad = MathHelper.DegreesToRadians(_char2.EulerDeg.Y);

        // OpenGL "en avant" = -Z en local. Donc on part de (0,0,-1) et on le tourne par yaw.
        Vector3 forwardDir = new Vector3(
            -MathF.Sin(yawRad), // X
             0f,
            -MathF.Cos(yawRad)  // Z
        );

        // normalisation au cas où (pas strictement nécessaire tant que yawRad vient d'un cos/sin, mais safe)
        forwardDir = forwardDir.Normalized();

        if (kb.IsKeyReleased(Keys.Up))
        {
            _char2._animIndex = 0;
        }
        if (kb.IsKeyDown(Keys.Down))
        {
            _char2.Position -= forwardDir * (_char2MoveSpeed * dt);
        }
        
        //Marche
        if (kb.IsKeyDown(Keys.Up))
        {
            _char2._animIndex = 3;
            _char2.Position += forwardDir * (_char2MoveSpeed * dt);
        }
        //course
        if (kb.IsKeyDown(Keys.Up) && kb.IsKeyDown(Keys.LeftShift))
        {
            _char2._animIndex = 1;
            _char2.Position += forwardDir * ((_char2MoveSpeed*1.65f) * dt);
        }

        // 3) Optionnel : petit strafe latéral avec A / D (comme RE4 Remake free-move)
        //    droite locale = croix de (0,1,0) x forward
        Vector3 rightDir = new Vector3(forwardDir.Z, 0f, -forwardDir.X); // rotation 90° autour Y

        if (kb.IsKeyDown(Keys.D))
        {
            _char2.Position += rightDir * (_char2MoveSpeed * dt);
        }
        if (kb.IsKeyDown(Keys.A))
        {
            _char2.Position -= rightDir * (_char2MoveSpeed * dt);
        }

        // 4) Debug visuel : affiche position + yaw
        Debug.WriteLine(
            $"char2 Pos=({_char2.Position.X:F2},{_char2.Position.Y:F2},{_char2.Position.Z:F2}) " +
            $"Yaw={_char2.EulerDeg.Y:F1}°  Scale={_char2.GetScale():F2}"
        );


        // Sélection: ← / → pour précédente / suivante
        if (_char2._scene?.AnimationCount > 0)
        {
            if (kb.IsKeyPressed(Keys.V))
            {
                _char2._animIndex = (_char2._animIndex - 1 + _char2._scene.AnimationCount) % _char2._scene.AnimationCount;
                _char2._animTimeSec = 0; _char2.UpdateCurrentAnimationDuration();
            }
            if (kb.IsKeyPressed(Keys.B))
            {
                _char2._animIndex = (_char2._animIndex + 1) % _char2._scene.AnimationCount;
                _char2._animTimeSec = 0; _char2.UpdateCurrentAnimationDuration();
            }
        }

        // (Option) sélection directe par chiffres 1..9
        if (_char2._scene?.AnimationCount > 0)
        {
            for (int i = 0; i < Math.Min(9, _char2._scene.AnimationCount); i++)
            {
                var key = Keys.D1 + i; // D1..D9
                if (kb.IsKeyPressed(key))
                {
                    _char2._animIndex = i; _char2._animTimeSec = 0; _char2.UpdateCurrentAnimationDuration();
                }
            }
        }

   

        // Vitesse de base en fonction de la taille de la scène
        float sceneScale = MathF.Max(0.5f, _char2._model.Radius); // >= 0.5 pour éviter 0   
        float baseUnitsPerSec = sceneScale * 2.5f; // ajuste si tu veux plus/moins rapide

        float yawSpeedDeg = 120f * (float)args.Time;

        float speed = 50f * (float)args.Time; // vitesse rotation degrés/s
        //float zoomSpeed = 1.2f; // facteur zoom

        //  Rotation orbitale avec les flèches
        /*if (kb.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Left))
            _char2.EulerDeg.Y += yawSpeedDeg;
        if (kb.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Right))
            _char2.EulerDeg.Y -= yawSpeedDeg;
        if (kb.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Up))
            _pitch += speed;
        if (kb.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Down))
            _pitch -= speed;*/

        
           



        _char2.RebuildModelMatrix();

        // Limite l’inclinaison (pitch)
        _pitch = Math.Clamp(_pitch, -89f, 89f);


   

        float lift = baseUnitsPerSec * modifier * (float)args.Time;
        // Monte/descend la caméra (pan vertical) — pavé numérique
        if (kb.IsKeyDown(Keys.KeyPad8))
        {
            _panOffset.Y += lift;
        }
        if (kb.IsKeyDown(Keys.KeyPad2))
            _panOffset.Y -= lift;


        float moveSpeed = baseUnitsPerSec * modifier * (float)args.Time;


        // --- Déplacement du cube mètre ---
        float cubeMoveSpeed = 2.0f * (float)args.Time; // vitesse de 2 m/s

        if (KeyboardState.IsKeyDown(Keys.J))
            _meterCubePos.X -= cubeMoveSpeed; // gauche

        if (KeyboardState.IsKeyDown(Keys.L))
            _meterCubePos.X += cubeMoveSpeed; // droite

        if (KeyboardState.IsKeyDown(Keys.I))
            _meterCubePos.Z -= cubeMoveSpeed; // avant (vers -Z)

        if (KeyboardState.IsKeyDown(Keys.K))
            _meterCubePos.Z += cubeMoveSpeed; // arrière (vers +Z)

       /* if (KeyboardState.IsKeyDown(Keys.B))
            _meterCubePos.Y -= cubeMoveSpeed; // arrière (vers -Y)

        if (KeyboardState.IsKeyDown(Keys.T))
            _meterCubePos.Y += cubeMoveSpeed; // arrière (vers +Y)*/

        // --- Affichage position debug ---
        //Debug.WriteLine($"MeterCube Pos = {_meterCubePos.X:F2}, {_meterCubePos.Y:F2}, {_meterCubePos.Z:F2}");





        // 2) Mises à jour caméra/orbite/zoom/pan
        UpdateCamera();
        //UpdateProjectionForCurrentView(); // ton recalcul near/far dynamique

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
        var kb = KeyboardState;     

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

        //Debug.WriteLine("ici:"+_char2.Position);

        //Background
        if (_visibleBackground)
        {
            GL.DepthMask(false); // don't write depth when drawing the background quad
            GL.UseProgram(_bgShader);
            GL.BindVertexArray(_bgVao);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _bgTexture);
            GL.Uniform1(GL.GetUniformLocation(_bgShader, "uTex"), 0);
            GL.DrawElements(GLPrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);
            GL.DepthMask(true);
        }


        GL.Enable(EnableCap.DepthTest);
        GL.DepthMask(true);          // on écrit dans le Z-buffer
        GL.Disable(EnableCap.Blend); // évite qu’elle “flotte” si tu blends ailleurs

        GL.Enable(EnableCap.PolygonOffsetLine);
        GL.PolygonOffset(1.0f, 1.0f);     // pousse la grille un peu “au fond”

        if (_showGrid)
        {
            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(true);

            _gridShader.Use();

            // place-la sous le perso si tu veux, et -0.01f pour éviter passer devant
            float gridY = (_gridFollowChar ? _char2.Position.Y : 0f) + _gridYOffset - 0.01f;
            var gridModel = Matrix4.CreateTranslation(
                new Vector3(0f,gridY,0f));

            _gridShader.Set("uModel", gridModel);
            _gridShader.Set("uView", _view);
            _gridShader.Set("uProj", _proj);

            // lignes fines
            GL.LineWidth(1f);
            _gridShader.Set("uColor", new Vector4(0.38f, 0.38f, 0.42f, 1f));
            GL.BindVertexArray(_gridVao);
            // tout sauf les 4 derniers sommets (axes)
            GL.DrawArrays(PrimitiveType.Lines, 0, _gridCount - 4);

            // axes plus visibles
            GL.LineWidth(2f);
            _gridShader.Set("uColor", new Vector4(0.9f, 0.2f, 0.2f, 1f));
            GL.DrawArrays(PrimitiveType.Lines, _gridCount - 4, 2); // X
            _gridShader.Set("uColor", new Vector4(0.2f, 0.45f, 0.9f, 1f));
            GL.DrawArrays(PrimitiveType.Lines, _gridCount - 2, 2); // Z

            GL.BindVertexArray(0);
        }

        GL.Disable(EnableCap.PolygonOffsetLine);



       // UpdateProjectionForCurrentView();        

        _char2.Update(args.Time);
        _char2.Render(_view, _proj, _camPos);

        _char3.Update(args.Time);
        _char3.Render(_view, _proj, _camPos);

        UpdateWindowTitle();



        if (_showMeterCube)
        {
            // modèle: on le pose au sol -> Y = 0.5 pour que la base touche Y=0
            var model = Matrix4.CreateTranslation(_meterCubePos + new Vector3(0f, 0.5f, 0f));
            var mvp = _camera.GetMVP(model);

            _meshShader.Use();

            _meshShader.Set("uMVP", mvp);
            _meshShader.Set("uModel", model);
            _meshShader.Set("uKdColor", new Vector3(0.0f, 1.0f, 0.0f));
            _meshShader.Set("uHasTex", 0);


  
            GL.BindVertexArray(_mCubeVao);
            GL.DrawElements(PrimitiveType.Triangles, _mCubeIndexCount, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }


        var (yawDeg, pitchDeg, rollDeg) = _camera.GetEulerYPR();
        //Debug.WriteLine($"[cam] Pitch={pitchDeg:F1}° Roll={rollDeg:F1}° Yaw={yawDeg:F1}°");

        /*Debug.WriteLine(
    $"[cam] Pos=({_camera.Position.X:F2}, {_camera.Position.Y:F2}, {_camera.Position.Z:F2})  " +
    $"Tgt=({_camera.Target.X:F2}, {_camera.Target.Y:F2}, {_camera.Target.Z:F2})");*/

     
    

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



        _view = _camera.View;

    }

    private void RebuildGridGeometry(float half, float step, float y = 0f)
    {
        var verts = new List<float>();
        int lines = (int)MathF.Floor(half / step);

        for (int i = -lines; i <= lines; i++)
        {
            float t = i * step;

            // Lignes parallèles à X (varie en Z)
            verts.Add(-half); verts.Add(y); verts.Add(t);
            verts.Add(+half); verts.Add(y); verts.Add(t);

            // Lignes parallèles à Z (varie en X)
            verts.Add(t); verts.Add(y); verts.Add(-half);
            verts.Add(t); verts.Add(y); verts.Add(+half);
        }

        // Axes (X rouge, Z bleu) au centre (2 segments) — optionnel
        verts.Add(-half); verts.Add(y); verts.Add(0f);
        verts.Add(+half); verts.Add(y); verts.Add(0f);
        verts.Add(0f); verts.Add(y); verts.Add(-half);
        verts.Add(0f); verts.Add(y); verts.Add(+half);

        _gridCount = verts.Count / 3;

        if (_gridVao == 0) _gridVao = GL.GenVertexArray();
        if (_gridVbo == 0) _gridVbo = GL.GenBuffer();

        GL.BindVertexArray(_gridVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _gridVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Count * sizeof(float), verts.ToArray(), BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.BindVertexArray(0);
    }


    protected override void OnUnload()
    {
        base.OnUnload();

        if (_gridVao != 0) GL.DeleteVertexArray(_gridVao);
        if (_gridVbo != 0) GL.DeleteBuffer(_gridVbo);
        _gridShader?.Dispose();
        //foreach (var m in _model.Meshes) m.Dispose();      
        _shader?.Dispose();
    }
}
