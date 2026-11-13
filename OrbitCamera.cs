using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// Camera orbită: privește mereu spre Target și se mișcă pe o sferă în jurul lui
/// la distanța Distance, sub unghiurile Azimuth (în jurul lui Y) și Elevation.
/// </summary>
public class OrbitCamera : Camera
{
    public Vector3 Target { get; set; }
    public float Distance { get; private set; }
    public float Azimuth { get; private set; }      // grade (similar cu yaw)
    public float Elevation { get; private set; }    // grade (similar cu pitch)

    // Parametri și limite
    public const float MaxElevation = 89.0f;
    public const float MinDistance = 1.0f;
    public const float MaxDistance = 40.0f;

    public float MouseSensitivity = 0.25f;          // grade pe pixel
    public float KeyRotateSpeed = 90.0f;            // grade pe secundă (săgeți)
    public float ZoomSpeed = 1.0f;                  // unități pe pas de rotiță
    public float PanSpeed = 5.0f;                   // unități pe secundă (W A S D)

    private static readonly Vector3 DefaultTarget = new(0.0f, 1.0f, -1.0f);
    private const float DefaultDistance = 8.0f;
    private const float DefaultAzimuth = 90.0f;     // poziționată pe +Z, privind spre -Z
    private const float DefaultElevation = 20.0f;

    public OrbitCamera()
    {
        Reset();
    }

    public override string Name => "Orbită";

    /// <summary>Poziția pe sferă: Target + Distance * (cos el * cos az, sin el, cos el * sin az).</summary>
    public override Vector3 Position
    {
        get
        {
            float az = MathHelper.DegreesToRadians(Azimuth);
            float el = MathHelper.DegreesToRadians(Elevation);
            float cosEl = MathF.Cos(el);

            Vector3 offset = new(
                cosEl * MathF.Cos(az),
                MathF.Sin(el),
                cosEl * MathF.Sin(az)
            );
            return Target + Distance * offset;
        }
    }

    public override Vector3 Front => Vector3.Normalize(Target - Position);

    public override Matrix4 ViewMatrix =>
        Matrix4.LookAt(Position, Target, Vector3.UnitY);

    public override void Update(KeyboardState keyboard, MouseState mouse, Vector2 mouseDelta, float dt)
    {
        // 1. Rotire cu clic stânga ținut + mouse
        if (mouse.IsButtonDown(MouseButton.Left))
        {
            Azimuth += mouseDelta.X * MouseSensitivity;
            Elevation += mouseDelta.Y * MouseSensitivity; // tragerea în jos ridică camera (unghi mai mare)
        }

        // 2. Rotire din tastatură (săgeți)
        if (keyboard.IsKeyDown(Keys.Left)) Azimuth += KeyRotateSpeed * dt;
        if (keyboard.IsKeyDown(Keys.Right)) Azimuth -= KeyRotateSpeed * dt;
        if (keyboard.IsKeyDown(Keys.Up)) Elevation += KeyRotateSpeed * dt;
        if (keyboard.IsKeyDown(Keys.Down)) Elevation -= KeyRotateSpeed * dt;

        // Limitarea elevației la ±89° (evită răsturnarea / blocarea la poli)
        Elevation = Math.Clamp(Elevation, -MaxElevation, MaxElevation);

        // 3. Rotița schimbă distanța, NU FOV-ul
        if (mouse.ScrollDelta.Y != 0.0f)
        {
            Distance = Math.Clamp(Distance - mouse.ScrollDelta.Y * ZoomSpeed, MinDistance, MaxDistance);
        }

        // 4. Al doilea bonus: W A S D mută ținta în planul orizontal
        Vector3 forward = new(Front.X, 0.0f, Front.Z);
        if (forward.LengthSquared > 0.0001f)
        {
            forward = Vector3.Normalize(forward);
            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));

            Vector3 move = Vector3.Zero;
            if (keyboard.IsKeyDown(Keys.W)) move += forward;
            if (keyboard.IsKeyDown(Keys.S)) move -= forward;
            if (keyboard.IsKeyDown(Keys.D)) move += right;
            if (keyboard.IsKeyDown(Keys.A)) move -= right;

            if (move.LengthSquared > 0.0f)
            {
                Target += Vector3.Normalize(move) * PanSpeed * dt;
            }
        }
    }

    /// <summary>Bonus 1: setează camera orbită pornind de la poziția și direcția unei camere FPS (fără salt).</summary>
    public void SyncFromFps(Vector3 fpsPosition, Vector3 fpsFront, float fixedDistance = 5.0f)
    {
        Distance = Math.Clamp(fixedDistance, MinDistance, MaxDistance);
        Target = fpsPosition + fpsFront * Distance;

        // Vectorul unitar țintă -> cameră este -fpsFront
        Vector3 o = Vector3.Normalize(-fpsFront);
        float elRad = MathF.Asin(Math.Clamp(o.Y, -1.0f, 1.0f));
        float azRad = MathF.Atan2(o.Z, o.X);

        Elevation = Math.Clamp(MathHelper.RadiansToDegrees(elRad), -MaxElevation, MaxElevation);
        Azimuth = MathHelper.RadiansToDegrees(azRad);
    }

    public override void Reset()
    {
        Target = DefaultTarget;
        Distance = DefaultDistance;
        Azimuth = DefaultAzimuth;
        Elevation = DefaultElevation;
        FieldOfView = 60.0f;
    }

    public override IEnumerable<string> Describe()
    {
        Vector3 pos = Position;
        yield return $"Țintă          : ({Target.X,6:F2}, {Target.Y,6:F2}, {Target.Z,6:F2})";
        yield return $"Distanță       : {Distance,6:F2}";
        yield return $"Azimut / elev. : {NormalizeAngle(Azimuth),7:F1}° / {Elevation,6:F1}°";
        yield return $"Poziție        : ({pos.X,6:F2}, {pos.Y,6:F2}, {pos.Z,6:F2})";
    }

    private static float NormalizeAngle(float degrees)
    {
        degrees %= 360.0f;
        if (degrees > 180.0f) degrees -= 360.0f;
        if (degrees <= -180.0f) degrees += 360.0f;
        return degrees;
    }
}