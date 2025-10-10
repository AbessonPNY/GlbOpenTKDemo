#version 330 core
in vec3 vNormal;
in vec3 vWorldPos;
in vec2 vUV;

uniform sampler2D uTex;
uniform vec3 uLightPos;
uniform vec3 uCamPos;
uniform bool uHasTex;

out vec4 FragColor;

void main()
{
    vec3 N = normalize(vNormal);
    vec3 L = normalize(uLightPos - vWorldPos);
    vec3 V = normalize(uCamPos - vWorldPos);
    vec3 H = normalize(L + V);

    float diff = max(dot(N, L), 0.0);
    float spec = pow(max(dot(N, H), 0.0), 32.0);

    vec3 base = uHasTex ? texture(uTex, vUV).rgb : vec3(0.8, 0.82, 0.85);
    vec3 color = base * (0.1 + 0.9 * diff) + vec3(1.0) * spec * 0.2;
    FragColor = vec4(color, 1.0);
}
