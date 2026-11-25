#version 330 core

in vec3 vColor;
in vec2 vUV;

uniform bool uShowUV;
uniform float uTime;
uniform float uHealth;
uniform float uTrail;
uniform float uFlash;
uniform bool uShielded;
uniform int uSegments;
uniform float uCornerRadius;
uniform float uBorderWidth;

out vec4 FragColor;

float sdRoundedBox(vec2 p, vec2 b, float r)
{
    vec2 q = abs(p) - b + r;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
}

void main()
{
    if (uShowUV)
    {
        FragColor = vec4(vUV, 0.0, 1.0);
        return;
    }

    vec2 aspect = vec2(7.1, 1.0);
    vec2 p = (vUV - 0.5) * aspect;
    vec2 halfSize = aspect * 0.47;

    float d = sdRoundedBox(p, halfSize, uCornerRadius);
    float aa = fwidth(d);

    float alpha = 1.0 - smoothstep(-aa, aa, d);
    if (alpha <= 0.001)
        discard;

    float innerMask = 1.0 - smoothstep(-uBorderWidth - aa, -uBorderWidth + aa, d);

    vec2 innerHalf = halfSize - vec2(uBorderWidth);
    float xNorm = clamp((p.x + innerHalf.x) / (2.0 * innerHalf.x), 0.0, 1.0);

    vec3 colLow  = vec3(0.95, 0.22, 0.42);
    vec3 colMid  = vec3(0.55, 0.35, 0.95);
    vec3 colHigh = vec3(0.15, 0.85, 0.95);

    vec3 healthCol = mix(colLow, colMid, smoothstep(0.15, 0.50, uHealth));
    healthCol = mix(healthCol, colHigh, smoothstep(0.50, 0.85, uHealth));

    // Reflex luminos care traverseaza bara plina
    float sweep = smoothstep(0.85, 1.0, sin((xNorm * 3.0 - uTime * 1.2) * 3.14159));
    healthCol += vec3(0.22) * sweep;

    // Luciu vertical 3D pe fiecare segment
    float shade = 0.78 + 0.30 * (1.0 - vUV.y);
    healthCol *= shade;

    // Sub 25% bara pulseaza alert
    if (uHealth > 0.001 && uHealth < 0.25)
    {
        float pulse = 0.60 + 0.40 * sin(uTime * 12.0);
        healthCol *= pulse;
    }

    float fillMask  = 1.0 - smoothstep(uHealth - 0.004, uHealth + 0.004, xNorm);
    float trailMask = 1.0 - smoothstep(uTrail - 0.004, uTrail + 0.004, xNorm);

    vec3 bgCol    = vec3(0.08, 0.09, 0.12);
    vec3 trailCol = vec3(0.88, 0.80, 0.98);

    vec3 innerCol = mix(bgCol, trailCol, trailMask);
    innerCol = mix(innerCol, healthCol, fillMask);

    if (uSegments > 1)
    {
        float segPos = fract(xNorm * float(uSegments));
        float distToSep = min(segPos, 1.0 - segPos);
        float sepMask = smoothstep(0.015, 0.035, distToSep);
        innerCol = mix(bgCol * 0.6, innerCol, sepMask);
    }

    innerCol = mix(innerCol, vec3(1.0), clamp(uFlash, 0.0, 1.0) * 0.85);

    // Rama capata o nuanta cyan stralucitoare cand scutul este activ
    vec3 borderNormal = vec3(0.72, 0.76, 0.84);
    vec3 borderShield = vec3(0.25, 0.88, 1.00);
    vec3 borderCol = uShielded ? borderShield : borderNormal;

    vec3 finalCol = mix(borderCol, innerCol, innerMask);
    FragColor = vec4(finalCol, alpha);
}