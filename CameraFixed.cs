using OpenTK.Mathematics;

namespace GlbOpenTKDemo
{
    public class CameraFixed
    {
        // --- État caméra ---
        public Vector3 Position = new(0, 5f, 10f);
        public Vector3 Target = Vector3.Zero;
        public Vector3 Up = Vector3.UnitY;

        public Vector3 Forward => (Target - Position).LengthSquared > 0
    ? Vector3.Normalize(Target - Position)
    : -Vector3.UnitZ;

        public Vector3 UpNorm => Vector3.Normalize(Up);
        public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, UpNorm));


        // --- Projection ---
        public float FovY = MathHelper.DegreesToRadians(60f);
        public float Aspect = 16f / 9f;
        public float Near = 0.1f;
        public float Far = 100f;

        // --- Matrices ---
        public Matrix4 View => Matrix4.LookAt(Position, Target, Up);
        public Matrix4 Projection => Matrix4.CreatePerspectiveFieldOfView(FovY, Aspect, Near, Far);

        /// MVP dans l’ordre OpenTK : Model * View * Projection
        public Matrix4 GetMVP(Matrix4 model) => model * View * Projection;
        // P * V * M
        //public Matrix4 GetMVP(Matrix4 model) => Projection * View * model;


        /// Matrice des normales (pour éclairage)
        public Matrix4 GetNormalMatrix(Matrix4 model)
        {
            var n = model.Inverted(); n.Transpose(); return n;
        }

        // ============================
        //          HELPERS
        // ============================

        /// LookAt basique : position + cible, Up conservé
        public void LookAt(Vector3 position, Vector3 target)
        {
            Position = position;
            Target = target;
            // Up reste tel quel (par défaut = +Y). Si la cam est trop "rasante",
            // n'hésite pas à remettre Up = Vector3.UnitY;
        }

        /// LookAt complet : position + cible + Up
        public void LookAt(Vector3 position, Vector3 target, Vector3 up)
        {
            Position = position;
            Target = target;
            Up = up.Normalized();
        }

        /// Configure la projection (utile quand tu changes FOV/aspect/clip)
        public void SetProjection(float fovYRadians, float aspect, float near = 0.1f, float far = 100f)
        {
            FovY = fovYRadians; Aspect = aspect; Near = near; Far = far;
        }

        /// Place la caméra depuis un export Blender (déjà converti en repère OpenGL)
        /// pos = (x, y, z), forward = direction avant (normée ou non), up = haut (normé ou non)
        /// In blender,  
        /// the right is the x-axis. (pitch)
        /// the forward is the y-axis (roll)
        /// the up axis is the z-axis (yaw) 
        public void FromBlenderOpenGL(Vector3 pos, Vector3 forward, Vector3 up, float fovYRadians, float aspect, float near = 0.1f, float far = 100f)
        {
            var f = forward.LengthSquared > 0 ? forward.Normalized() : -Vector3.UnitZ;
            var u = up.LengthSquared > 0 ? up.Normalized() : Vector3.UnitY;

            Position = pos;
            Target = pos + f;
            Up = u;
            SetProjection(fovYRadians, aspect, near, far);
        }

        public void OrbitYaw(float degrees, Vector3 pivot)
        {
            float rad = MathHelper.DegreesToRadians(degrees);
            var q = Quaternion.FromAxisAngle(Vector3.UnitY, rad);

            Vector3 dir = Position - pivot;
            Vector3 newDir = Vector3.Transform(dir, q);

            Position = pivot + newDir;
            Target = pivot;
            Up = Vector3.UnitY;
        }

        public void YawInPlace(float degrees)
        {
            float rad = MathHelper.DegreesToRadians(degrees);
            var q = Quaternion.FromAxisAngle(Vector3.UnitY, rad);

            Vector3 dir = Target - Position;
            Vector3 newDir = Vector3.Transform(dir, q);

            Target = Position + newDir;
            Up = Vector3.UnitY;
        }


        public (float YawDeg, float PitchDeg, float RollDeg) GetEulerYPR()
        {
            // bases orthonormées de la caméra
            var f = Forward;                  // vers la cible
            var upN = UpNorm;                 // Up normalisé
            var r = Vector3.Normalize(Vector3.Cross(f, upN));
            var u = Vector3.Normalize(Vector3.Cross(r, f));

            // Yaw (autour de Y) : cap horizontal
            float yaw = MathF.Atan2(f.X, f.Z);

            // Pitch (autour de X) : inclinaison verticale
            float pitch = MathF.Atan2(f.Y, MathF.Sqrt(f.X * f.X + f.Z * f.Z));

            // Roll (autour de Z) : banc (penchement)
            // Compare le Up réel à l'Up "idéal" déduit de f & r (u).
            // Si ta caméra maintient Up = (0,1,0), le roll sera ~0.
            float roll = MathF.Atan2(Vector3.Dot(r, upN), Vector3.Dot(u, upN));

            return (MathHelper.RadiansToDegrees(yaw),
                    MathHelper.RadiansToDegrees(pitch),
                    MathHelper.RadiansToDegrees(roll));
        }


    }
}
