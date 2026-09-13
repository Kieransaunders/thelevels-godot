shader_type spatial;

// Vertex colour palette; the scene's directional light and shadows do the shading.
// Contract (P6): COLOR.rgb is the terrain palette fed by HeightfieldView; COLOR.a is
// normalized water depth (0 on dry land) and drives shallow-water caustics only —
// the shader stays fully opaque, alpha never reaches blending.

uniform vec3 caustic_color : source_color = vec3(1.0, 0.94, 0.76);
uniform float caustic_strength : hint_range(0.0, 1.0) = 0.22;
uniform float caustic_scale = 0.5;
uniform float caustic_speed : hint_range(0.0, 2.0) = 0.45;

varying vec3 world_position;

float hash12(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

float vnoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash12(i), hash12(i + vec2(1.0, 0.0)), u.x),
               mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), u.x), u.y);
}

void vertex() {
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	ALBEDO = COLOR.rgb;
	ROUGHNESS = 0.9;
	SPECULAR = 0.05;

	// Caustic light focusing on the floor of shallow water: two noise sheets sliding
	// across each other, sharpened, faded out with depth (deep water absorbs them)
	// and at the waterline. EMISSION keeps it a light effect rather than a paint layer.
	if (COLOR.a > 0.003) {
		vec2 p = world_position.xz;
		float t = TIME * caustic_speed;
		float a = vnoise(p * caustic_scale + vec2(t * 0.8, t * 0.5));
		float b = vnoise(p * caustic_scale * 1.27 - vec2(t * 0.55, t * 0.95));
		float caustic = pow(clamp(a * b * 2.4, 0.0, 1.0), 2.0);
		float shallow = smoothstep(0.03, 0.10, COLOR.a) * (1.0 - smoothstep(0.30, 0.80, COLOR.a));
		EMISSION = caustic_color * caustic * shallow * caustic_strength;
	}
}
