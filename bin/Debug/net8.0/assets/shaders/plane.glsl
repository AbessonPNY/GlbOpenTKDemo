#version 330 core
layout(location = 0) in vec3 aPos;

uniform mat4 uView;
uniform mat4 uProj;

uniform vec4 uColor;

out vec4 vColor;

void main()
{
    vColor = uColor;
    gl_Position = uProj * uView * vec4(aPos, 1.0);
}
