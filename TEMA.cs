
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>

///  - Drum cu 3 benzi (stânga, centru, dreapta);
///  - Schimbarea benzii cu Tween + EaseOut (0.16 s);
///  - Salt cu gravitație (Euler semi-implicit);
///  - Obstacole joase (se pot sări) și înalte (se ocolesc), cu viteză și frecvență crescătoare;
///  - Coliziuni AABB (cutii aliniate cu axele);
///  - Ierarhie părinte-copil (jucătorul are un pivot + corp + vizor/antenă copil);
/// </summary>
public sealed class AvoidObstaclesScene : Scene
{
    // Acțiuni proprii scenei (nu suprascriem Actions.Left / Right din LabScene ca să nu se amestece în meniul H,
    // sau folosim denumiri clare specifice jocului).
    private const string MoveLeft = "schimbă banda la stânga";
    private const string MoveRight = "schimbă banda la dreapta";
    private const string JumpAction = "salt peste obstacole joase";
    private const string RestartGame = "joc nou (după coliziune)";

    // Parametrii drumului și ai benzilor
    private static readonly float[] LaneX = { -2.4f, 0.0f, 2.4f };
    private const float RoadWidth = 7.6f;
    private const float RoadLength = 52.0f;
    private const float PlayerZ = 4.0f;
    private const float SpawnZ = -38.0f;
    private const float DespawnZ = 12.0f;

    // Parametrii jucătorului și ai fizicii
    private const float PlayerHalfW = 0.42f;
    private const float PlayerHalfH = 0.50f;
    private const float PlayerHalfD = 0.42f;
    private const float GroundY = PlayerHalfH;
    private const float LaneSwitchDuration = 0.16f; // sub 0.2 s, cu EaseOut
    private const float JumpSpeed = 10.5f;
    private const float Gravity = 26.0f;

    // Forme partajate
    private Mesh _cubeMesh = null!;
    private Mesh _diskMesh = null!;

    // Ierarhia jucătorului: pivot -> corp + vizor + antenă
    private SceneObject _playerRoot = null!;
    private SceneObject _playerBody = null!;
    private SceneObject _playerShadow = null!;
    private Tween _laneTween = null!;
    private int _currentLane = 1; // 0 = stânga, 1 = centru, 2 = dreapta
    private float _playerY = GroundY;
    private float _verticalVelocity;

    // Obstacole active
    private sealed class Obstacle
    {
        public SceneObject Visual = null!;
        public Vector3 HalfExtents;
        public bool IsHigh;
    }

    private readonly List<Obstacle> _obstacles = new();
    private readonly List<SceneObject> _roadMarkings = new();
    private readonly Random _rng = new();

    // Stare joc, viteză și scor
    private float _worldSpeed;
    private float _spawnTimer;
    private float _score;
    private float _bestScore;
    private bool _gameOver;

    public override string Name => "tema B — evită obstacolele";

    public override string Status => _gameOver
        ? $"GAME OVER | Scor: {_score:F1} s | Record: {_bestScore:F1} s (apasa R)"
        : $"Scor: {_score:F1} s | Record: {_bestScore:F1} s | Viteza: {_worldSpeed:F1}";

    public override IEnumerable<string> ActionNames => new[]
    {
        MoveLeft, MoveRight, JumpAction, RestartGame
    };

    public override void Load(Input input)
    {
        CameraPosition = new Vector3(0.0f, 5.2f, 11.2f);
        CameraTarget = new Vector3(0.0f, 0.8f, -4.0f);

        input.Bind(MoveLeft, Keys.A, Keys.Left);
        input.Bind(MoveRight, Keys.D, Keys.Right);
        input.Bind(JumpAction, Keys.Space, Keys.W, Keys.Up);
        input.Bind(RestartGame, Keys.R);

        _cubeMesh = AddMesh(Shapes.ShadedCube());
        _diskMesh = AddMesh(Shapes.ShadedCylinder(0.5f, 0.02f, 24));

        BuildRoad();
        BuildPlayer();
        ResetGame();
    }

