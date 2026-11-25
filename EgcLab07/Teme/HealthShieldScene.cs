// Varianta B: bara de viață și scutul
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc.Teme;

public sealed class HealthShieldScene : Scene
{
    private const string ActionDamage = "lovitură (-1 punct / -0.5 cu scut)";
    private const string ActionCrit = "lovitură critică (-3 puncte)";
    private const string ActionHeal = "vindecare (+1 punct)";
    private const string ActionToggleShield = "scut: pornit / oprit";
    private const string ActionMoveLeft = "deplasare stânga";
    private const string ActionMoveRight = "deplasare dreapta";
    private const string ActionMoveForward = "deplasare înainte";
    private const string ActionMoveBack = "deplasare înapoi";
    private const int TotalSegments = 10;

    private Material _barMat = null!;
    private Material _shieldMat = null!;
    private SceneObject _hero = null!;
    private SceneObject _shieldObj = null!;
    private SceneObject _healthBar = null!;

    private Vector3 _heroBasePos = new(0.0f, 1.0f, 0.0f);
    private float _bobTime;

    private float _healthTarget = 1.0f;
    private Tween _healthTween = null!;
    private Tween _trailTween = null!;
    private float _trailDelayTimer;

    private bool _shieldActive = true;
    private Tween _shieldAlphaTween = null!;
    private float _barFlash;
    private float _shieldFlash;

    public override string Name => "tema B: viață și scut";

    public override IEnumerable<string> ActionNames => new[]
    {
        ActionDamage, ActionCrit, ActionHeal, ActionToggleShield,
        ActionMoveLeft, ActionMoveRight, ActionMoveForward, ActionMoveBack
    };

    public override string Status =>
        $"viață: {MathF.Round(_healthTarget * TotalSegments, 1)}/{TotalSegments} | scut: {(_shieldActive ? "ACTIV" : "oprit")}";

    protected override void Load(Input input)
    {
        CameraPosition = new Vector3(0.0f, 2.5f, 6.8f);
        CameraTarget = new Vector3(0.0f, 1.5f, 0.0f);

        input.Bind(ActionDamage, Keys.J);
        input.Bind(ActionCrit, Keys.M);
        input.Bind(ActionHeal, Keys.K);
        input.Bind(ActionToggleShield, Keys.Space);
        input.Bind(ActionMoveLeft, Keys.A);
        input.Bind(ActionMoveRight, Keys.D);
        input.Bind(ActionMoveForward, Keys.W);
        input.Bind(ActionMoveBack, Keys.S);

        Mesh grid = AddMesh(Shapes.Grid(10, 1.0f, new Color4(0.28f, 0.31f, 0.36f, 1.0f)));
        Mesh barQuad = AddMesh(Shapes.Quad(3.0f, 0.42f));
        Mesh cube = AddMesh(Shapes.ShadedCube());
        Mesh sphere = AddMesh(Shapes.ShadedSphere(1.18f, 36, 20));

        AddObject("podea", grid);

        _barMat = AddMaterial("bara_viata", "procedural.vert", "healthbar.frag")
            .Param("uSegments", 1, 20, TotalSegments)
            .Param("uCornerRadius", 0.02f, 0.22f, 0.12f)
            .Param("uBorderWidth", 0.005f, 0.06f, 0.025f);
        _barMat.Transparent = true;

        var initHealth = new Vector3(_healthTarget, 0f, 0f);
        _healthTween = new Tween(initHealth, initHealth, 0.22f, Easing.EaseOut);
        _trailTween = new Tween(initHealth, initHealth, 0.45f, Easing.EaseInOut);

        _barMat.Uniforms.Set("uHealth", _healthTarget);
        _barMat.Uniforms.Set("uTrail", _healthTarget);
        _barMat.Uniforms.Set("uFlash", 0.0f);
        _barMat.Uniforms.Set("uShielded", _shieldActive);

        _shieldMat = AddMaterial("scut", "procedural.vert", "shield.frag")
            .Param("uBandCount", 3.0f, 16.0f, 8.0f);
        _shieldMat.Transparent = true;
        _shieldMat.Uniforms.Set("uShieldColor", new Vector3(0.20f, 0.85f, 1.0f));
        _shieldMat.Uniforms.Set("uShieldAlpha", 1.0f);
        _shieldMat.Uniforms.Set("uShieldFlash", 0.0f);

        var initAlpha = new Vector3(1.0f, 0f, 0f);
        _shieldAlphaTween = new Tween(initAlpha, initAlpha, 0.45f, Easing.EaseInOut);

        _hero = AddObject("personaj", cube);
        _hero.Transform.Position = _heroBasePos;
        _hero.Tint = new Color4(0.38f, 0.58f, 0.90f, 1.0f);
        _hero.Spin = new Vector3(0.0f, 22.0f, 0.0f);

        _shieldObj = AddObject("scut", sphere, _shieldMat);
        _shieldObj.Transform.Position = _hero.Transform.Position;

        _healthBar = AddObject("bara_viata", barQuad, _barMat);
        _healthBar.Transform.Position = _hero.Transform.Position + new Vector3(0.0f, 1.65f, 0.0f);
    }

