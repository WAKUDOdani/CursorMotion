// SDF and curved exposure integration adapted from motion_blur_cursor.lua (Unlicense).
cbuffer Constants : register(b0) {
    float4 Box;
    float4 Path0;
    float4 Path1;
    float4 Motion;
    float4 Shape;
    float4 fill_color;
    float4 outline_color;
    float4 shadow_color;
    float4 Look;
    float4 Skins;
    float4 Exposure;
    float4 History[16];
};
#define box_origin Box.xy
#define box_size Box.zw
#define p0 Path0.xy
#define p1 Path0.zw
#define p2 Path1.xy
#define cur_scale Path1.z
#define sample_count Path1.w
#define tilt_cs Motion.xy
#define trail_falloff Motion.z
#define opacity Motion.w
#define shape_a Shape.x
#define shape_b Shape.y
#define morph Shape.z
#define anim_time Shape.w
#define outline_width Look.x
#define shadow_blur Look.y
#define shadow_offset Look.zw
float4 VSMain(uint id : SV_VertexID) : SV_POSITION {
    return float4(id == 2 ? 3.0 : -1.0, id == 1 ? 3.0 : -1.0, 0, 1);
}
// One edge of the polygon SDF (Inigo Quilez). ds = (min squared distance, sign).
float2 poly_edge(float2 ds, float2 p, float2 a, float2 b)
{
	float2 e = b - a;
	float2 w = p - a;
	float2 q = w - e * clamp(dot(w, e) / dot(e, e), 0.0, 1.0);
	ds.x = min(ds.x, dot(q, q));
	bool c1 = p.y >= a.y;
	bool c2 = p.y < b.y;
	bool c3 = e.x * w.y > e.y * w.x;
	if ((c1 && c2 && c3) || (!c1 && !c2 && !c3))
		ds.y = -ds.y;
	return ds;
}

// Classic pointer, hotspot (tip) at the origin, y pointing down.
float sd_arrow(float2 p, float skin)
{
	float2 v0 = float2(0.0, 0.0);
	float2 v1 = float2(0.0, 16.0);
	float2 v2 = float2(3.8, 12.4);
	float2 v3 = float2(6.3, 18.2);
	float2 v4 = float2(8.9, 17.1);
	float2 v5 = float2(6.5, 11.4);
	float2 v6 = float2(11.4, 11.4);
	if (skin > 0.5) {
		v1 = float2(0.0, 15.5); v2 = float2(3.6, 12.1);
		v3 = float2(6.1, 18.2); v4 = float2(8.5, 17.2);
		v5 = float2(6.0, 11.2); v6 = float2(11.8, 11.2);
	}
	if (skin > 2.5 && skin < 3.5) {
		v2 = float2(3.6, 12.0); v3 = float2(6.0, 18.2);
		v4 = float2(8.4, 17.2); v5 = float2(6.0, 11.0);
	}
	float2 ds = float2(dot(p - v0, p - v0), 1.0);
	ds = poly_edge(ds, p, v0, v6);
	ds = poly_edge(ds, p, v1, v0);
	ds = poly_edge(ds, p, v2, v1);
	ds = poly_edge(ds, p, v3, v2);
	ds = poly_edge(ds, p, v4, v3);
	ds = poly_edge(ds, p, v5, v4);
	ds = poly_edge(ds, p, v6, v5);
	return ds.y * sqrt(ds.x) - ((skin > 0.5 && skin < 2.5) ? 0.85 : 0.6);
}

float sd_segment(float2 p, float2 a, float2 b)
{
	float2 pa = p - a;
	float2 ba = b - a;
	float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
	return length(pa - ba * h);
}

float smin(float a, float b, float k)
{
	float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
	return lerp(b, a, h) - k * h * (1.0 - h);
}