    private void BuildRoad()
    {
        // Asfaltul principal
        SceneObject road = AddObject("drum", _cubeMesh);
        road.Transform.Position = new Vector3(0.0f, -0.1f, -13.0f);
        road.Transform.Scale = new Vector3(RoadWidth, 0.2f, RoadLength);
        road.Tint = new Color4(0.18f, 0.20f, 0.25f, 1.0f);

        // Borduri laterale
        foreach (float side in new[] { -1.0f, 1.0f })
        {
            SceneObject border = AddObject("bordura", _cubeMesh);
            border.Transform.Position = new Vector3(side * (RoadWidth * 0.5f + 0.2f), 0.1f, -13.0f);
            border.Transform.Scale = new Vector3(0.4f, 0.4f, RoadLength);
            border.Tint = new Color4(0.85f, 0.35f, 0.25f, 1.0f);
        }

        // Bonus: marcaje discontinue între benzi care se deplasează odată cu lumea
        float[] dividerX = { -1.2f, 1.2f };
        for (int row = 0; row < 12; row++)
        {
            float z = SpawnZ + row * 4.2f;
            foreach (float x in dividerX)
            {
                SceneObject dash = AddObject("marcaj", _cubeMesh);
                dash.Transform.Position = new Vector3(x, 0.01f, z);
                dash.Transform.Scale = new Vector3(0.14f, 0.02f, 1.8f);
                dash.Tint = new Color4(0.92f, 0.92f, 0.95f, 1.0f);
                _roadMarkings.Add(dash);
            }
        }
    }

    private void BuildPlayer()
    {
        // Umbra pe asfalt
        _playerShadow = AddObject("umbra jucator", _diskMesh);
        _playerShadow.Tint = new Color4(0.05f, 0.06f, 0.08f, 1.0f);

        // Părintele (pivotul logic al jucătorului)
        _playerRoot = AddObject("jucator_pivot", null);

        // Corpul principal (copil al pivotului)
        _playerBody = AddObject("jucator_corp", _cubeMesh, _playerRoot);
        _playerBody.Transform.Scale = new Vector3(PlayerHalfW * 2f, PlayerHalfH * 2f, PlayerHalfD * 2f);
        _playerBody.Tint = new Color4(0.20f, 0.78f, 0.95f, 1.0f);

        // Vizor frontal (copil al corpului -> ierarhie pe 2 niveluri)
        SceneObject visor = AddObject("jucator_vizor", _cubeMesh, _playerBody);
        visor.Transform.Position = new Vector3(0.0f, 0.18f, -0.46f);
        visor.Transform.Scale = new Vector3(0.75f, 0.25f, 0.15f);
        visor.Tint = new Color4(1.0f, 0.92f, 0.30f, 1.0f);

        // Rucsac / propulsor spate (copil al corpului)
        SceneObject pack = AddObject("jucator_propulsor", _cubeMesh, _playerBody);
        pack.Transform.Position = new Vector3(0.0f, 0.0f, 0.52f);
        pack.Transform.Scale = new Vector3(0.55f, 0.60f, 0.22f);
        pack.Tint = new Color4(0.95f, 0.45f, 0.20f, 1.0f);

        _laneTween = new Tween(
            new Vector3(LaneX[1], 0f, 0f),
            new Vector3(LaneX[1], 0f, 0f),
            LaneSwitchDuration,
            Easing.EaseOut);
    }

