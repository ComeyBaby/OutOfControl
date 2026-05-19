shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear_mipmap;
uniform float blur_lod : hint_range(0.0, 6.0, 0.1) = 2.2;
uniform float dim_strength : hint_range(0.0, 1.0, 0.01) = 0.45;

void fragment() {
	vec3 blurred = textureLod(screen_texture, SCREEN_UV, blur_lod).rgb;
	vec3 dimmed = blurred * (1.0 - dim_strength);
	COLOR = vec4(dimmed, 1.0);
}