    public override void Update(float dt, Input input)
    {
        base.Update(dt, input);
        _bobTime += dt;

        Vector3 move = Vector3.Zero;
        if (input.IsDown(ActionMoveLeft)) move.X -= 1.0f;
        if (input.IsDown(ActionMoveRight)) move.X += 1.0f;
        if (input.IsDown(ActionMoveForward)) move.Z -= 1.0f;
        if (input.IsDown(ActionMoveBack)) move.Z += 1.0f;

        if (move.LengthSquared > 0.0f)
        {
            move = Vector3.Normalize(move) * 3.2f * dt;
            _heroBasePos.X = Math.Clamp(_heroBasePos.X + move.X, -3.5f, 3.5f);
            _heroBasePos.Z = Math.Clamp(_heroBasePos.Z + move.Z, -3.5f, 3.5f);
        }

        float hover = 0.08f * MathF.Sin(_bobTime * 2.5f);
        _hero.Transform.Position = _heroBasePos + new Vector3(0.0f, hover, 0.0f);
        _shieldObj.Transform.Position = _hero.Transform.Position;
        _healthBar.Transform.Position = _hero.Transform.Position + new Vector3(0.0f, 1.65f, 0.0f);

        if (input.WasPressed(ActionToggleShield))
        {
            _shieldActive = !_shieldActive;
            _shieldAlphaTween.GoTo(new Vector3(_shieldActive ? 1.0f : 0.0f, 0f, 0f), 0.45f);
        }

        if (input.WasPressed(ActionDamage))
        {
            float step = 1.0f / TotalSegments;
            float damage = _shieldActive ? step * 0.5f : step;
            ApplyDamage(damage);
        }

        if (input.WasPressed(ActionCrit))
        {
            float step = 1.0f / TotalSegments;
            float damage = _shieldActive ? step * 1.5f : step * 3.0f;
            ApplyDamage(damage);
        }

        if (input.WasPressed(ActionHeal))
        {
            float step = 1.0f / TotalSegments;
            _healthTarget = Math.Clamp(_healthTarget + step, 0.0f, 1.0f);
            _healthTween.GoTo(new Vector3(_healthTarget, 0f, 0f), 0.25f);

            if (_trailTween.Value.X < _healthTarget)
                _trailTween.GoTo(new Vector3(_healthTarget, 0f, 0f), 0.25f);
        }

        _healthTween.Update(dt);

        if (_trailDelayTimer > 0.0f)
        {
            _trailDelayTimer -= dt;
            if (_trailDelayTimer <= 0.0f)
                _trailTween.GoTo(new Vector3(_healthTarget, 0f, 0f), 0.45f);
        }
        _trailTween.Update(dt);

        _shieldAlphaTween.Update(dt);

        const float flashDecay = 7.5f;
        _barFlash *= MathF.Exp(-flashDecay * dt);
        _shieldFlash *= MathF.Exp(-flashDecay * dt);

        _barMat.Uniforms.Set("uHealth", _healthTween.Value.X);
        _barMat.Uniforms.Set("uTrail", _trailTween.Value.X);
        _barMat.Uniforms.Set("uFlash", _barFlash);
        _barMat.Uniforms.Set("uShielded", _shieldActive);

        _shieldMat.Uniforms.Set("uShieldAlpha", _shieldAlphaTween.Value.X);
        _shieldMat.Uniforms.Set("uShieldFlash", _shieldFlash);
    }

    private void ApplyDamage(float damage)
    {
        _healthTarget = Math.Clamp(_healthTarget - damage, 0.0f, 1.0f);
        _healthTween.GoTo(new Vector3(_healthTarget, 0f, 0f), 0.20f);

        _trailDelayTimer = 0.35f;
        _barFlash = 1.0f;
        if (_shieldActive)
            _shieldFlash = 1.0f;
    }

    public override IEnumerable<string> Describe()
    {
        foreach (string line in base.Describe()) yield return line;
        yield return $"Viață curentă  : {MathF.Round(_healthTarget * TotalSegments, 1)}/{TotalSegments} puncte (J = lovitură, M = critică, K = vindecare)";
        yield return $"Scut hexagonal : {(_shieldActive ? "PORNIT" : "OPRIT")} (Space) | Deplasare: W, A, S, D";
    }
}