#version 330 core

in vec3 vColor;
in vec2 vUV;

uniform bool uShowUV;
uniform float uTime;
uniform float uShieldAlpha;
uniform float uShieldFlash;
uniform float uBandCount;
uniform vec3 uShieldColor;

out vec4 FragColor;

// Distanta pana la marginea unei celule hexagonale
float hexDist(vec2 p)
{
    p = abs(p);
    return max(dot(p, normalize(vec2(1.0, 1.73205))), p.x);
}

vec4 hexCoords(vec2 uv)
{
    vec2 r = vec2(1.0, 1.73205);
    vec2 h = r * 0.5;
    vec2 a = mod(uv, r) - h;
    vec2 b = mod(uv - h, r) - h;
    vec2 gv = dot(a, a) < dot(b, b) ? a : b;
    float d = 0.5 - hexDist(gv);
    return vec4(gv.x, gv.y, d, 0.0);
}

void main()
{
    if (uShowUV)
    {
        FragColor = vec4(vUV, 0.0, 1.0);
        return;
    }

    if (uShieldAlpha <= 0.001)
        discard;

    // Grila hexagonala scalata pe sfera
    vec2 uvScaled = vec2(vUV.x * uBandCount * 2.0, vUV.y * uBandCount);
    vec4 hc = hexCoords(uvScaled);

    // Liniile fagurelui
    float hexLine = 1.0 - smoothstep(0.03, 0.08, hc.z);

    // Unda de energie care urca pe scut
    float wave = 0.5 + 0.5 * sin(6.2831853 * (vUV.y * 2.5 - uTime * 0.5));
    float pulse = 0.45 + 0.55 * wave;

    // Unda de soc la impactul loviturii (cand uShieldFlash > 0)
    float impactWave = sin((vUV.y - (1.0 - uShieldFlash)) * 18.0);
    float impactRing = smoothstep(0.5, 1.0, impactWave) * uShieldFlash;

    vec3 col = mix(uShieldColor * 0.5, uShieldColor * 1.5, hexLine * pulse);
    col += vec3(0.5, 0.9, 1.0) * impactRing;
    col = mix(col, vec3(1.0), clamp(uShieldFlash, 0.0, 1.0) * 0.65);

    float alpha = (0.10 + 0.48 * hexLine * pulse + 0.35 * uShieldFlash) * uShieldAlpha;
    FragColor = vec4(col * vColor, clamp(alpha, 0.0, 0.95));
}