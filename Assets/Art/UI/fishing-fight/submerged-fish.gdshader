shader_type canvas_item;
render_mode unshaded;

uniform vec4 water_tint : source_color = vec4(0.56, 0.70, 0.69, 1.0);
uniform float water_mix : hint_range(0.0, 1.0) = 0.30;
uniform float opacity : hint_range(0.0, 1.0) = 0.78;
uniform vec4 edge_color : source_color = vec4(0.114, 0.125, 0.118, 1.0);
uniform float edge_strength : hint_range(0.0, 1.0) = 0.85;
varying vec4 vertex_modulation;

void vertex() {
	vertex_modulation = COLOR;
}

void fragment() {
	// Geometry is skinned by Skeleton2D; the shader only supplies water depth.
	vec4 center = texture(TEXTURE, UV);
	vec2 spread = vec2(0.005, 0.012);
	float soft_alpha = (center.a * 4.0
		+ texture(TEXTURE, UV + vec2(spread.x, 0.0)).a
		+ texture(TEXTURE, UV - vec2(spread.x, 0.0)).a
		+ texture(TEXTURE, UV + vec2(0.0, spread.y)).a
		+ texture(TEXTURE, UV - vec2(0.0, spread.y)).a) / 8.0;
	vec3 submerged = mix(center.rgb, water_tint.rgb, water_mix);
	// A thin inner edge keeps the ivory fish readable through the glass panel.
	vec2 edge_step = max(fwidth(UV), TEXTURE_PIXEL_SIZE);
	float neighbor_alpha = min(min(
		texture(TEXTURE, UV + vec2(edge_step.x, 0.0)).a,
		texture(TEXTURE, UV - vec2(edge_step.x, 0.0)).a), min(
		texture(TEXTURE, UV + vec2(0.0, edge_step.y)).a,
		texture(TEXTURE, UV - vec2(0.0, edge_step.y)).a));
	float edge = clamp((center.a - neighbor_alpha) * 2.0, 0.0, 1.0);
	submerged = mix(submerged, edge_color.rgb, edge * edge_strength);
	COLOR = vec4(submerged, soft_alpha * opacity) * vertex_modulation;
}
