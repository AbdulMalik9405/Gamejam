// Paint or plaster on stone. Lit by the scene like any other surface.
// wear > 0 flakes patches off, grime > 0 stains it, edge_chip > 0 breaks the edges of a panel.
shader_type spatial;

uniform vec4 paint_color : source_color = vec4(0.85, 0.78, 0.62, 1.0);
uniform vec4 grime_color : source_color = vec4(0.22, 0.17, 0.12, 1.0);
uniform sampler2D noise : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform sampler2D bump : hint_normal, filter_linear_mipmap, repeat_enable;
uniform float noise_scale = 1.6;
uniform float wear : hint_range(0.0, 1.0) = 0.0;
uniform float grime : hint_range(0.0, 1.0) = 0.0;
uniform float bumpiness : hint_range(0.0, 2.0) = 0.3;
uniform float roughness_value : hint_range(0.0, 1.0) = 0.9;
// Half the panel's width and height, in metres. Only used when edge_chip > 0.
uniform vec2 half_size = vec2(0.6, 0.3);
uniform float edge_chip = 0.0;

varying vec3 local_pos;

void vertex() {
	local_pos = VERTEX;
}

void fragment() {
	vec2 p = local_pos.xy * noise_scale;
	float coarse = texture(noise, p).r;
	float fine = texture(noise, p * 7.0 + vec2(0.37, 0.71)).r;
	float n = coarse * 0.65 + fine * 0.35;

	if (n < wear)
		discard;
	if (edge_chip > 0.0) {
		vec2 inside = half_size - abs(local_pos.xy);
		if (min(inside.x, inside.y) < edge_chip * (1.0 - coarse) * 1.6)
			discard;
	}

	// Darker where it's about to flake, plus broad water stains.
	float near_flake = 1.0 - smoothstep(wear, wear + 0.08, n);
	float stain = smoothstep(0.5, 0.85, 1.0 - (coarse * 0.5 + fine * 0.5)) * grime * 0.7;
	vec3 col = mix(paint_color.rgb, grime_color.rgb, clamp(stain + near_flake * grime * 0.5, 0.0, 1.0));
	col *= mix(1.0, 0.85 + fine * 0.3, grime);

	ALBEDO = col;
	ROUGHNESS = roughness_value;
	NORMAL_MAP = texture(bump, p * 3.0).rgb;
	NORMAL_MAP_DEPTH = bumpiness;
}
