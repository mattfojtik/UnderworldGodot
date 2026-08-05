// NOTE: Shader automatically converted from Godot Engine 4.1.1.stable.mono's StandardMaterial3D.

shader_type spatial;
render_mode blend_mix,depth_draw_opaque,cull_back,diffuse_burley,specular_disabled,unshaded;
uniform vec4 albedo : source_color;
uniform float point_size : hint_range(0,128);
uniform float roughness : hint_range(0,1);
uniform sampler2D texture_metallic : hint_default_white,filter_linear_mipmap,repeat_enable;
uniform vec4 metallic_texture_channel;
uniform sampler2D texture_roughness : hint_roughness_r,filter_linear_mipmap,repeat_enable;
uniform float specular;
uniform float metallic;
uniform vec3 uv1_scale;
uniform vec3 uv1_offset;
uniform vec3 uv2_scale;
uniform vec3 uv2_offset;

//UW Shader Params

uniform sampler2D texture_albedo : source_color,filter_nearest,repeat_enable;// the source image
global uniform sampler2D smoothpalette: source_color,filter_nearest; // pals.dat mapped into shades
global uniform sampler2D xfer: source_color,filter_nearest; //xfer.dat transparency
global uniform sampler2D simpleshade: source_color,filter_nearest;//shading data for objectinfo layer. used to find darkness

global uniform float cutoffdistance; // how far away the scene will render
global uniform vec3 avatar_shade_origin; // simulated avatar eye for shade distance (not HMD)
global uniform bool renderwalls;
global uniform bool renderfloors;
global uniform bool renderceilings;
global uniform bool final_color_pass; // native VR: resolve palette in this pass

uniform int tileflags;
uniform int objectindex_lowerbytes;
uniform int objectindex_upperbytes;

void vertex() {
	UV=UV*uv1_scale.xy+uv1_offset.xy;
}


void fragment() {

	vec3 world_camera = (INV_VIEW_MATRIX * vec4(vec3(0.0), 1.0)).xyz;
	vec4 fragWorld4 = INV_VIEW_MATRIX * vec4(VERTEX, 1.0);
	// Avatar eye for VR room-scale; fall back to camera until C# sets the uniform.
	vec3 shade_origin = avatar_shade_origin;
	if (dot(shade_origin, shade_origin) < 1e-12) {
		shade_origin = world_camera;
	}
	float dist = distance(fragWorld4.xyz, shade_origin);

	if (dist > cutoffdistance)
	{		
		discard;
	}
	else
	{
		// Exact ObjectInfo camera only (not VR's multi-layer cull mask).
		if (!final_color_pass && CAMERA_VISIBLE_LAYERS == 8u)
		{
			//object info layer. 
			//contains tile information. (eg x,y and surface) or object index in the case of 3d models.
			
			vec4 simple = texture(simpleshade, vec2(dist/cutoffdistance, 0.0));
			int shadelevel = ivec3(round(simple.rgb * 255.0)).r;
			if (shadelevel >= 112 )
			{
				discard;
			}
			else
			{
				ALBEDO.r = float(tileflags)/255.0;//this will eventually be tileflags indicating if this is a floor, NSEW facing wall, diagonal or ceiling.
				ALBEDO.g = float(objectindex_lowerbytes)/255.0;   // or tileX
				ALBEDO.b = float(objectindex_upperbytes)/255.0;   // or tileY
			}
		}
		else
		{
			vec4 lowdetail = texture(texture_albedo, vec2(0.0, 0.0)); //get pixel at 0,0 which is the lowdetail colour to use
			//Get the red channel original data.
			vec4 rgba;// = texture(texture_albedo, UV);

			if ((!renderwalls) && (((tileflags >= 4) && (tileflags<=64)) || (tileflags == 0)))
			{
				rgba = lowdetail;
			}
			else
			{
				if ((!renderceilings) && (tileflags == 128))
				{
					rgba = lowdetail;
				}
				else
				{
					if ((!renderfloors) && ((tileflags >= 1) && (tileflags<=2)))
					{
						rgba = lowdetail;
					}
				else
					{
						rgba = texture(texture_albedo, UV);
					}
				}
				
			}
			
			//Get the uv distance offset within the shades.dat image
			float maptouse = dist/cutoffdistance;

			if (final_color_pass)
			{
				// Native VR: resolve palette here (no viewport_combination pass).
				// texelFetch uses raw palette bytes; texture() + source_color linearizes and
				// the mobile/XR 3D path can display darker than the flat canvas composite.
				ivec2 pal_size = textureSize(smoothpalette, 0);
				int px = clamp(int(round(rgba.r * 255.0)), 0, pal_size.x - 1);
				int py = clamp(int(round(maptouse * float(pal_size.y - 1))), 0, pal_size.y - 1);
				vec4 finalcolour = texelFetch(smoothpalette, ivec2(px, py), 0);
				ALBEDO = finalcolour.rgb;
			}
			else
			{
				// Multi-viewport: emit index + shade row for viewport_combination.
				rgba.g = maptouse;
				ALBEDO = rgba.rgb;
			}
		}

	}
}