// Scenă suplimentară: Mașină mov cu girofar care se deplasează dintr-o parte în alta și o minge care sare peste ea
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

public sealed class CarAndBallScene : Scene
{
    private const string JumpAction = "salt peste obstacole joase";

    private const string JumpBall = "salt minge peste mașină";
    private const string ToggleAuto = "comută mod automat / manual minge";
    private const string RestartAnim = "repornește animația";

    // Mașina (ierarhie + Tween dus-întors)
    private SceneObject _carRoot = null!;
    private SceneObject _carBody = null!;
    private SceneObject _sirenPivot = null!;
    private readonly List<SceneObject> _wheels = new();
    private Tween _carTween = null!;
    private float _prevCarX;

    // Mingea (KeyframeTrack pentru traiectoria peste mașină + fizică gravitațională pentru salt)
    private SceneObject _ball = null!;
    private SceneObject _ballShadow = null!;
    private KeyframeTrack _ballTrack = null!;
    private float _trackTime;
    private bool _autoMode = true;

    // Parametri fizici pentru modul manual (Space)
    private Vector3 _ballPos;
    private float _ballVelY;
    private float _ballDirX = 1.0f;
    private const float BallGroundY = 0.35f;
    private const float Gravity = 22.0f;
    private const float JumpVelocity = 11.5f;

    // Contor sărituri reușite peste mașină
    private int _successfulJumps;
    private int _bestJumps;
    private bool _wasAboveCar;
    private float _hitFlashTimer;

    public override string Name => "mașina și mingea";

    public override string Status =>
        $"Sărituri peste mașină: {_successfulJumps} (Record: {_bestJumps}) | Mod: {(_autoMode ? "Automat" : "Manual - Space")}";

    public override IEnumerable<string> ActionNames => new[]
    {
        JumpBall, ToggleAuto, RestartAnim
    };

    public override void Load(Input input)
    {
        CameraPosition = new Vector3(0.0f, 5.5f, 13.5f);
        CameraTarget = new Vector3(0.0f, 1.2f, 0.0f);

        input.Bind(JumpBall, Keys.Space);
        input.Bind(ToggleAuto, Keys.M);
        input.Bind(RestartAnim, Keys.R);

        Mesh grid = AddMesh(Shapes.Grid(12, 1.0f, new Color4(0.30f, 0.33f, 0.38f, 1.0f)));
        Mesh cube = AddMesh(Shapes.ShadedCube());
        Mesh sphere = AddMesh(Shapes.ShadedSphere(1.0f, 28, 16));
        Mesh wheelMesh = AddMesh(Shapes.ShadedCylinder(0.38f, 0.25f, 20));
        Mesh shadowDisk = AddMesh(Shapes.ShadedCylinder(0.5f, 0.02f, 24));

        // Podeaua și șoseaua
        AddObject("podea", grid);

        SceneObject road = AddObject("sosea", cube);
        road.Transform.Position = new Vector3(0.0f, 0.01f, 0.0f);
        road.Transform.Scale = new Vector3(22.0f, 0.02f, 4.2f);
        road.Tint = new Color4(0.18f, 0.20f, 0.24f, 1.0f);

        BuildCar(cube, wheelMesh);
        BuildBall(sphere, shadowDisk);
    }