// Text I-beam with flared serifs, hotspot at the centre.
float sd_ibeam(float2 p, float skin)
{
	float2 q = abs(p);
	if (skin > 0.5) {
		float stem = sd_segment(q, float2(0, 0), float2(0, 9.1));
		float cap = sd_segment(q, float2(0, 9.1), float2(3.0, 9.1));
		return smin(stem, cap, skin > 2.5 && skin < 3.5 ? 0.2 : 1.25) - 0.8;
	}
	float stem  = sd_segment(q, float2(0.0, 0.0), float2(0.0, 8.0));
	float serif = sd_segment(q, float2(0.0, 8.7), float2(3.4, 9.6));
	return smin(stem, serif, 1.4) - 0.8;
}

// Busy spinner: a ring with a rotating comet. Returns (distance, fill alpha).
float2 sd_spinner(float2 p, float skin)
{
	float d = abs(length(p) - (skin > 0.5 ? 7.3 : 7.0)) - (skin > 0.5 ? 1.9 : 2.2);
	float a = atan2(p.y, p.x) * 0.15915494;
	float f = frac(a - anim_time * 1.25);
	return float2(d, lerp(0.1, 1.0, f * f));
}

float sd_hand_box(float2 p, float2 half_size, float radius)
{
    float2 q = abs(p) - half_size + radius;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
}

// Link/select hand. The index fingertip is the hotspot, just like the arrow tip.
// Broad palm, curled knuckles and an isolated thumb keep it readable at small sizes.
float sd_hand(float2 p, float skin)
{
    float softness = skin > 2.5 && skin < 3.5 ? 0.24 : 0.52;
    float palm = sd_hand_box(p - float2(2.7, 12.7), float2(4.3, 5.5), skin > 0.5 ? 1.75 : 1.3);
    float index = sd_segment(p, float2(0.0, 1.15), float2(0.0, 10.2)) - 1.15;
    float middle = sd_segment(p, float2(2.6, 7.1), float2(2.6, 11.0)) - 1.22;
    float ring = sd_segment(p, float2(4.9, 7.7), float2(4.9, 11.9)) - 1.15;
    float pinky = sd_segment(p, float2(6.8, 8.7), float2(6.8, 12.8)) - 1.05;
    float thumb = sd_segment(p, float2(-3.6, 9.0), float2(0.0, 15.1)) - 1.35;
    float d = smin(palm, index, softness);
    d = smin(d, middle, softness);
    d = smin(d, ring, softness);
    d = smin(d, pinky, softness);
    return smin(d, thumb, softness);
}

// Symmetric two-headed resize arrow, centre hotspot. The same material/outline
// as the main arrow, rotated for vertical and both diagonal Windows edge roles.
float sd_resize(float2 p, float id, float skin)
{
    float2 q = p;
    if (id > 4.5 && id < 5.5) q = p.yx;
    else if (id > 5.5 && id < 6.5) q = float2(p.x + p.y, p.y - p.x) * 0.70710678;
    else if (id > 6.5) q = float2(p.x - p.y, p.x + p.y) * 0.70710678;
    float2 a = float2(-9.5, 0), b = float2(-4.1, -4.3), c = float2(-4.1, -1.25);
    float2 d = float2(4.1, -1.25), e = float2(4.1, -4.3), f = float2(9.5, 0);
    float2 g = float2(4.1, 4.3), h = float2(4.1, 1.25), i = float2(-4.1, 1.25), j = float2(-4.1, 4.3);
    float2 ds = float2(dot(q-a,q-a), 1);
    ds = poly_edge(ds,q,a,b); ds = poly_edge(ds,q,b,c); ds = poly_edge(ds,q,c,d);
    ds = poly_edge(ds,q,d,e); ds = poly_edge(ds,q,e,f); ds = poly_edge(ds,q,f,g);
    ds = poly_edge(ds,q,g,h); ds = poly_edge(ds,q,h,i); ds = poly_edge(ds,q,i,j); ds = poly_edge(ds,q,j,a);
    return ds.y * sqrt(ds.x) - (skin > 2.5 && skin < 3.5 ? 0.18 : skin > 0.5 ? 0.45 : 0.3);
}

