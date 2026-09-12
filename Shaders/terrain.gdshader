shader_type spatial;

void fragment() {
	// Vertex colour palette; the scene's directional light and shadows do the shading.
	ALBEDO = COLOR.rgb;
	ROUGHNESS = 0.9;
	SPECULAR = 0.05;
}
