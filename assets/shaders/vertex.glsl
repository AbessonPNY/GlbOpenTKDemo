#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec2 aUV;
layout(location=3) in ivec4 aBoneIds;
layout(location=4) in vec4  aBoneW;

uniform mat4 uModel, uView, uProj;

layout(std140) uniform Bones {
    mat4 uBones[100];
};

out vec3 vNormal;
out vec3 vWorldPos;
out vec2 vUV;

void main() {
    mat4 skin = mat4(1.0);
    if (aBoneW.x + aBoneW.y + aBoneW.z + aBoneW.w > 0.0)
    {
        skin =
            aBoneW.x * uBones[aBoneIds.x] +
            aBoneW.y * uBones[aBoneIds.y] +
            aBoneW.z * uBones[aBoneIds.z] +
            aBoneW.w * uBones[aBoneIds.w];
    }

    vec4 localPos = skin * vec4(aPos, 1.0);
    vec3 localNrm = mat3(skin) * aNormal;

    vec4 worldPos = uModel * localPos;
    vWorldPos = worldPos.xyz;
    vNormal = mat3(transpose(inverse(uModel))) * localNrm;
    vUV = aUV;

    gl_Position = uProj * uView * worldPos;
}