    private void BuildCar(Mesh cube, Mesh wheelMesh)
    {
        // 1. Pivotul mașinii (părintele tuturor componentelor ei)
        _carRoot = AddObject("masina_pivot", null);
        _carRoot.Transform.Position = new Vector3(-8.0f, 0.0f, 0.0f);

        // 2. Șasiul mov (copil al pivotului)
        _carBody = AddObject("masina_sasiu", cube, _carRoot);
        _carBody.Transform.Position = new Vector3(0.0f, 0.65f, 0.0f);
        _carBody.Transform.Scale = new Vector3(3.2f, 0.65f, 1.6f);
        _carBody.Tint = new Color4(0.65f, 0.25f, 0.90f, 1.0f); // mov

        // 3. Cabina (copil al șasiului)
        SceneObject cabin = AddObject("masina_cabina", cube, _carBody);
        cabin.Transform.Position = new Vector3(-0.08f, 0.72f, 0.0f);
        cabin.Transform.Scale = new Vector3(0.55f, 0.75f, 0.86f);
        cabin.Tint = new Color4(0.85f, 0.70f, 0.98f, 1.0f); // lila deschis

        // 4. Girofarul pe acoperișul cabinei (ierarhie pe 4 niveluri: pivot -> sasiu -> cabina -> girofar)
        _sirenPivot = AddObject("girofar_pivot", null, cabin);
        _sirenPivot.Transform.Position = new Vector3(0.0f, 0.58f, 0.0f);
        _sirenPivot.Spin = new Vector3(0.0f, 540.0f, 0.0f); // se rotește rapid (1.5 rotații/sec)

        SceneObject blueLight = AddObject("girofar_albastru", cube, _sirenPivot);
        blueLight.Transform.Position = new Vector3(0.10f, 0.0f, 0.0f);
        blueLight.Transform.Scale = new Vector3(0.18f, 0.24f, 0.22f);
        blueLight.Tint = new Color4(0.15f, 0.55f, 1.0f, 1.0f);

        SceneObject redLight = AddObject("girofar_rosu", cube, _sirenPivot);
        redLight.Transform.Position = new Vector3(-0.10f, 0.0f, 0.0f);
        redLight.Transform.Scale = new Vector3(0.18f, 0.24f, 0.22f);
        redLight.Tint = new Color4(1.0f, 0.20f, 0.25f, 1.0f);

        // Faruri față (+X)
        foreach (float z in new[] { -0.32f, 0.32f })
        {
            SceneObject headlight = AddObject("far", cube, _carBody);
            headlight.Transform.Position = new Vector3(0.51f, 0.05f, z);
            headlight.Transform.Scale = new Vector3(0.06f, 0.22f, 0.16f);
            headlight.Tint = new Color4(1.0f, 0.95f, 0.45f, 1.0f);
        }

        // Cele 4 roți (copii ai pivotului mașinii)
        Vector3[] wheelOffsets =
        {
            new(-1.05f, 0.38f,  0.85f),
            new( 1.05f, 0.38f,  0.85f),
            new(-1.05f, 0.38f, -0.85f),
            new( 1.05f, 0.38f, -0.85f),
        };

        foreach (Vector3 offset in wheelOffsets)
        {
            SceneObject wheelMount = AddObject("suport_roata", null, _carRoot);
            wheelMount.Transform.Position = offset;
            wheelMount.Transform.Rotation = new Vector3(90.0f, 0.0f, 0.0f);

            SceneObject wheel = AddObject("roata", wheelMesh, wheelMount);
            wheel.Tint = new Color4(0.12f, 0.12f, 0.14f, 1.0f);
            _wheels.Add(wheel);
        }

        // Tween dus-întors dintr-o parte în alta
        _carTween = new Tween(
            new Vector3(-8.0f, 0.0f, 0.0f),
            new Vector3(8.0f, 0.0f, 0.0f),
            3.6f,
            Easing.EaseInOut,
            pingPong: true);

        _prevCarX = _carTween.From.X;
    }

    private void BuildBall(Mesh sphere, Mesh shadowDisk)
    {
        _ballShadow = AddObject("umbra_minge", shadowDisk);
        _ballShadow.Tint = new Color4(0.05f, 0.06f, 0.08f, 1.0f);

        _ball = AddObject("minge", sphere);
        _ball.Transform.Scale = new Vector3(0.35f);
        _ball.Tint = new Color4(1.0f, 0.65f, 0.10f, 1.0f); // portocaliu

        // Traseu prin cadre-cheie: mingea sare în arc de cerc peste mașină dintr-o parte în alta și înapoi
        _ballTrack = new KeyframeTrack(0.45f) { Ease = Easing.Linear };
        _ballTrack
            .Add(new Vector3(6.5f, BallGroundY, 0.0f))  // dreapta jos
            .Add(new Vector3(3.2f, 2.8f, 0.0f))  // urcă
            .Add(new Vector3(0.0f, 4.1f, 0.0f))  // sus, peste mașină!
            .Add(new Vector3(-3.2f, 2.8f, 0.0f))  // coboară
            .Add(new Vector3(-6.5f, BallGroundY, 0.0f))  // stânga jos (atinge solul)
            .Add(new Vector3(-3.2f, 2.8f, 0.0f))  // urcă înapoi
            .Add(new Vector3(0.0f, 4.1f, 0.0f))  // sus, peste mașină!
            .Add(new Vector3(3.2f, 2.8f, 0.0f)); // coboară spre dreapta

        _ballPos = new Vector3(-6.5f, BallGroundY, 0.0f);
    }

