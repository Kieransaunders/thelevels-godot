// Stylized water over the sim-driven heightfield. Technique after the gameidea.org
// 2026-02 stylized-water writeup (depth-texture thickness, Beer-Lambert absorption,
// screen-space refraction with a depth-mask ghost fix); code written for this project.
// The C# solver owns the surface; this shader only shades it.
// Contract: COLOR.a is the dry/wet gate fed by HeightfieldView — dry cells are exactly
// 0 (--verify-p3 asserts it), so the discard below must not weaken. COLOR.r is the
// flow-whitewater drive (normalized FlowSpeed, P6); COLOR.g/b are unused here.
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
uniform float foam_noise_scale = 1.6;
uniform float foam_noise_speed : hint_range(0.0, 2.0) = 0.35;
uniform float crest_amount : hint_range(0.0, 1.0) = 0.35;
// Shoreline bands are measured in metres along the ground, not in metres of depth.
uniform float foam_width : hint_range(0.05, 3.0) = 0.55;    // foam belt width, m
uniform float foam_wobble : hint_range(0.0, 2.0) = 0.45;    // lateral wobble of the waterline, m
uniform float edge_feather : hint_range(0.02, 1.5) = 0.30;  // fade to nothing before the mesh edge, m

// Cel mode: the Wind Waker hard-band ramp, off by default. Colours stay dialable rather
// than hardcoded so they can be pulled toward the Somerset palette.
uniform bool cel_shade = false;
uniform vec3 cel_deep : source_color = vec3(0.12, 0.25, 0.60);
uniform vec3 cel_mid : source_color = vec3(0.22, 0.42, 0.76);
uniform vec3 cel_bright : source_color = vec3(0.38, 0.58, 0.90);
uniform vec3 cel_highlight : source_color = vec3(0.58, 0.74, 0.96);

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
    // Fast flow (whitewater drive) roughens the chop locally.
    vec2 p = vec2(world_position.x, -world_position.z);
    float t = TIME * wave_speed;
    float flow = COLOR.r;
    float e = 0.06 + dist * 0.0015;
    float h = wave_height(p, t);
    float hx = wave_height(p + vec2(e, 0.0), t);
    float hz = wave_height(p + vec2(0.0, e), t);
    float calm = 1.0 - smoothstep(60.0, 140.0, dist);
    float ripple = clamp(COLOR.a * 2.0, 0.0, 1.0) * clamp(thickness * 5.0, 0.0, 1.0);
    vec3 n = normalize(vec3(-(hx - h) / e * wave_strength * calm * ripple * (1.0 + flow * 1.8), 1.0,
                            -(hz - h) / e * wave_strength * calm * ripple * (1.0 + flow * 1.8)));

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

    vec3 col;
    if (cel_shade) {
        // Bands ride the world-space wave sum, fresnel and the specular lobe only.
        // Driving them from `thickness` would re-print the grid: thickness comes from
        // the depth buffer, which renders the 0.75 m terrain mesh, so a step() on it
        // is a step() on the cells. The reference shader gets away with hard bands
        // because it shades an infinite smooth plane with no shoreline at all.
        float band = clamp(h * 0.9 + fresnel * 0.35 + spec * 0.25, 0.0, 1.0);
        col = cel_deep;
        col = mix(col, cel_mid, step(0.30, band));
        col = mix(col, cel_bright, step(0.52, band));
        col = mix(col, cel_highlight, step(0.72, band));
    } else {
        col = screen_col * transmittance * 0.7 + body;
        col *= diffuse;
        col = mix(col, sky_reflect, fresnel * 0.55);
        col += spec * vec3(1.0, 0.88, 0.70) * spec_strength;
    }

    // Distance from this pixel to the waterline, in metres along the ground: thickness
    // divided by its own world-space gradient. A depth threshold cannot do this job —
    // the same 0.4 m of depth is a few centimetres of shore against a ditch wall and
    // many metres across a silted margin, which is why the old fixed band ringed
    // shallow pools with a white crust and left steep banks bare. Measuring laterally
    // makes one belt width correct everywhere and retires the dFdx "wall" special case.
    vec3 wp_dx = dFdx(world_position), wp_dy = dFdy(world_position);
    vec2 shore_grad = vec2(dFdx(thickness) / max(length(wp_dx.xz), 1e-4),
                           dFdy(thickness) / max(length(wp_dy.xz), 1e-4));
    // The gradient floor is a real limit, not epsilon-avoidance: thickness comes from
    // the depth buffer, which renders the un-subdivided 129² terrain, so its gradient
    // is piecewise-constant and jumps at every terrain triangle edge. An unbounded
    // ratio turns those jumps into visible herringbone facets across the shallows.
    // 0.2 caps the band at 5x the local depth — flat silted margins then behave like a
    // plain depth band, which is the right failure mode.
    float shore_dist = clamp(thickness / max(length(shore_grad), 0.2), 0.0, 8.0);

    // The waterline meanders on a domain-warped noise contour rather than tracing the
    // mesh: `lap` warps the sample point itself, so the line curves instead of holding
    // a fixed offset from the coast.
    vec2 lap = vec2(fbm(p * 0.8 + vec2(t * 0.20, 0.0)), fbm(p * 0.8 + vec2(4.7 - t * 0.17, 2.3))) - 0.5;
    float fn = fbm((p + lap * 1.3) * foam_noise_scale + vec2(t * foam_noise_speed, -t * foam_noise_speed * 0.7));
    float shore = shore_dist + (fn - 0.5) * foam_wobble;

    // The mesh silhouette is a stair of 0.375 m sub-grid edges, so nothing may be opaque
    // on it. Every waterline term is multiplied by this feather and the water reaches
    // zero alpha a few centimetres *inside* the wet area — never on the polygon edge.
    // The previous `max(alpha, contact * 0.95)` did the opposite: it forced near-opaque
    // white foam exactly at thickness 0, printing the staircase in the brightest colour
    // on screen.
    // COLOR.a is the only authoritative wet mask. `shore_dist` alone cannot retire the
    // skirt of quads around each pool: HeightfieldView lifts every water vertex by at
    // least 0.015 m, so dry fringe quads carry a constant-thickness film whose gradient
    // is ~0, which reads as "far from any shore" and would draw them at full alpha as
    // hard cell-sized rectangles. The vertex gate is bilinear across the sub-grid, so
    // feathering on it fades that skirt out smoothly instead of clipping it to a stair.
    float wet = smoothstep(0.0, 0.35, COLOR.a);
    float edge = smoothstep(0.0, edge_feather, shore) * wet;

    float shore_foam = (1.0 - smoothstep(0.05, foam_width, shore)) * smoothstep(0.22, 0.55, fn);
    float crest = smoothstep(0.55, 0.80, h) * calm;
    float rapid = smoothstep(0.18, 0.85, flow);
    float foam = clamp(shore_foam + crest * crest_amount * step(0.06, thickness)
        + rapid * (0.30 + 0.70 * fn), 0.0, 1.0) * edge;
    col = mix(col, foam_color * (diffuse * 0.5 + 0.5), foam);
    col = mix(col, foam_color * (diffuse * 0.5 + 0.5), rapid * 0.22 * smoothstep(0.02, 0.1, thickness) * edge);

    float absorb = clamp(1.0 - (transmittance.r + transmittance.g + transmittance.b) / 3.0, 0.0, 1.0);
    float alpha = clamp(0.30 + absorb * 0.7 + fresnel * 0.15, 0.0, 1.0);
    alpha = max(alpha, foam * 0.9) * edge;
    ALBEDO = col;
    ALPHA = alpha;
}
