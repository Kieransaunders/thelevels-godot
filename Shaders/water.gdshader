// Stylized water over the sim-driven heightfield. Technique after the gameidea.org
// 2026-02 stylized-water writeup (depth-texture thickness, Beer-Lambert absorption,
// screen-space refraction with a depth-mask ghost fix); code written for this project.
// The C# solver owns the surface; this shader only shades it.
// Contract: COLOR.a is the dry/wet gate fed by HeightfieldView — dry cells are exactly
// 0 (--verify-p3 asserts it), so the discard below must not weaken.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;

uniform sampler2D screen_sampler : hint_screen_texture, filter_linear;
uniform sampler2D depth_sampler : hint_depth_texture, filter_linear;

uniform vec3 light_direction = vec3(-0.35, 0.72, -0.55); // set from MorningSun in Main.cs
uniform vec3 shallow_color : source_color = vec3(0.35, 0.50, 0.52);
uniform vec3 deep_color : source_color = vec3(0.04, 0.16, 0.28);
uniform vec3 absorption = vec3(2.6, 1.2, 0.8); // per-channel extinction, 1/m: red dies first
uniform vec3 sky_reflect : source_color = vec3(0.60, 0.68, 0.70);
uniform vec3 foam_color : source_color = vec3(0.80, 0.85, 0.83);

uniform float refraction_strength : hint_range(0.0, 0.2) = 0.05;
uniform float refraction_distance_fade : hint_range(0.0, 0.1) = 0.008;
uniform float wave_strength : hint_range(0.0, 0.5) = 0.16;
uniform float wave_scale = 0.9;
uniform float wave_speed : hint_range(0.0, 3.0) = 1.0;
uniform float spec_strength : hint_range(0.0, 1.0) = 0.22;
uniform float foam_sharpness : hint_range(0.01, 0.3) = 0.09;
uniform float foam_noise_scale = 2.5;
uniform float foam_noise_speed : hint_range(0.0, 2.0) = 0.35;
uniform float crest_amount : hint_range(0.0, 1.0) = 0.35;

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

float fbm(vec2 p) {
    return vnoise(p) * 0.65 + vnoise(p * 2.13 + 7.7) * 0.35;
}

float wave_height(vec2 p, float t) {
    // Two octaves scrolling in different directions read as chop, not as a marching sine.
    float a = fbm(p * wave_scale + vec2(t * 0.80, t * 0.55));
    float b = fbm(p * wave_scale * 1.9 - vec2(t * 0.60, t * 0.75));
    return a * 0.7 + b * 0.3;
}

// Unprojects a raw depth sample to world space. Forward+ NDC z is [0, 1].
// Engine built-ins are not visible in user functions, so they arrive as parameters.
vec3 depth_to_world(vec2 uv, float raw_depth, mat4 inv_projection, mat4 inv_view) {
    vec4 view_pos = inv_projection * vec4(uv * 2.0 - 1.0, raw_depth, 1.0);
    view_pos.xyz /= view_pos.w;
    return (inv_view * vec4(view_pos.xyz, 1.0)).xyz;
}

void vertex() {
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    // A dry cell must never acquire opacity from the fresnel/specular terms.
    if (COLOR.a <= 0.001) discard;

    vec3 to_camera = CAMERA_POSITION_WORLD - world_position;
    float dist = length(to_camera);
    vec3 v = to_camera / dist;

    // True per-pixel thickness: the water surface minus whatever opaque geometry is
    // under it. Finer than the 0.75 m cells the vertex colours can express.
    float thickness = world_position.y
        - depth_to_world(SCREEN_UV, texture(depth_sampler, SCREEN_UV).r, INV_PROJECTION_MATRIX, INV_VIEW_MATRIX).y;
    thickness = clamp(thickness, 0.0, 2.5);

    // Detail normals: two scrolling octaves, finite differences with an epsilon that
    // grows with distance so speculars stay stable; the surface calms far away.
    vec2 p = vec2(world_position.x, -world_position.z);
    float t = TIME * wave_speed;
    float e = 0.06 + dist * 0.0015;
    float h = wave_height(p, t);
    float hx = wave_height(p + vec2(e, 0.0), t);
    float hz = wave_height(p + vec2(0.0, e), t);
    float calm = 1.0 - smoothstep(60.0, 140.0, dist);
    float ripple = clamp(COLOR.a * 2.0, 0.0, 1.0) * clamp(thickness * 5.0, 0.0, 1.0);
    vec3 n = normalize(vec3(-(hx - h) / e * wave_strength * calm * ripple, 1.0,
                            -(hz - h) / e * wave_strength * calm * ripple));

    vec3 l = normalize(light_direction);
    float diffuse = clamp(dot(n, l), 0.0, 1.0) * 0.4 + 0.6;
    float fresnel = pow(1.0 - clamp(dot(n, v), 0.0, 1.0), 5.0);
    // ponytail: a 220-power lobe over ripple normals aliases into a white maze at this
    // cell size; the broad 24-power lobe reads as sheen. Only retighten on capture proof.
    float spec = pow(clamp(dot(n, normalize(l + v)), 0.0, 1.0), 24.0);

    // Beer-Lambert: the refracted floor dies off per channel while backscatter takes over.
    vec3 transmittance = exp(-absorption * thickness);
    vec3 body = shallow_color * transmittance + deep_color * (1.0 - transmittance);

    // Refraction: offset screen UVs by the ripple normal, faded with distance, and
    // reverted when the offset lands on geometry above the surface (hills behind the
    // water must not smear into the shallows).
    vec2 ref_uv = SCREEN_UV + n.xz * refraction_strength * clamp(thickness, 0.0, 1.0)
        * exp(-dist * refraction_distance_fade);
    if (depth_to_world(ref_uv, texture(depth_sampler, ref_uv).r, INV_PROJECTION_MATRIX, INV_VIEW_MATRIX).y
        > world_position.y + 0.01)
        ref_uv = SCREEN_UV;
    vec3 screen_col = texture(screen_sampler, ref_uv).rgb;

    vec3 col = screen_col * transmittance * 0.7 + body;
    col *= diffuse;
    col = mix(col, sky_reflect, fresnel * 0.55);
    col += spec * vec3(1.0, 0.88, 0.70) * spec_strength;

    // Foam: a continuous bright waterline where the terrain meets the surface, a
    // noise-broken belt just inside it, plus crests on the tallest ripple tops.
    float fn = fbm(p * foam_noise_scale + vec2(t * foam_noise_speed, -t * foam_noise_speed * 0.7));
    float edge_line = 1.0 - smoothstep(0.0, 0.02, thickness);
    float belt = (1.0 - smoothstep(0.01, foam_sharpness, thickness)) * smoothstep(0.35, 0.75, fn);
    float crest = smoothstep(0.55, 0.80, h) * calm;
    float foam = clamp(edge_line * 0.6 + belt + crest * crest_amount * step(0.06, thickness), 0.0, 1.0);
    col = mix(col, foam_color * (diffuse * 0.5 + 0.5), foam);

    float absorb = clamp(1.0 - (transmittance.r + transmittance.g + transmittance.b) / 3.0, 0.0, 1.0);
    float alpha = clamp(0.30 + absorb * 0.7 + fresnel * 0.15, 0.0, 1.0);
    alpha *= smoothstep(0.0, 0.04, thickness); // soft shoreline: the film fades onto the mud
    alpha = max(alpha, foam * 0.85);
    ALBEDO = col;
    ALPHA = alpha;
}
