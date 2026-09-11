// Minimal unlit colour shader for cursor rings, pointer flashes and HUD-space fx.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_always, cull_disabled;

uniform vec4 albedo : source_color = vec4(1.0);

void fragment() {
	ALBEDO = albedo.rgb;
	ALPHA = albedo.a;
}