float2 shape_eval(float id, float2 p, float skin)
{
	float2 r;
	if (id < 0.5)
		r = float2(sd_arrow(p, skin), 1.0);
	else if (id < 1.5)
		// Optical sizing: arrow height 19.4 units; text 18.3, spinner 18.4.
		// Scaling the distance too preserves outline thickness across all forms.
		r = float2(sd_ibeam(p / 0.88, skin) * 0.88, 1.0);
    else if (id < 2.5)
		r = sd_spinner(p, skin);
    else if (id < 3.5)
        r = float2(sd_hand(p, skin), 1.0);
    else
        r = float2(sd_resize(p, id, skin), 1.0);
	return r;
}

// Morphs between two shapes by blending their distance fields.
float2 shape_mix(float2 p)
{
	float2 b = shape_eval(shape_b, p, Skins.y);
	if (morph < 0.999)
		b = lerp(shape_eval(shape_a, p, Skins.x), b, morph);
	return b;
}

// Premultiplied colour of the cursor (with shadow) placed at pos.
// k converts local distance units to output pixels.
float3 material_color(float style, float2 p)
{
	if (style < 0.5) return fill_color.rgb;
	float y = saturate((p.y + 10.0) / 30.0);
	float highlight = exp(-pow((p.x + p.y * 0.32 - 2.0) * 0.35, 2.0));
	float3 tint = style < 1.5 ? float3(0.65, 0.88, 1.0) : style < 2.5 ? float3(0.50, 1.0, 0.88) : style < 3.5 ? float3(0.65, 0.76, 0.92) : float3(1.0, 0.82, 0.68);
	float strength = style > 2.5 && style < 3.5 ? 0.10 : 0.32;
	float3 material = lerp(fill_color.rgb * (1.10 - y * 0.32), tint * fill_color.a, highlight * strength);
	return min(lerp(material, fill_color.a.xxx, pow(saturate(1.0 - y), 5.0) * 0.18), fill_color.a.xxx);
}

// Native alpha glass: beveled contour, a directional reflected rim and broad
// highlights. The desktop remains visible through the body via Windows alpha
// composition. No desktop capture or pointer-position filtering is involved.
float4 material_surface(float style, float2 p, float distance, float2 normal)
{
    if (style < 0.5) return fill_color;
    if (style > 2.5) return float4(material_color(style, p), fill_color.a);
    float edge = exp(-abs(distance + 0.35) * 1.25);
    float light = saturate(dot(normal, float2(-0.6, -0.8)));
    float dark = saturate(dot(normal, float2(0.6, 0.8)));
    float streak = exp(-pow((p.x + p.y * 0.38 - 1.6) * 0.27, 2.0));
    float fine = exp(-pow((p.x + p.y * 0.38 + 2.6) * 0.70, 2.0));
    float3 tint = fill_color.rgb / max(fill_color.a, 0.00001);
    float3 reflection = style < 1.5 ? float3(0.76, 0.93, 1.0)
        : lerp(float3(0.45, 0.95, 1.0), float3(0.94, 0.60, 1.0), saturate(0.5 + p.x * 0.035 + p.y * 0.012));
    float alpha = fill_color.a * (0.28 + 0.25 * edge + 0.12 * streak);
    float3 body = saturate(tint * (0.82 - 0.22 * edge * dark) + reflection * (0.12 + 0.12 * streak));
    float shine = fill_color.a * saturate(edge * (0.12 + 0.65 * pow(light, 4.0)) + fine * 0.18 + streak * 0.12);
    float4 base = float4(body * alpha, alpha);
    return float4(lerp(reflection, 1.0.xxx, light * 0.7) * shine, shine) + base * (1.0 - shine);
}

