#version 330 core

in vec3 vWorldPos;
in vec3 vNormal;
in vec2 vUV;

out vec4 FragColor;

uniform vec3  uKdColor = vec3(0.8, 0.8, 0.8);
uniform int   uHasTex  = 0;
uniform sampler2D uTex;

uniform vec3 uLightPos = vec3(3,5,2);
uniform vec3 uCamPos   = vec3(0,0,5);


uniform float uSpecStrength = 0.1;
uniform float uSpecPower    = 16.0;

void main()
{
    vec3 N = normalize(vNormal);
    vec3 L = normalize(uLightPos - vWorldPos);
    vec3 V = normalize(uCamPos   - vWorldPos);


    float ndl = max(dot(N, L), 0.0);


    vec3 H = normalize(L + V);
    float spec = pow(max(dot(N, H), 0.0), uSpecPower) * uSpecStrength;

    vec3 base = (uHasTex == 1) ? texture(uTex, vUV).rgb : uKdColor;

    vec3 color = base * (0.08 + 0.92 * ndl) + spec;
    FragColor = vec4(color, 1.0);
}
