// Uma peça do paper doll dentro do bake (sempre desenhada 1:1, alinhada ao pixel).
// TEXTURE (shade): luz e volume em tons de cinza, silhueta no alpha.
// mask_tex: regiões pintáveis. Peso de cada região = canal × alpha da máscara.
//           A região "base" recebe o que sobra: 1 - (R + G + B).
// decal_tex: camada de decalques da peça (DecalRasterizer). Entra no albedo ANTES da luz.
// As três texturas têm o mesmo layout de sprite sheet, então o mesmo UV serve para todas.
shader_type canvas_item;

// Dados, não cores: sem source_color. O bake é 1:1, então nearest é exato para qualquer estilo de arte.
uniform sampler2D mask_tex : filter_nearest;
uniform sampler2D decal_tex : filter_nearest;

uniform vec4 base_color : source_color = vec4(1.0);
uniform vec4 color_r : source_color = vec4(1.0);
uniform vec4 color_g : source_color = vec4(1.0);
uniform vec4 color_b : source_color = vec4(1.0);

void fragment() {
	vec4 shade = texture(TEXTURE, UV);
	vec4 mask = texture(mask_tex, UV);
	vec4 decal = texture(decal_tex, UV);

	vec3 w = mask.rgb * mask.a;
	float base_w = clamp(1.0 - (w.r + w.g + w.b), 0.0, 1.0);
	vec3 tint = base_color.rgb * base_w
			+ color_r.rgb * w.r
			+ color_g.rgb * w.g
			+ color_b.rgb * w.b;

	vec3 albedo = mix(tint, decal.rgb, decal.a);
	COLOR = vec4(shade.rgb * albedo, shade.a);
}
