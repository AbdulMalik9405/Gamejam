// Fades the outer edge of the logo image into the backdrop, so the square picture has no visible border.
shader_type canvas_item;

uniform float edge : hint_range(0.0, 0.5) = 0.06;

void fragment() {
	vec2 inside = min(UV, 1.0 - UV);
	COLOR.a *= smoothstep(0.0, edge, inside.x) * smoothstep(0.0, edge, inside.y);
}
