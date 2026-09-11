shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;

varying vec3 world_position;
varying vec3 world_normal;

void vertex() {
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = MODEL_NORMAL_MATRIX * NORMAL;
}

void fragment() {
    float depth = COLOR.a;
    // A dry cell must never acquire opacity from the fresnel/specular terms.
    if (depth <= 0.001) discard;
    vec3 n = normalize(world_normal);
    vec3 v = normalize(CAMERA_POSITION_WORLD - world_position);
    vec3 l = normalize(vec3(-0.35, 0.72, -0.55));
    vec2 p = vec2(world_position.x, -world_position.z);
    float amp = 0.07 * clamp(depth * 2.0, 0.0, 1.0);
    n.x += (sin(p.x * 1.7 + TIME * 1.3) + sin((p.x + p.y) * 2.3 - TIME * 1.7) * 0.6) * amp;
    n.z -= (cos(p.y * 1.9 - TIME * 1.1) + sin((p.x - p.y) * 2.1 + TIME * 1.5) * 0.6) * amp;
    n = normalize(n);
    float diffuse = clamp(dot(n, l), 0.0, 1.0) * 0.4 + 0.6;
    float fresnel = pow(1.0 - clamp(dot(n, v), 0.0, 1.0), 5.0);
    float spec = pow(clamp(dot(n, normalize(l + v)), 0.0, 1.0), 220.0);
    float foam = (1.0 - smoothstep(0.04, 0.22, depth)) * step(0.015, depth);
    vec3 col = mix(COLOR.rgb * diffuse, vec3(0.05, 0.28, 0.55), fresnel * 0.6);
    col += spec * vec3(1.0, 0.72, 0.42) * 2.5;
    ALBEDO = mix(col, vec3(0.85, 0.90, 0.90), foam * 0.7);
    ALPHA = clamp(depth + fresnel * 0.25 + spec + foam * 0.6, 0.0, 1.0);
}