float4 cursor_sample(float2 px, float2 pos, float k, out float geometric_coverage)
{
	float2 d  = px - pos;
	float2 lp = float2(tilt_cs.x * d.x + tilt_cs.y * d.y,
	                   -tilt_cs.y * d.x + tilt_cs.x * d.y) / cur_scale;

	float2 s     = shape_mix(lp);
	if (Skins.w > 1.0) {
		// A target pixel covers a square: diagonal edges and corners span a
		// wider SDF interval than vertical edges. Use the actual shape gradient,
		// rather than only the screen-coordinate derivative from PSMain.
		float edge_width = abs(ddx(s.x)) + abs(ddy(s.x));
		k = min(k, rcp(max(edge_width, 1.0 / cur_scale) * Skins.w));
	}
	float  outer = saturate(0.5 - (s.x - outline_width) * k);
	float  inner = saturate(0.5 - s.x * k) * s.y;
	geometric_coverage = inner + outer * (1.0 - inner);

	float4 surface = fill_color;
	float styled = lerp(Skins.x > 0.5 ? 1.0 : 0.0, Skins.y > 0.5 ? 1.0 : 0.0, morph);
	float glass_style = lerp(Skins.x > 0.5 && Skins.x < 2.5 ? 1.0 : 0.0, Skins.y > 0.5 && Skins.y < 2.5 ? 1.0 : 0.0, morph);
	float2 normal = float2(0, -1);
	if (glass_style > 0.001) {
		// Quad derivatives give the SDF normal without four more shape evaluations
		// per blur sample. All branch conditions are uniform shader constants.
		float2 gradient = float2(ddx(s.x), ddy(s.x));
		normal = gradient * rsqrt(max(dot(gradient, gradient), 0.000001));
	}
	if (styled > 0.001) {
		// Blend the two actual materials, never intermediate pack IDs.
		surface = lerp(material_surface(Skins.x, lp, s.x, normal), material_surface(Skins.y, lp, s.x, normal), morph);
	}
	float4 col = outline_color * outer;
	col = surface * inner + col * (1.0 - surface.a * inner);
	if (styled > 0.001) {
		// A glass fill must reveal the desktop, rather than an opaque outline
		// painted underneath it. Keep only the visible outline shell.
		float fill_coverage = saturate(0.5 - s.x * k);
		float4 shell = outline_color * max(0.0, outer - fill_coverage);
		float rim_light = saturate(dot(normal, float2(-0.6, -0.8)));
		shell.rgb *= lerp(1.0, 0.48 + rim_light * 0.52, glass_style);
		float4 glass = surface * inner + shell * (1.0 - surface.a * inner);
		float shell_coverage = max(0.0, outer - fill_coverage);
		geometric_coverage = lerp(geometric_coverage, inner + shell_coverage * (1.0 - inner), styled);
		col = lerp(col, glass, styled);
	}

	if (shadow_color.a > 0.0) {
		float  sd = shape_mix(lp - shadow_offset).x - outline_width;
		float  sb = max(shadow_blur, 1.0 / k);
		float  sh = 1.0 - smoothstep(-sb, sb, sd);
		col = col + shadow_color * sh * (1.0 - col.a);
		geometric_coverage += sh * (1.0 - geometric_coverage);
	}
	return col;
}

float4 cursor_at(float2 px, float2 pos, float k)
{
    float coverage;
    return cursor_sample(px, pos, k, coverage);
}

float2 exposure_at(float t)
{
    float index = saturate(t) * (Exposure.x - 1.0);
    int a = min(int(index), int(Exposure.x) - 2);
    return lerp(History[a].xy, History[a + 1].xy, index - float(a));
}