    public override void Update(float dt, Input input)
    {
        base.Update(dt, input); // rotește automat girofarul (_sirenPivot.Spin)

        if (input.WasPressed(RestartAnim))
        {
            _carTween.Restart();
            _prevCarX = _carTween.From.X;
            _trackTime = 0.0f;
            _ballPos = new Vector3(-6.5f, BallGroundY, 0.0f);
            _ballVelY = 0.0f;
            _successfulJumps = 0;
            _wasAboveCar = false;
            _hitFlashTimer = 0.0f;
            _ball.Tint = new Color4(1.0f, 0.65f, 0.10f, 1.0f);
            ConsolePanel.PrintMessage("animația și contorul au repornit");
        }

        if (input.WasPressed(ToggleAuto))
        {
            _autoMode = !_autoMode;
            _ballPos = _ball.Transform.Position;
            _ballVelY = 0.0f;
            _wasAboveCar = false;
            ConsolePanel.PrintMessage($"mod minge: {(_autoMode ? "automat (KeyframeTrack)" : "manual (apasă Space pentru salt)")}");
        }

        if (dt <= 0.0f) return;

        // 1. Deplasarea mașinii dintr-o parte în alta (Tween)
        _carTween.Update(dt);
        Vector3 carPos = _carTween.Value;
        _carRoot.Transform.Position = carPos;

        float velocityX = (carPos.X - _prevCarX) / dt;
        _prevCarX = carPos.X;

        if (MathF.Abs(velocityX) > 0.05f)
        {
            _carRoot.Transform.Rotation = new Vector3(0.0f, velocityX >= 0 ? 0.0f : 180.0f, 0.0f);
        }

        float wheelSpinSpeed = MathF.Abs(velocityX) * 140.0f;
        foreach (SceneObject wheel in _wheels)
        {
            wheel.Transform.Rotation += new Vector3(0.0f, -wheelSpinSpeed * dt, 0.0f);
        }

        // 2. Mișcarea mingii care sare peste mașină
        if (_autoMode)
        {
            _trackTime += dt;
            _ballPos = _ballTrack.Sample(_trackTime);
        }
        else
        {
            _ballPos.X += _ballDirX * 5.0f * dt;
            if (_ballPos.X > 7.5f) { _ballPos.X = 7.5f; _ballDirX = -1.0f; }
            if (_ballPos.X < -7.5f) { _ballPos.X = -7.5f; _ballDirX = 1.0f; }

            bool onGround = _ballPos.Y <= BallGroundY + 1e-4f;
            if (onGround && input.WasPressed(JumpAction))
            {
                _ballVelY = JumpVelocity;
            }

            if (!onGround || _ballVelY > 0.0f)
            {
                _ballVelY -= Gravity * dt;
                _ballPos.Y += _ballVelY * dt;
                if (_ballPos.Y <= BallGroundY)
                {
                    _ballPos.Y = BallGroundY;
                    _ballVelY = 0.0f;
                }
            }
        }

        // 3. Contorizare sărituri reușite peste mașină și verificare coliziune (AABB simplificat pe X și Y)
        float dx = MathF.Abs(_ballPos.X - carPos.X);
        bool horizontallyOverCar = dx < 1.75f; // jumătate din lungimea mașinii + raza mingii
        bool safelyAboveCar = _ballPos.Y > 1.85f; // deasupra cabinei și a girofarului

        if (horizontallyOverCar)
        {
            if (safelyAboveCar)
            {
                _wasAboveCar = true;
            }
            else if (!_autoMode && _hitFlashTimer <= 0.0f)
            {
                // Atingere în modul manual -> resetează seria curentă
                _successfulJumps = 0;
                _wasAboveCar = false;
                _hitFlashTimer = 0.8f;
                ConsolePanel.PrintMessage("Ai atins mașina! Contorul de sărituri s-a resetat.");
            }
        }
        else if (_wasAboveCar)
        {
            // Mingea tocmai a trecut cu succes dintr-o parte în alta pe deasupra mașinii!
            _successfulJumps++;
            if (_successfulJumps > _bestJumps) _bestJumps = _successfulJumps;
            _wasAboveCar = false;
        }

        // Culoarea mingii (roșu scurt la lovire, apoi revine la portocaliu)
        if (_hitFlashTimer > 0.0f)
        {
            _hitFlashTimer -= dt;
            _ball.Tint = new Color4(0.95f, 0.20f, 0.20f, 1.0f);
        }
        else
        {
            _ball.Tint = new Color4(1.0f, 0.65f, 0.10f, 1.0f);
        }

        _ball.Transform.Position = _ballPos;
        _ball.Transform.Rotation += new Vector3(0.0f, 0.0f, -180.0f * dt);

        // Actualizăm umbra mingii pe sol
        float heightAboveGround = MathF.Max(0.0f, _ballPos.Y - BallGroundY);
        float shadowScale = 0.85f / (1.0f + 0.35f * heightAboveGround);
        _ballShadow.Transform.Position = new Vector3(_ballPos.X, 0.025f, _ballPos.Z);
        _ballShadow.Transform.Scale = new Vector3(shadowScale, 1.0f, shadowScale);
    }

    public override IEnumerable<string> Describe()
    {
        foreach (string line in base.Describe()) yield return line;
        yield return $"Mașină X       : {_carRoot.Transform.Position.X:F2} (progres tween: {_carTween.Progress:F2})";
        yield return $"Minge          : ({_ballPos.X:F2}, {_ballPos.Y:F2}) — mod {(_autoMode ? "Automat" : "Manual")}";
        yield return $"Sărituri       : {_successfulJumps} reușite (record: {_bestJumps})";
    }
}