    private void ResetGame()
    {
        // Ștergem obstacolele vechi din lista de obiecte ale scenei
        foreach (Obstacle obs in _obstacles)
        {
            Objects.Remove(obs.Visual);
        }
        _obstacles.Clear();

        _currentLane = 1;
        _laneTween = new Tween(
            new Vector3(LaneX[_currentLane], 0f, 0f),
            new Vector3(LaneX[_currentLane], 0f, 0f),
            LaneSwitchDuration,
            Easing.EaseOut);

        _playerY = GroundY;
        _verticalVelocity = 0.0f;
        _worldSpeed = 13.0f;
        _spawnTimer = 0.6f;
        _score = 0.0f;
        _gameOver = false;

        _playerBody.Tint = new Color4(0.20f, 0.78f, 0.95f, 1.0f);
        UpdatePlayerTransform();
    }

    public override void Update(float dt, Input input)
    {
        base.Update(dt, input);

        if (_gameOver)
        {
            if (input.WasPressed(RestartGame))
            {
                ResetGame();
                ConsolePanel.PrintMessage("joc nou pornit!");
            }
            return;
        }

        if (dt <= 0.0f) return;

        // 1. Scor și creșterea progresivă a vitezei lumii
        _score += dt;
        if (_score > _bestScore) _bestScore = _score;
        _worldSpeed = 13.0f + _score * 0.45f; // accelerează treptat

        // 2. Schimbarea benzii (Tween cu EaseOut, sub 0.2 s)
        if (input.WasPressed(MoveLeft) && _currentLane > 0)
        {
            _currentLane--;
            _laneTween.GoTo(new Vector3(LaneX[_currentLane], 0f, 0f), LaneSwitchDuration);
        }
        if (input.WasPressed(MoveRight) && _currentLane < LaneX.Length - 1)
        {
            _currentLane++;
            _laneTween.GoTo(new Vector3(LaneX[_currentLane], 0f, 0f), LaneSwitchDuration);
        }
        _laneTween.Update(dt);

        // 3. Salt cu gravitație (Euler semi-implicit)
        bool onGround = _playerY <= GroundY + 1e-4f;
        if (onGround && input.WasPressed(JumpAction))
        {
            _verticalVelocity = JumpSpeed;
        }

        if (!onGround || _verticalVelocity > 0.0f)
        {
            _verticalVelocity -= Gravity * dt;
            _playerY += _verticalVelocity * dt;
            if (_playerY <= GroundY)
            {
                _playerY = GroundY;
                _verticalVelocity = 0.0f;
            }
        }

        UpdatePlayerTransform();

        // 4. Bonus: deplasarea marcajelor de pe drum
        float loopSpan = 12 * 4.2f;
        foreach (SceneObject dash in _roadMarkings)
        {
            Vector3 pos = dash.Transform.Position;
            pos.Z += _worldSpeed * dt;
            if (pos.Z > DespawnZ)
            {
                pos.Z -= loopSpan;
            }
            dash.Transform.Position = pos;
        }

        // 5. Generarea obstacolelor (frecvența crește odată cu viteza)
        _spawnTimer -= dt;
        if (_spawnTimer <= 0.0f)
        {
            SpawnObstacleRow();
            float interval = Math.Clamp(1.35f - _score * 0.018f, 0.55f, 1.35f);
            _spawnTimer = interval;
        }

        // 6. Deplasarea obstacolelor și testarea coliziunilor AABB
        Vector3 playerCenter = new(_laneTween.Value.X, _playerY, PlayerZ);
        Vector3 playerHalf = new(PlayerHalfW, PlayerHalfH, PlayerHalfD);

        for (int i = _obstacles.Count - 1; i >= 0; i--)
        {
            Obstacle obs = _obstacles[i];
            Vector3 pos = obs.Visual.Transform.Position;
            pos.Z += _worldSpeed * dt;
            obs.Visual.Transform.Position = pos;

            if (CheckAabbOverlap(playerCenter, playerHalf, pos, obs.HalfExtents))
            {
                _gameOver = true;
                _playerBody.Tint = new Color4(0.95f, 0.22f, 0.22f, 1.0f);
                ConsolePanel.PrintMessage($"Coliziune! Scor: {_score:F1} s (Record: {_bestScore:F1} s). Apasă R pentru joc nou.");
                return;
            }

            if (pos.Z - obs.HalfExtents.Z > DespawnZ)
            {
                Objects.Remove(obs.Visual);
                _obstacles.RemoveAt(i);
            }
        }
    }

