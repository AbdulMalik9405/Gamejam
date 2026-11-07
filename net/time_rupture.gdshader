// Full-screen "time rupture" for net/Intro.cs: rippling distortion, colour split and a cold tint,
// plus a white flash that hides the cut to the players' own cameras.
shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float strength : hint_range(0.0, 1.0) = 0.0;
uniform float flash : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	vec2 uv = SCREEN_UV;
	vec2 from_center = uv - 0.5;
	float dist = length(from_center);
	vec2 dir = dist > 0.0001 ? from_center / dist : vec2(0.0);

	// Rings rushing outward, strongest in the middle of the screen.
	float ripple = sin(dist * 38.0 - TIME * 14.0) * 0.014 * strength * (1.2 - dist);
	uv += dir * ripple;

	float split = 0.014 * strength;
	vec3 col;
	col.r = texture(screen_tex, uv + dir * split).r;
	col.g = texture(screen_tex, uv).g;
	col.b = texture(screen_tex, uv - dir * split).b;

	col = mix(col, col * vec3(0.7, 0.95, 1.35), strength * 0.55);
	col = mix(col, vec3(1.0), flash);
	COLOR = vec4(col, 1.0);
}
