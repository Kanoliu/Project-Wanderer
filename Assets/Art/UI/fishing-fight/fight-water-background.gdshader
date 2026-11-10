shader_type canvas_item;
render_mode unshaded;

uniform float panel_opacity : hint_range(0.0, 1.0) = 0.96;
uniform vec4 backing_color : source_color = vec4(0.114, 0.125, 0.118, 1.0);
varying vec4 vertex_modulation;

void vertex() {
	vertex_modulation = COLOR;
}

void fragment() {
	vec4 water = texture(TEXTURE, UV);
	// Preserve the painted texture's translucent detail over a stable dark base.
	// Apply panel transparency once, after the internal water composition.
	vec3 surface = mix(backing_color.rgb, water.rgb, water.a);
	COLOR = vec4(surface, panel_opacity) * vertex_modulation;
}
