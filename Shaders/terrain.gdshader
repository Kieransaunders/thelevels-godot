shader_type spatial;
render_mode unshaded;

varying vec3 world_normal;

void vertex() {
    world_normal = MODEL_NORMAL_MATRIX * NORMAL;
}

void fragment() {
    // Source palette and fixed diffuse ramp, with Z reflected at the visual boundary.
    vec3 light_direction = normalize(vec3(-0.45, 0.72, 0.35));
    float diffuse = clamp(dot(normalize(world_normal), light_direction), 0.0, 1.0) * 0.46 + 0.54;
    ALBEDO = COLOR.rgb * diffuse;
}