    private void UpdatePlayerTransform()
    {
        float x = _laneTween.Value.X;
        _playerRoot.Transform.Position = new Vector3(x, _playerY, PlayerZ);

        // Ușoară înclinare laterală în timpul schimbării benzii
        float tiltZ = (_laneTween.To.X - x) * -8.0f;
        _playerRoot.Transform.Rotation = new Vector3(0.0f, 0.0f, tiltZ);

        // Umbra rămâne pe sol și se micșorează când jucătorul sare
        float jumpHeight = _playerY - GroundY;
        float shadowScale = 1.05f / (1.0f + 0.35f * jumpHeight);
        _playerShadow.Transform.Position = new Vector3(x, 0.015f, PlayerZ);
        _playerShadow.Transform.Scale = new Vector3(shadowScale, 1.0f, shadowScale);
    }

    private void SpawnObstacleRow()
    {
        // Blocăm cel mult 2 benzi din 3, ca jucătorul să aibă întotdeauna o cale de scăpare
        int blockedCount = _rng.NextDouble() < 0.45 ? 2 : 1;
        int firstLane = _rng.Next(0, 3);

        for (int k = 0; k < blockedCount; k++)
        {
            int lane = (firstLane + k) % 3;
            bool isHigh = _rng.NextDouble() < 0.45;

            float halfW = 0.85f;
            float halfH = isHigh ? 1.35f : 0.42f; // jos = 0.84 unități (se poate sări), înalt = 2.70 unități
            float halfD = 0.55f;

            SceneObject visual = AddObject(isHigh ? "obstacol_inalt" : "obstacol_jos", _cubeMesh);
            visual.Transform.Position = new Vector3(LaneX[lane], halfH, SpawnZ);
            visual.Transform.Scale = new Vector3(halfW * 2f, halfH * 2f, halfD * 2f);
            visual.Tint = isHigh
                ? new Color4(0.90f, 0.24f, 0.28f, 1.0f)  // roșu: trebuie ocolit
                : new Color4(0.98f, 0.72f, 0.18f, 1.0f); // portocaliu: se poate sări

            _obstacles.Add(new Obstacle
            {
                Visual = visual,
                HalfExtents = new Vector3(halfW, halfH, halfD),
                IsHigh = isHigh
            });
        }
    }

    /// <summary>Coliziune AABB: două cutii aliniate cu axele se suprapun dacă se suprapun pe toate cele 3 axe.</summary>
    private static bool CheckAabbOverlap(Vector3 centerA, Vector3 halfA, Vector3 centerB, Vector3 halfB)
    {
        bool overlapX = MathF.Abs(centerA.X - centerB.X) < (halfA.X + halfB.X);
        bool overlapY = MathF.Abs(centerA.Y - centerB.Y) < (halfA.Y + halfB.Y);
        bool overlapZ = MathF.Abs(centerA.Z - centerB.Z) < (halfA.Z + halfB.Z);
        return overlapX && overlapY && overlapZ;
    }

    public override IEnumerable<string> Describe()
    {
        foreach (string line in base.Describe()) yield return line;
        yield return $"Stare joc      : {(_gameOver ? "GAME OVER" : "în desfășurare")}";
        yield return $"Scor / Record  : {_score:F1} s / {_bestScore:F1} s";
        yield return $"Viteza lumii   : {_worldSpeed:F1} unități/s";
        yield return $"Banda curentă  : {_currentLane + 1} / 3 (x = {_laneTween.Value.X:F2})";
        yield return $"Obstacole      : {_obstacles.Count} active";
    }
}