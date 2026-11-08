shader_type canvas_item;
render_mode unshaded;

uniform vec4 water_tint : source_color = vec4(0.56, 0.70, 0.69, 1.0);
uniform float water_mix : hint_range(0.0, 1.0) = 0.30;
uniform float opacity : hint_range(0.0, 1.0) = 0.78;
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
	COLOR = vec4(submerged, soft_alpha * opacity) * vertex_modulation;
}
