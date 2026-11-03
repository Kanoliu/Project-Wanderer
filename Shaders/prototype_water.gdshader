shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

uniform vec3 deep_color : source_color = vec3(0.055, 0.20, 0.23);
uniform vec3 shallow_color : source_color = vec3(0.25, 0.48, 0.49);
uniform vec3 crest_color : source_color = vec3(0.86, 0.80, 0.60);
uniform float wave_height : hint_range(0.0, 0.5) = 0.18;
uniform float wave_speed : hint_range(0.0, 3.0) = 0.85;

varying float wave_value;

float waves(vec2 point, float time) {
	float long_wave = sin(point.x * 0.72 + point.y * 0.34 + time * wave_speed);
	float cross_wave = sin(point.x * -1.08 + point.y * 0.57 - time * wave_speed * 1.37);
	float ripple = sin(point.x * 2.15 + point.y * 1.63 + time * wave_speed * 2.2);
	return long_wave * 0.52 + cross_wave * 0.31 + ripple * 0.17;
}

void vertex() {
	float time = TIME;
	wave_value = waves(VERTEX.xz, time);
	VERTEX.y += wave_value * wave_height;
	float sample_step = 0.08;
	float height_x = waves(VERTEX.xz + vec2(sample_step, 0.0), time) * wave_height;
	float height_z = waves(VERTEX.xz + vec2(0.0, sample_step), time) * wave_height;
	NORMAL = normalize(vec3(
		-(height_x - wave_value * wave_height) / sample_step,
		1.0,
		-(height_z - wave_value * wave_height) / sample_step
	));
}

void fragment() {
	float fresnel = pow(1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0), 3.0);
	float moving_glint = pow(max(0.0, sin((VERTEX.x - VERTEX.z) * 4.0 + TIME * 1.8)), 14.0);
	float crest = smoothstep(0.48, 0.95, wave_value) * 0.14 + moving_glint * 0.045;
	ALBEDO = mix(shallow_color, deep_color, 0.48 + fresnel * 0.44) + crest_color * crest;
	ROUGHNESS = mix(0.28, 0.44, fresnel);
	METALLIC = 0.03;
	SPECULAR = 0.58;
}