float4 PSMain(float4 screen : SV_POSITION) : SV_TARGET
{
	float2 px = box_origin + screen.xy;
	float2 fw = abs(ddx(px)) + abs(ddy(px));
	float  k  = cur_scale / max(max(fw.x, fw.y), 0.0001);
	// Coverage for the monitor's final cursor pixels, before Windows scales the
	// native bitmap. The primary monitor uses the original branch unchanged.
	if (Skins.w > 1.0) k /= Skins.w;
	if (Exposure.x > 1.5 && Exposure.w > 0.35) {
		int count = int(clamp(sample_count, 4.0, 128.0));
		float4 sum = 0;
		float coverage_sum = 0, weights = 0;
		[loop] for (int j = 0; j < count; j++) {
			float t = float(j) / float(max(count - 1, 1));
			float2 position = exposure_at(t);
			float dt = 0.5 / float(max(count - 1, 1));
			float spacing = length(exposure_at(min(1.0, t + dt)) - exposure_at(max(0.0, t - dt)));
			float sample_k = k / max(1.0, spacing);
			// Endpoint-weighted whole-image exposure: the physical hotspot is
			// represented in the integral, with no separate sharp-head pass.
			float weight = exp2(-(1.5 + 3.5 * trail_falloff) * (1.0 - t));
			if (j == 0 || j == count - 1) weight *= 0.5;
			// Temporal samples already provide subpixel coverage while moving.
			// Keep the original4-sample raster for the stationary branch below.
			float mask;
			float4 value = cursor_sample(px, position, sample_k, mask);
			sum += value * weight;
			coverage_sum += mask * weight;
			weights += weight;
		}
		float4 result = sum / max(weights, 0.0001);
		float coverage = coverage_sum / max(weights, 0.0001);
		// Restore spatial coverage instead of making a long trail increasingly
		// faint. Intrinsic material transparency stays in premultiplied RGBA.
		float gain = 1.0 + Exposure.y * (1.0 - exp(-Exposure.w / max(cur_scale * 15.0, 1.0)));
		if (coverage > 0.002) result *= (1.0 - pow(saturate(1.0 - coverage), gain)) / coverage;
		return result * opacity;
	}

	int n = int(min(sample_count, 128.0));
	float4 acc  = float4(0.0, 0.0, 0.0, 0.0);
	float  wsum = 0.0;
	bool native_motion = Skins.z > 0.5;
	// Integrate the WHOLE image, as in the original cursor renderer. No
	// additional sharp icon painted over the exposure and no mode switch
	// that doubles translucent pixels when sample count changes from 1 to 2.
	for (int i = 0; i < n; i++) {
		// Midpoint exposure samples avoid screen-space noise crawling over the trail.
		float t = (n > 1) ? (float(i) + 0.5) / float(n) : 1.0;
		float u = 1.0 - t;
		float2 pos = u * u * p0 + 2.0 * u * t * p1 + t * t * p2;
		float w = lerp(1.0, 0.1 + 0.9 * t, trail_falloff);
		// Integrate the trail's temporal sample footprint as well as pixels.
		// Keeping a half-pixel edge on widely spaced exposure samples produces
		// a comb of tiny silhouettes. The actual head keeps its sharp footprint.
		float2 derivative = 2.0 * (u * (p1 - p0) + t * (p2 - p1));
		float footprint = native_motion ? max(0.5, length(derivative) / float(n)) : 0.5;
		float coverage_k = k / footprint;
		// Integrate four half-pixel footprints. SDFs stay vector-sharp at every
		// scale; subpixel coverage smooths diagonals without a post-blur filter.
		float4 coverage = cursor_at(px + float2(-0.25, -0.25), pos, coverage_k);
		coverage += cursor_at(px + float2(0.25, -0.25), pos, coverage_k);
		coverage += cursor_at(px + float2(-0.25, 0.25), pos, coverage_k);
		coverage += cursor_at(px + float2(0.25, 0.25), pos, coverage_k);
		acc += coverage * (0.25 * w);
		wsum += w;
	}

	float4 c = acc / max(wsum, 0.0001);
	c *= opacity;
	if (c.a < 0.0001)
		return float4(0.0, 0.0, 0.0, 0.0);
	return c;
}


