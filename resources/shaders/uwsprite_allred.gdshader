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

//Shader Params

//UW Shader Params

uniform sampler2D texture_albedo : source_color,filter_nearest,repeat_enable;// the source image
global uniform sampler2D smoothpalette: source_color,filter_nearest; // pals.dat mapped into shades
global uniform sampler2D xfer: source_color,filter_nearest; //xfer.dat transparency
global uniform float cutoffdistance; // how far away the scene will render
global uniform sampler2D simpleshade: source_color,filter_nearest;//shading data for objectinfo layer. used to find darkness
global uniform bool final_color_pass; // native VR: resolve palette in this pass

uniform int objectindex_lowerbytes;
uniform int objectindex_upperbytes;

//varying float distanceToCam; // distance from cam to center
//varying vec4 world_pos;  // of current vertex
//varying float vertexToCam; // distance from current vertex to the camera

uniform float camera_offset = 0.0;

void vertex() {
	UV=UV*uv1_scale.xy+uv1_offset.xy;

	// Y-axis (cylindrical) billboard: yaw toward camera, keep feet on the floor (no pitch/roll).
	vec3 cam_x = normalize(vec3(INV_VIEW_MATRIX[0].x, 0.0, INV_VIEW_MATRIX[0].z));
	if (length(cam_x) < 0.001) {
		cam_x = vec3(1.0, 0.0, 0.0);
	}
	vec3 cam_y = vec3(0.0, 1.0, 0.0);
	vec3 cam_z = normalize(cross(cam_x, cam_y));
	mat4 modified_model_view = VIEW_MATRIX * mat4(
		vec4(cam_x, 0.0),
		vec4(cam_y, 0.0),
		vec4(cam_z, 0.0),
		MODEL_MATRIX[3]
	);
	MODELVIEW_MATRIX = modified_model_view;
	VERTEX += camera_offset * normalize((MODELVIEW_MATRIX * vec4(0.0, 0.0, 1.0, 0.0)).xyz);
}


void fragment() {

	float dist = final_color_pass ? length(VERTEX) : distance((INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xz, (INV_VIEW_MATRIX * vec4(vec3(0.0), 1.0)).xz);

	if (dist > cutoffdistance)
	{		
		discard;
	}
	else
	{
		uint camera_layers = CAMERA_VISIBLE_LAYERS;
		//read in greyscale texture to get color index		
		vec4 rgba = texture(texture_albedo, UV);
		vec4 alphacolour = texture(smoothpalette, vec2(rgba.r, 0));
		float maptouse = dist/cutoffdistance;

		if (final_color_pass)
		{
			// Native VR: resolve palette here (includes xfer indices via palette row 0 alpha).
			vec4 finalcolour = texture(smoothpalette, vec2(rgba.r, maptouse));
			ALBEDO = finalcolour.rgb;
			ALPHA = finalcolour.a;
		}
		else
		{
		switch(camera_layers)
		{
			case 2u: //world without xfer
			{
				int r = int( round (rgba.r * 255.0) );
				//ivec3 indexfer = ivec3(round(rgba.rgb * 255.0));
				if ((r>=249) && (r<=253))
				{
					//xfer colour detected in a world without xfer.
					ALBEDO.r = 0.0; 	
					ALBEDO.g = 0.0; 				
					ALBEDO.b = 0.0; 
					ALPHA = 0.0;	//force it to transparent alpha so it does not render on this camera.
				}
				else
				{
					//passthrough the data.
					ALBEDO.r = rgba.r;	
					ALBEDO.g = maptouse;				
					ALBEDO.b = rgba.b;
					ALPHA = alphacolour.a;
				}
				break;
			}

			case 6u: // world with xfer
			{	
				ALBEDO.r = rgba.r;	
				ALBEDO.g = maptouse;				
				ALBEDO.b = rgba.b;
				ALPHA = alphacolour.a;
				break;
			}

			case 8u: //objectinfo
			{
				//Render object info on camera 4
				vec4 simple = texture(simpleshade, vec2(dist/cutoffdistance, 0.0));
				int shadelevel = ivec3(round(simple.rgb * 255.0)).r;
				if (shadelevel >= 112 )
				{
					discard;
				}
				else
				{				
					ALBEDO.r = 0.0;
					ALBEDO.g = float(objectindex_lowerbytes)/255.0; 
					ALBEDO.b = float(objectindex_upperbytes)/255.0;
					ALPHA = alphacolour.a;
					break;
				}		
			}
			default:
			{
				//should not happen.

				ALBEDO.r = 0.3;
				ALPHA = alphacolour.a;
				break;
			}
		}
		}
	}
}