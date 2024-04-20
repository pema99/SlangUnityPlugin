Shader "Pema99/MercMarch"
{
    Properties
    {
        [Header(General)]
        _Iterations("Iterations", Range(1, 256)) = 50
        _MaxDist("Max distance", Range(0, 4000)) = 100
        _Cutoff("Cutoff", Range(0, 0.1)) = 0.002
        _Epsilon("Epsilon", Range(0.000001, 0.1)) = 0.001
        _Scale("Scale", Range(0.1, 15)) = 0.5

        [Header(Animation)]
        _A("A", Range(0, 1)) = 0
        _B("B", Range(0, 1)) = 0
        _C("C", Range(0, 1)) = 0
        _D("D", Range(0, 1)) = 0
        _E("E", Vector) = (0, 0, 0, 0)
        _F("F", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+500" "DisableBatching" = "True" }
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
#define mod(x,y) (((x)-(y)*floor((x)/(y)))) 

// Shader properties
float _Iterations;
float _MaxDist;
float _Cutoff;
float _Epsilon;

// Common operators
float map(float3 p);
float3 normal(float3 p);
float3 march(float3 ro, float3 rd);

float s_union( float d1, float d2, float k )
{
    float h = clamp( 0.5 + 0.5*(d2-d1)/k, 0.0, 1.0 );
    return lerp( d2, d1, h ) - k*h*(1.0-h);
}

float s_subtract( float d1, float d2, float k )
{
    float h = clamp( 0.5 - 0.5*(d2+d1)/k, 0.0, 1.0 );
    return lerp( d2, -d1, h ) + k*h*(1.0-h);
}

float s_intersection( float d1, float d2, float k )
{
    float h = clamp( 0.5 - 0.5*(d2-d1)/k, 0.0, 1.0 );
    return lerp( d2, d1, h ) + k*h*(1.0-h);
}

float3 s_repeat(float3 p, float c)
{
    return mod(p+0.5*c,c)-0.5*c;
}

float i_sphere(float3 p, float4 sphere)
{
    return length(p - sphere.xyz) - sphere.w;
}

float i_torus(float3 p, float2 t)
{
    float2 q = float2(length(p.xz) - t.x,p.y);
    return length(q) - t.y;
}

float i_spiral(float3 p, float thickness, float height, float a, float b, float offset)
{
    const float two_pi = 6.2831;
    const float e = 2.7182;

    float r = sqrt(p.x * p.x + p.y * p.y);
    float t = atan2(p.y, p.x) + offset;

    float n = (log(r / a) / b - t) / two_pi;

    float r1 = a * exp(b * (t + two_pi * ceil(n)));
    float r2 = a * exp(b * (t + two_pi * floor(n)));
    
    float dist = min(abs(r1 - r), abs(r - r2));

    return max(dist / thickness, abs(p.z) - height);
}

float i_box(float3 p, float3 b, float r)
{
    float3 q = abs(p) - b;
    return length(max(q,0.0)) + min(max(q.x,max(q.y,q.z)),0.0) - r;
}

float i_capsule(float3 p, float3 a, float3 b, float r )
{
    float3 pa = p - a, ba = b - a;
    float h = clamp( dot(pa,ba)/dot(ba,ba), 0.0, 1.0 );
    return length( pa - ba*h ) - r;
}

float i_pyramid(float3 p, float2 c, float h)
{
    float2 q = h*float2(c.x/c.y,-1.0);
        
    float2 w = float2( length(p.xz), p.y );
    float2 a = w - q*clamp( dot(w,q)/dot(q,q), 0.0, 1.0 );
    float2 b = w - q*float2( clamp( w.x/q.x, 0.0, 1.0 ), 1.0 );
    float k = sign( q.y );
    float d = min(dot( a, a ),dot(b, b));
    float s = max( k*(w.x*q.y-w.y*q.x),k*(w.y-q.y)  );
    return sqrt(d)*sign(s);
}

float i_menger(float3 z0, float4 w) {
     float4 z = float4(z0, 2.);
     float3 offset = float3(0.785,1.1,0.46) + w.xyz;
     float scale = 2.3 + w.w;
     for (int n = 0; n < 5; n++) {
         z = abs(z);
         if (z.x < z.y) z.xy = z.yx;
         if (z.x < z.z) z.xz = z.zx;
         if (z.y < z.z) z.yz = z.zy;
         z = z*scale;
         z.xyz -= offset*(scale-1.0);
         if(z.z<-0.5*offset.z*(scale-1.0))z.z+=offset.z*(scale-1.0);
     }
     return (length(max(abs(z.xyz)-float3(1.0, 1.0, 1.0),0.0))-0.05)/z.w;
}

float i_ellipse( float3 p, float3 r )
{
    float k0 = length(p/r);
    float k1 = length(p/(r*r));
    return k0*(k0-1.0)/k1;
}

float i_bulb(float3 p, float phase, float spike, float2 rot)
{
    phase = -phase * 5 * _Time.y;

    float3 w = p;
    float m = dot(w,w);

    float4 trap = float4(abs(w),m);
	float dz = 1.0;
    
	for(int i = 0; i < 2; i++)
    {
        dz = 8.0*pow(sqrt(m),7.0)*dz + 1.0;
		//dz = 8.0*pow(m,3.5)*dz + 1.0;
        
        float r = length(w);
        float b = 8.0*acos( w.y/r );
        float a = 8.0*atan2( w.x + rot.x, w.z + rot.y);
        w = p + pow(r,8.0) * float3( sin(b + phase)*sin(a + phase), cos(b + phase), sin(b + phase)*cos(a  + phase + spike) );     
        
        trap = min(trap, float4(abs(w),m));

        m = dot(w, w);
		if (m > 256.0)
            break;
    }

    return 0.25*log(m)*sqrt(m)/dz;
}

float i_tentacle(float3 p)
{
	float3 n = float3(0, 1, 0);
	float k1 = 1.9;
	float k2 = (sin(p.x * k1) + sin(p.z * k1)) * 0.8;
	float k3 = (sin(p.y * k1) + sin(p.z * k1)) * 0.8;
	float w1 = 4.0 - dot(abs(p), normalize(n)) + k2;
	float w2 = 4.0 - dot(abs(p), normalize(n.yzx)) + k3;
	float s1 = length(mod(p.xy + float2(sin((p.z + p.x) * 2.0) * 0.3, cos((p.z + p.x) * 1.0) * 0.5), 2.0) - 1.0) - 0.2;
	float s2 = length(mod(0.5+p.yz + float2(sin((p.z + p.x) * 2.0) * 0.3, cos((p.z + p.x) * 1.0) * 0.3), 2.0) - 1.0) - 0.2;
	return min(w1, min(w2, min(s1, s2)));
}

float rand(float3 r) { return frac(sin(dot(r.xy,float2(1.38984*sin(r.z),1.13233*cos(r.z))))*653758.5453); }
float i_truchetarc(float3 pos)
{
    const float Thickness = 0.1;
    const float SuperQuadPower = 8.0;
	float r=length(pos.xy);
	return pow(pow(abs(r-0.5),SuperQuadPower)+pow(abs(pos.z-0.5),SuperQuadPower),1.0/SuperQuadPower)-Thickness;
}
float i_truchetcell(float3 pos)
{
	return min(min(
	    i_truchetarc(pos),
	    i_truchetarc(float3(pos.z,1.0-pos.x,pos.y))),
	    i_truchetarc(float3(1.0-pos.y,1.0-pos.z,pos.x)));
}
float i_truchet(float3 pos)
{
	float3 cellpos=frac(pos);
	float3 gridpos=floor(pos);

	float rnd=rand(gridpos);

	if(rnd<1.0/8.0) return i_truchetcell(float3(cellpos.x,cellpos.y,cellpos.z));
	else if(rnd<2.0/8.0) return i_truchetcell(float3(cellpos.x,1.0-cellpos.y,cellpos.z));
	else if(rnd<3.0/8.0) return i_truchetcell(float3(1.0-cellpos.x,cellpos.y,cellpos.z));
	else if(rnd<4.0/8.0) return i_truchetcell(float3(1.0-cellpos.x,1.0-cellpos.y,cellpos.z));
	else if(rnd<5.0/8.0) return i_truchetcell(float3(cellpos.y,cellpos.x,1.0-cellpos.z));
	else if(rnd<6.0/8.0) return i_truchetcell(float3(cellpos.y,1.0-cellpos.x,1.0-cellpos.z));
	else if(rnd<7.0/8.0) return i_truchetcell(float3(1.0-cellpos.y,cellpos.x,1.0-cellpos.z));
	else  return i_truchetcell(float3(1.0-cellpos.y,1.0-cellpos.x,1.0-cellpos.z));
}

float i_apollo(float3 p)
{
	float scale = 1.0;

    for( int i=0; i<4; i++ )
	{
		p = -1.0 + 2.0*frac(0.5*p+0.5);

        p -= sign(p)*0.04; // trick
        
        float r2 = dot(p,p);
		float k = 0.95/r2;
		p     *= k;
		scale *= k;
	}

    float d1 = sqrt( min( min( dot(p.xy,p.xy), dot(p.yz,p.yz) ), dot(p.zx,p.zx) ) ) - 0.02;
    float d2 = abs(p.y);
    float dmi = d2;
    if( d1<d2 )
    {
        dmi = d1;
    }
    return 0.5*dmi/scale;
}

float3 i_tunnel(float3 p)
{
    return cos(p.x)+cos(p.y*1.5)+cos(p.z)+cos(p.y*20.)*.05;
}

float2 i_hell_rotate(in float2 v, in float a) {
	return float2(cos(a)*v.x + sin(a)*v.y, -sin(a)*v.x + cos(a)*v.y);
}
float3 i_hell(float3 p)
{
    float time = 0;//_Time.y+60.0;
	float cutout = dot(abs(p.yz),float2(0.5, 0.5))-0.035;
	//float road = max(abs(p.y-0.025), abs(p.z)-0.035);
    float road = i_box(p+float3(0, 0.036, 0), float3(1000, 0.01, 0.03), 0);
	
	float3 z = abs(1.0-mod(p,2.0));
	z.yz = i_hell_rotate(z.yz, time*0.05);

	float d = 999.0;
	float s = 1.0;
	for (float i = 0.0; i < 3.0; i++) {
		z.xz = i_hell_rotate(z.xz, radians(i*10.0+time));
		z.zy = i_hell_rotate(z.yz, radians((i+1.0)*20.0+time*1.1234));
		z = abs(1.0-mod(z+i/3.0,2.0));
		
		z = z*2.0 - 0.3;
		s *= 0.5;
		d = min(d, (abs(min(i_torus(z, float2(0.3, 0.05)), max(abs(z.z)-0.05, abs(z.x)-0.05)))-0.005) * s);
	}
	return s_subtract( i_sphere(p, float4(-0.15, 0.2, 0, 0.1)), min(max(d, -cutout), road), 0);
}

float i_sierpinski(float3 p)
{
    const float3 va = float3(  0.0,  0.57735,  0.0 );
    const float3 vb = float3(  0.0, -1.0,  1.15470 );
    const float3 vc = float3(  1.0, -1.0, -0.57735 );
    const float3 vd = float3( -1.0, -1.0, -0.57735 );
    float a = 0.0;
    float s = 1.0;
    float r = 1.0;
    float dm;
    float3 v;
    for( int i=0; i<7; i++ )
	{
	    float d, t;
		d = dot(p-va,p-va);              v=va; dm=d; t=0.0;
        d = dot(p-vb,p-vb); if( d<dm ) { v=vb; dm=d; t=1.0; }
        d = dot(p-vc,p-vc); if( d<dm ) { v=vc; dm=d; t=2.0; }
        d = dot(p-vd,p-vd); if( d<dm ) { v=vd; dm=d; t=3.0; }
		p = v + 2.0*(p - v); r*= 2.0;
		a = t + 4.0*a; s*= 4.0;
	}
	
	return (sqrt(dm)-1.0)/r;
}

float i_julia(float3 pos)
{
    //float t = 2.5;
    float t = _Time.y / 3.0;
    
	float4 c = 0.5*float4(cos(t),cos(t*1.1),cos(t*2.3),cos(t*3.1));
    float4 z = float4( pos, 0.0 );
	float4 nz;
    
	float md2 = 1.0;
	float mz2 = dot(z,z);

	for(int i=0;i<8;i++)
	{
		md2*=4.0*mz2;
	    nz.x=z.x*z.x-dot(z.yzw,z.yzw);
		nz.yzw=2.0*z.x*z.yzw;
		z=nz+c;

		mz2 = dot(z,z);
		if(mz2>4.0)
        {
			break;
        }
	}

	return 0.25*sqrt(mz2/md2)*log(mz2);
}

float i_knighty(float3 p, float i0)
{
	const float minsx[5] = {-.3252, -1.05,-1.21,-1.04,-0.737};
	const float minsy[5] = {-.7862, -1.05,-.954,-.79,-0.73};
	const float minsz[5] = {-.0948, -0.0001,-.0001,-.126,-1.23};
    const float minsw[5] = {.678, .7,1.684,.833, .627};
	const float maxsx[5] = {.3457, 1.05,.39,.3457,.73};
	const float maxsy[5] = {1.0218, 1.05,.65,1.0218,0.73};
	const float maxsz[5] = {1.2215, 1.27,1.27,1.2215,.73};
    const float maxsw[5] = {.9834, .95,2.74,.9834, .8335};

    float4 mins = float4(minsx[i0], minsy[i0], minsz[i0], minsw[i0]);
    float4 maxs = float4(maxsx[i0], maxsy[i0], maxsz[i0], maxsw[i0]);

    float k = 0.0;
    float scale=1.0;
    for (int i=0; i < 5; i++)
    {
        p = 2.0 * clamp(p, mins.xyz, maxs.xyz) - p;
        k = max(mins.w / dot(p,p), 1.0);
        p *= k;
        scale *= k;
    }
    float rxy = length(p.xy);
    return 0.7 * max(rxy - maxs.w, rxy * p.z / length(p)) / scale;
}

float i_tube(float3 q)
{
 	float3 p = abs(frac(q/4.)*4. - 2.);
 	float tube = min(max(p.x, p.y), min(max(p.y, p.z), max(p.x, p.z))) - 4./3. - .015;// + .05;
    p = abs(frac(q/2.)*2. - 1.);
 	tube = max(tube, s_union(max(p.x, p.y), s_union(max(p.y, p.z), max(p.x, p.z), .05), .05) - 2./3.);// + .025
    float panel = s_union(max(p.x, p.y),s_union(max(p.y, p.z),max(p.x, p.z), .125), .125)-0.5; // EQN 3
    float strip = step(p.x, .75)*step(p.y, .75)*step(p.z, .75);
    panel -= (strip)*.025;     
    p = abs(frac(q*2.)*.5 - .25);
    float pan2 = min(p.x, min(p.y,p.z))-.05;    
    panel = max(abs(panel), abs(pan2)) - .0425;    
    p = abs(frac(q*1.5)/1.5 - 1./3.);
 	tube = max(tube, min(max(p.x, p.y), min(max(p.y, p.z), max(p.x, p.z))) - 2./9. + .025); // + .025 
    p = abs(frac(q*3.)/3. - 1./6.);
 	tube = max(tube, min(max(p.x, p.y), min(max(p.y, p.z), max(p.x, p.z))) - 1./9. - .035); //- .025 
    return min(panel, tube);
}

// Machinery
float3 normal(float3 p)
{
    float d = map(p);
    float2 e = float2(_Epsilon, 0);
    
    float3 n = d - float3(
        map(p-e.xyy),
        map(p-e.yxy),
        map(p-e.yyx));

    return normalize(mul(unity_ObjectToWorld, n));
}

static float mat = 0;
float3 march(float3 ro, float3 rd)
{
    float c = 1e5;
    float t = 0.0;
    
    for (int i = 0; i < _Iterations; i++) {
        float3 p = ro + rd * t;
        float closest = map(p);
        c = min(c, closest);
        t += closest * 0.7;
        if(t > _MaxDist || closest < _Cutoff) break;
    }
    
    return float3(t, i, c);
}

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 ro_w : TEXCOORD1;
                float3 hitPos_w : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.ro_w = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1));
                o.hitPos_w = v.vertex;
                return o;
            }

            float _A;
            float _B;
            float _C;
            float _D;
            float4 _E;
            float4 _F;
            float _Scale;

            float2x2 Rot(float a) {
                float s=sin(a), c=cos(a);
                return float2x2(c, -s, s, c);
            }

            float sdBox( float3 p, float3 b )
            {
                float3 q = abs(p) - b;
                return length(max(q,0.0)) + min(max(q.x,max(q.y,q.z)),0.0);
            }












            

            float s_primal_ctx_length_0_0(float3 U_S40_0)
            {
                return length(U_S40_0);
            }
            
            float s_primal_ctx_pow_0_0(float U_S23_0, float U_S24_0)
            {
                return pow(U_S23_0, U_S24_0);
            }
            
            float s_primal_ctx_acos_0_0(float U_S5_0)
            {
                return acos(U_S5_0);
            }
            
            float s_primal_ctx_atan2_0_0(float U_S11_0, float U_S12_0)
            {
                return atan2(U_S11_0, U_S12_0);
            }
            
            float s_primal_ctx_sin_0_0(float U_S33_0)
            {
                return sin(U_S33_0);
            }
            
            float s_primal_ctx_cos_0_0(float U_S29_0)
            {
                return cos(U_S29_0);
            }
            
            float s_primal_ctx_log_0_0(float U_S17_0)
            {
                return log(U_S17_0);
            }
            
            struct s_bwd_prop_i_mandelbulb_Intermediates_0_0
            {
                float U_S36_0;
                float  U_S37_0[int(14)];
                float3  U_S38_0[int(14)];
                int U_S39_0;
            };
            
            float s_bwd_primal_i_mandelbulb_0_0(float3 dppos_0_0, float bailout_0_0, float power_0_0, out s_bwd_prop_i_mandelbulb_Intermediates_0_0 _s_diff_ctx_0_0)
            {
                float3 U_S42_0 = (float3)0.0;
                _s_diff_ctx_0_0.U_S36_0 = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(0)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(1)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(2)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(3)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(4)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(5)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(6)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(7)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(8)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(9)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(10)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(11)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(12)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(13)] = 0.0;
                _s_diff_ctx_0_0.U_S38_0[int(0)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(1)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(2)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(3)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(4)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(5)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(6)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(7)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(8)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(9)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(10)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(11)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(12)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(13)] = U_S42_0;
                _s_diff_ctx_0_0.U_S39_0 = int(0);
                _s_diff_ctx_0_0.U_S36_0 = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(0)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(1)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(2)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(3)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(4)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(5)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(6)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(7)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(8)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(9)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(10)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(11)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(12)] = 0.0;
                _s_diff_ctx_0_0.U_S37_0[int(13)] = 0.0;
                _s_diff_ctx_0_0.U_S38_0[int(0)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(1)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(2)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(3)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(4)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(5)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(6)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(7)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(8)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(9)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(10)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(11)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(12)] = U_S42_0;
                _s_diff_ctx_0_0.U_S38_0[int(13)] = U_S42_0;
                _s_diff_ctx_0_0.U_S39_0 = int(0);
                float _S1 = power_0_0 - 1.0;
                float r_0_0;
                int _pc_0_0 = int(0);
                float dr_0_0 = 2.0;
                float3 z_0_0 = dppos_0_0;
                bool _bflag_0_0 = true;
                int i_0_0 = int(0);
                float r_1_0 = 0.0;
                [unroll]
                for(;;)
                {
                    _s_diff_ctx_0_0.U_S36_0 = r_0_0;
                    _s_diff_ctx_0_0.U_S37_0[_pc_0_0] = dr_0_0;
                    _s_diff_ctx_0_0.U_S38_0[_pc_0_0] = z_0_0;
                    _s_diff_ctx_0_0.U_S39_0 = _pc_0_0;
                    if(_bflag_0_0)
                    {
                    }
                    else
                    {
                        break;
                    }
                    bool U_S45_0 = i_0_0 < int(12);
                    float r_2_0;
                    if(U_S45_0)
                    {
                        r_2_0 = r_0_0;
                    }
                    else
                    {
                        r_2_0 = r_1_0;
                    }
                    float r_3_0;
                    float U_S46_0;
                    int U_S47_0;
                    float3 U_S48_0;
                    if(U_S45_0)
                    {
                        float U_S49_0 = s_primal_ctx_length_0_0(z_0_0);
                        bool _bflag_1_0;
                        if(U_S49_0 > bailout_0_0)
                        {
                            _bflag_1_0 = false;
                            r_3_0 = U_S49_0;
                        }
                        else
                        {
                            _bflag_1_0 = U_S45_0;
                            r_3_0 = r_2_0;
                        }
                        if(_bflag_1_0)
                        {
                            float theta_0_0 = s_primal_ctx_acos_0_0(z_0_0.z / U_S49_0) * power_0_0;
                            float phi_0_0 = s_primal_ctx_atan2_0_0(z_0_0.y, z_0_0.x) * power_0_0;
                            float U_S51_0 = s_primal_ctx_sin_0_0(theta_0_0);
                            float3 z_1_0 = s_primal_ctx_pow_0_0(U_S49_0, power_0_0) * float3(U_S51_0 * s_primal_ctx_cos_0_0(phi_0_0), s_primal_ctx_sin_0_0(phi_0_0) * U_S51_0, s_primal_ctx_cos_0_0(theta_0_0)) + dppos_0_0;
                            U_S46_0 = s_primal_ctx_pow_0_0(U_S49_0, _S1) * power_0_0 * dr_0_0 + 1.0;
                            U_S47_0 = int(1);
                            U_S48_0 = z_1_0;
                        }
                        else
                        {
                            U_S46_0 = 0.0;
                            U_S47_0 = int(0);
                            U_S48_0 = U_S42_0;
                        }
                        float _S2 = r_3_0;
                        r_3_0 = U_S46_0;
                        U_S46_0 = U_S49_0;
                        r_0_0 = _S2;
                    }
                    else
                    {
                        U_S47_0 = int(0);
                        r_3_0 = 0.0;
                        U_S46_0 = 0.0;
                        U_S48_0 = U_S42_0;
                        r_0_0 = r_2_0;
                    }
                    if(U_S47_0 != int(1))
                    {
                        _bflag_0_0 = false;
                    }
                    if(_bflag_0_0)
                    {
                        int U_S53_0 = i_0_0 + int(1);
                        dr_0_0 = r_3_0;
                        z_0_0 = U_S48_0;
                        i_0_0 = U_S53_0;
                        r_1_0 = U_S46_0;
                    }
                    int _S3 = _pc_0_0 + int(1);
                    _pc_0_0 = _S3;
                }
                return 0.5 * s_primal_ctx_log_0_0(r_0_0) * r_0_0 / dr_0_0;
            }
            
            struct DiffPair_float_0_0
            {
                float primal_0_0;
                float differential_0_0;
            };
            
            void _d_log_0_0(inout DiffPair_float_0_0 dpx_3_0, float dOut_3_0)
            {
                dpx_3_0.differential_0_0 = 1.0 / dpx_3_0.primal_0_0 * dOut_3_0;
                return;
            }
            
            void s_bwd_prop_log_0_0(inout DiffPair_float_0_0 U_S18_0, float U_S19_0)
            {
                _d_log_0_0(U_S18_0, U_S19_0);
                return;
            }
            
            void _d_cos_0_0(inout DiffPair_float_0_0 dpx_5_0, float dOut_5_0)
            {
                dpx_5_0.differential_0_0 = - sin(dpx_5_0.primal_0_0) * dOut_5_0;
                return;
            }
            
            void s_bwd_prop_cos_0_0(inout DiffPair_float_0_0 U_S30_0, float U_S31_0)
            {
                _d_cos_0_0(U_S30_0, U_S31_0);
                return;
            }
            
            void _d_sin_0_0(inout DiffPair_float_0_0 dpx_6_0, float dOut_6_0)
            {
                dpx_6_0.differential_0_0 = cos(dpx_6_0.primal_0_0) * dOut_6_0;
                return;
            }
            
            void s_bwd_prop_sin_0_0(inout DiffPair_float_0_0 U_S34_0, float U_S35_0)
            {
                _d_sin_0_0(U_S34_0, U_S35_0);
                return;
            }
            
            void _d_pow_0_0(inout DiffPair_float_0_0 dpx_4_0, inout DiffPair_float_0_0 dpy_1_0, float dOut_4_0)
            {
                if(dpx_4_0.primal_0_0 < 9.99999997475242708e-07)
                {
                    dpx_4_0.differential_0_0 = 0.0;
                    dpy_1_0.differential_0_0 = 0.0;
                }
                else
                {
                    float val_0_0 = pow(dpx_4_0.primal_0_0, dpy_1_0.primal_0_0);
                    DiffPair_float_0_0 U_S20_0 = dpx_4_0;
                    dpx_4_0.differential_0_0 = val_0_0 * dpy_1_0.primal_0_0 / dpx_4_0.primal_0_0 * dOut_4_0;
                    dpy_1_0.differential_0_0 = val_0_0 * log(U_S20_0.primal_0_0) * dOut_4_0;
                }
                return;
            }
            
            void s_bwd_prop_pow_0_0(inout DiffPair_float_0_0 U_S25_0, inout DiffPair_float_0_0 U_S26_0, float U_S27_0)
            {
                _d_pow_0_0(U_S25_0, U_S26_0, U_S27_0);
                return;
            }
            
            void _d_atan2_0_0(inout DiffPair_float_0_0 dpy_0_0, inout DiffPair_float_0_0 dpx_2_0, float dOut_2_0)
            {
                DiffPair_float_0_0 U_S8_0 = dpx_2_0;
                dpx_2_0.differential_0_0 = - dpy_0_0.primal_0_0 / (dpx_2_0.primal_0_0 * dpx_2_0.primal_0_0 + dpy_0_0.primal_0_0 * dpy_0_0.primal_0_0) * dOut_2_0;
                float _S4 = U_S8_0.primal_0_0;
                dpy_0_0.differential_0_0 = U_S8_0.primal_0_0 / (_S4 * _S4 + dpy_0_0.primal_0_0 * dpy_0_0.primal_0_0) * dOut_2_0;
                return;
            }
            
            void s_bwd_prop_atan2_0_0(inout DiffPair_float_0_0 U_S13_0, inout DiffPair_float_0_0 U_S14_0, float U_S15_0)
            {
                _d_atan2_0_0(U_S13_0, U_S14_0, U_S15_0);
                return;
            }
            
            void _d_acos_0_0(inout DiffPair_float_0_0 dpx_1_0, float dOut_1_0)
            {
                dpx_1_0.differential_0_0 = -1.0 / sqrt(1.0 - dpx_1_0.primal_0_0 * dpx_1_0.primal_0_0) * dOut_1_0;
                return;
            }
            
            void s_bwd_prop_acos_0_0(inout DiffPair_float_0_0 U_S6_0, float U_S7_0)
            {
                _d_acos_0_0(U_S6_0, U_S7_0);
                return;
            }
            
            void _d_sqrt_0_0(inout DiffPair_float_0_0 dpx_0_0, float dOut_0_0)
            {
                dpx_0_0.differential_0_0 = 0.5 / sqrt(max(1.00000001168609742e-07, dpx_0_0.primal_0_0)) * dOut_0_0;
                return;
            }
            
            void s_bwd_prop_sqrt_0_0(inout DiffPair_float_0_0 U_S2_0, float U_S3_0)
            {
                _d_sqrt_0_0(U_S2_0, U_S3_0);
                return;
            }
            
            struct DiffPair_vectorx3Cfloatx2C3x3E_0_0
            {
                float3 primal_0_1;
                float3 differential_0_1;
            };
            
            void s_bwd_prop_length_impl_0_0(inout DiffPair_vectorx3Cfloatx2C3x3E_0_0 dpx_7_0, float _s_dOut_0_0)
            {
                DiffPair_float_0_0 U_S57_0;
                U_S57_0.primal_0_0 = dpx_7_0.primal_0_1[int(0)] * dpx_7_0.primal_0_1[int(0)] + dpx_7_0.primal_0_1[int(1)] * dpx_7_0.primal_0_1[int(1)] + dpx_7_0.primal_0_1[int(2)] * dpx_7_0.primal_0_1[int(2)];
                U_S57_0.differential_0_0 = 0.0;
                s_bwd_prop_sqrt_0_0(U_S57_0, _s_dOut_0_0);
                float U_S58_0 = dpx_7_0.primal_0_1[int(2)] * U_S57_0.differential_0_0;
                float U_S59_0 = U_S58_0 + U_S58_0;
                float U_S60_0 = dpx_7_0.primal_0_1[int(1)] * U_S57_0.differential_0_0;
                float U_S61_0 = U_S60_0 + U_S60_0;
                float U_S62_0 = dpx_7_0.primal_0_1[int(0)] * U_S57_0.differential_0_0;
                float U_S63_0 = U_S62_0 + U_S62_0;
                float3 U_S64_0 = (float3)0.0;
                U_S64_0[int(2)] = U_S59_0;
                U_S64_0[int(1)] = U_S61_0;
                U_S64_0[int(0)] = U_S63_0;
                dpx_7_0.differential_0_1 = U_S64_0;
                return;
            }
            
            void s_bwd_length_impl_0_0(inout DiffPair_vectorx3Cfloatx2C3x3E_0_0 U_S65_0, float U_S66_0)
            {
                s_bwd_prop_length_impl_0_0(U_S65_0, U_S66_0);
                return;
            }
            
            void s_bwd_prop_length_0_0(inout DiffPair_vectorx3Cfloatx2C3x3E_0_0 U_S67_0, float U_S68_0)
            {
                s_bwd_length_impl_0_0(U_S67_0, U_S68_0);
                return;
            }
            
            void s_bwd_prop_i_mandelbulb_0_0(inout DiffPair_vectorx3Cfloatx2C3x3E_0_0 dppos_1_0, float bailout_1_0, float power_1_0, float _s_dOut_1_0, s_bwd_prop_i_mandelbulb_Intermediates_0_0 _s_diff_ctx_1_0)
            {
                float3 U_S69_0;
                float U_S70_0;
                float r_4_0;
                float r_5_0;
                int _dc_0_0;
                bool _bflag_2_0;
                float3 U_S71_0 = (float3)0.0;
                DiffPair_vectorx3Cfloatx2C3x3E_0_0 _S5 = dppos_1_0;
                float _S6 = power_1_0 - 1.0;
                int U_S74_0 = _s_diff_ctx_1_0.U_S39_0 - int(1);
                float U_S75_0 = 0.5 * s_primal_ctx_log_0_0(_s_diff_ctx_1_0.U_S36_0);
                float U_S76_0 = U_S75_0 * _s_diff_ctx_1_0.U_S36_0;
                float r_6_0;
                bool _bflag_3_0 = true;
                int i_1_0 = int(0);
                float r_7_0 = 0.0;
                float3 z_2_0 = dppos_1_0.primal_0_1;
                float dr_1_0 = 2.0;
                int _pc_1_0 = int(0);
                [unroll]
                for(;;)
                {
                    if(_bflag_3_0)
                    {
                    }
                    else
                    {
                        break;
                    }
                    bool U_S77_0 = i_1_0 < int(12);
                    if(U_S77_0)
                    {
                        r_5_0 = r_6_0;
                    }
                    else
                    {
                        r_5_0 = r_7_0;
                    }
                    if(U_S77_0)
                    {
                        float U_S78_0 = s_primal_ctx_length_0_0(z_2_0);
                        if(U_S78_0 > bailout_1_0)
                        {
                            _bflag_2_0 = false;
                            r_4_0 = U_S78_0;
                        }
                        else
                        {
                            _bflag_2_0 = U_S77_0;
                            r_4_0 = r_5_0;
                        }
                        if(_bflag_2_0)
                        {
                            float theta_1_0 = s_primal_ctx_acos_0_0(z_2_0.z / U_S78_0) * power_1_0;
                            float phi_1_0 = s_primal_ctx_atan2_0_0(z_2_0.y, z_2_0.x) * power_1_0;
                            float U_S80_0 = s_primal_ctx_sin_0_0(theta_1_0);
                            float3 z_3_0 = s_primal_ctx_pow_0_0(U_S78_0, power_1_0) * float3(U_S80_0 * s_primal_ctx_cos_0_0(phi_1_0), s_primal_ctx_sin_0_0(phi_1_0) * U_S80_0, s_primal_ctx_cos_0_0(theta_1_0)) + _S5.primal_0_1;
                            U_S70_0 = s_primal_ctx_pow_0_0(U_S78_0, _S6) * power_1_0 * dr_1_0 + 1.0;
                            _dc_0_0 = int(1);
                            U_S69_0 = z_3_0;
                        }
                        else
                        {
                            U_S70_0 = 0.0;
                            _dc_0_0 = int(0);
                            U_S69_0 = U_S71_0;
                        }
                        float _S7 = r_4_0;
                        r_4_0 = U_S70_0;
                        U_S70_0 = U_S78_0;
                        r_6_0 = _S7;
                    }
                    else
                    {
                        _dc_0_0 = int(0);
                        r_4_0 = 0.0;
                        U_S70_0 = 0.0;
                        U_S69_0 = U_S71_0;
                        r_6_0 = r_5_0;
                    }
                    if(_dc_0_0 != int(1))
                    {
                        _bflag_3_0 = false;
                    }
                    if(_bflag_3_0)
                    {
                        i_1_0 = i_1_0 + int(1);
                        r_7_0 = U_S70_0;
                        z_2_0 = U_S69_0;
                        dr_1_0 = r_4_0;
                    }
                    int _S8 = _pc_1_0 + int(1);
                    _pc_1_0 = _S8;
                }
                float U_S83_0 = _s_diff_ctx_1_0.U_S37_0[_pc_1_0];
                float U_S84_0 = _s_dOut_1_0 / (U_S83_0 * U_S83_0);
                float U_S85_0 = U_S76_0 * - U_S84_0;
                float U_S86_0 = _s_diff_ctx_1_0.U_S37_0[_pc_1_0] * U_S84_0;
                float U_S87_0 = U_S75_0 * U_S86_0;
                float U_S88_0 = 0.5 * (_s_diff_ctx_1_0.U_S36_0 * U_S86_0);
                DiffPair_float_0_0 U_S89_0;
                U_S89_0.primal_0_0 = _s_diff_ctx_1_0.U_S36_0;
                U_S89_0.differential_0_0 = 0.0;
                s_bwd_prop_log_0_0(U_S89_0, U_S88_0);
                float U_S90_0 = U_S87_0 + U_S89_0.differential_0_0;
                _dc_0_0 = U_S74_0;
                dr_1_0 = U_S85_0;
                r_7_0 = 0.0;
                z_2_0 = U_S71_0;
                U_S69_0 = U_S71_0;
                float3 U_S91_0 = U_S71_0;
                r_6_0 = U_S90_0;
                int i_0 = int(0);
                [unroll]
                for(;;)
                {
                    if(_dc_0_0 >= int(0))
                    {
                    }
                    else
                    {
                        break;
                    }
                    bool U_S92_0 = _dc_0_0 < int(12);
                    float U_S95_0;
                    float U_S96_0;
                    float U_S97_0;
                    float U_S98_0;
                    float U_S99_0;
                    float U_S100_0;
                    float U_S93_0;
                    float U_S94_0;
                    float U_S101_0;
                    float3 U_S102_0;
                    float3 U_S103_0;
                    if(U_S92_0)
                    {
                        int _S9 = _dc_0_0;
                        float U_S105_0 = s_primal_ctx_length_0_0(_s_diff_ctx_1_0.U_S38_0[_dc_0_0]);
                        bool U_S106_0 = U_S105_0 > bailout_1_0;
                        if(U_S106_0)
                        {
                            _bflag_3_0 = false;
                        }
                        else
                        {
                            _bflag_3_0 = U_S92_0;
                        }
                        if(_bflag_3_0)
                        {
                            float U_S107_0 = _s_diff_ctx_1_0.U_S38_0[_S9].z;
                            float U_S108_0 = U_S107_0 / U_S105_0;
                            float U_S109_0 = U_S105_0 * U_S105_0;
                            float U_S110_0 = _s_diff_ctx_1_0.U_S38_0[_S9].y;
                            float U_S111_0 = _s_diff_ctx_1_0.U_S38_0[_S9].x;
                            float3 U_S113_0 = (float3)s_primal_ctx_pow_0_0(U_S105_0, power_1_0);
                            float theta_2_0 = s_primal_ctx_acos_0_0(U_S108_0) * power_1_0;
                            float phi_2_0 = s_primal_ctx_atan2_0_0(U_S110_0, U_S111_0) * power_1_0;
                            float U_S114_0 = s_primal_ctx_sin_0_0(theta_2_0);
                            float U_S115_0 = s_primal_ctx_cos_0_0(phi_2_0);
                            float U_S116_0 = s_primal_ctx_sin_0_0(phi_2_0);
                            float3 U_S117_0 = float3(U_S114_0 * U_S115_0, U_S116_0 * U_S114_0, s_primal_ctx_cos_0_0(theta_2_0));
                            U_S95_0 = s_primal_ctx_pow_0_0(U_S105_0, _S6) * power_1_0;
                            U_S96_0 = U_S110_0;
                            U_S97_0 = U_S111_0;
                            U_S98_0 = U_S108_0;
                            U_S99_0 = U_S109_0;
                            U_S100_0 = U_S107_0;
                            i_1_0 = int(1);
                            U_S102_0 = U_S113_0;
                            U_S103_0 = U_S117_0;
                            r_5_0 = theta_2_0;
                            r_4_0 = U_S116_0;
                            U_S70_0 = U_S114_0;
                            U_S93_0 = phi_2_0;
                            U_S94_0 = U_S115_0;
                        }
                        else
                        {
                            U_S95_0 = 0.0;
                            U_S96_0 = 0.0;
                            U_S97_0 = 0.0;
                            U_S98_0 = 0.0;
                            U_S99_0 = 0.0;
                            U_S100_0 = 0.0;
                            i_1_0 = int(0);
                            U_S102_0 = U_S71_0;
                            U_S103_0 = U_S71_0;
                            r_5_0 = 0.0;
                            r_4_0 = 0.0;
                            U_S70_0 = 0.0;
                            U_S93_0 = 0.0;
                            U_S94_0 = 0.0;
                        }
                        float _S10 = U_S95_0;
                        float _S11 = U_S96_0;
                        float _S12 = U_S97_0;
                        float _S13 = U_S98_0;
                        float _S14 = U_S99_0;
                        float _S15 = U_S100_0;
                        U_S95_0 = U_S105_0;
                        U_S96_0 = _S10;
                        U_S97_0 = _S11;
                        U_S98_0 = _S12;
                        U_S99_0 = _S13;
                        U_S100_0 = _S14;
                        U_S101_0 = _S15;
                        _bflag_2_0 = U_S106_0;
                    }
                    else
                    {
                        i_1_0 = int(0);
                        _bflag_3_0 = false;
                        U_S102_0 = U_S71_0;
                        U_S103_0 = U_S71_0;
                        r_5_0 = 0.0;
                        r_4_0 = 0.0;
                        U_S70_0 = 0.0;
                        U_S93_0 = 0.0;
                        U_S94_0 = 0.0;
                        U_S95_0 = 0.0;
                        U_S96_0 = 0.0;
                        U_S97_0 = 0.0;
                        U_S98_0 = 0.0;
                        U_S99_0 = 0.0;
                        U_S100_0 = 0.0;
                        U_S101_0 = 0.0;
                        _bflag_2_0 = false;
                    }
                    float U_S124_0;
                    float U_S126_0;
                    float U_S125_0;
                    float U_S127_0;
                    float3 U_S128_0;
                    float3 U_S129_0;
                    if(!(i_1_0 != int(1)))
                    {
                        U_S128_0 = z_2_0;
                        U_S124_0 = dr_1_0;
                        U_S129_0 = U_S71_0;
                        U_S126_0 = 0.0;
                        U_S125_0 = r_7_0;
                        U_S127_0 = 0.0;
                    }
                    else
                    {
                        U_S128_0 = U_S71_0;
                        U_S124_0 = 0.0;
                        U_S129_0 = z_2_0;
                        U_S126_0 = dr_1_0;
                        U_S125_0 = 0.0;
                        U_S127_0 = r_7_0;
                    }
                    float U_S130_0;
                    float3 z_2_1;
                    float3 U_S69_1;
                    if(U_S92_0)
                    {
                        float U_S131_0;
                        float3 U_S134_0;
                        if(_bflag_3_0)
                        {
                            float3 U_S135_0 = U_S102_0 * U_S128_0;
                            float3 U_S136_0 = U_S103_0 * U_S128_0;
                            DiffPair_float_0_0 U_S137_0;
                            U_S137_0.primal_0_0 = r_5_0;
                            U_S137_0.differential_0_0 = 0.0;
                            s_bwd_prop_cos_0_0(U_S137_0, U_S135_0[int(2)]);
                            float U_S138_0 = r_4_0 * U_S135_0[int(1)];
                            float U_S139_0 = U_S70_0 * U_S135_0[int(1)];
                            DiffPair_float_0_0 U_S140_0;
                            U_S140_0.primal_0_0 = U_S93_0;
                            U_S140_0.differential_0_0 = 0.0;
                            s_bwd_prop_sin_0_0(U_S140_0, U_S139_0);
                            float U_S141_0 = U_S70_0 * U_S135_0[int(0)];
                            float U_S142_0 = U_S94_0 * U_S135_0[int(0)];
                            DiffPair_float_0_0 U_S143_0;
                            U_S143_0.primal_0_0 = U_S93_0;
                            U_S143_0.differential_0_0 = 0.0;
                            s_bwd_prop_cos_0_0(U_S143_0, U_S141_0);
                            float U_S144_0 = U_S138_0 + U_S142_0;
                            DiffPair_float_0_0 U_S145_0;
                            U_S145_0.primal_0_0 = r_5_0;
                            U_S145_0.differential_0_0 = 0.0;
                            s_bwd_prop_sin_0_0(U_S145_0, U_S144_0);
                            float U_S146_0 = power_1_0 * (U_S140_0.differential_0_0 + U_S143_0.differential_0_0);
                            float U_S147_0 = power_1_0 * (U_S137_0.differential_0_0 + U_S145_0.differential_0_0);
                            float U_S148_0 = U_S136_0[int(0)] + U_S136_0[int(1)] + U_S136_0[int(2)];
                            DiffPair_float_0_0 U_S149_0;
                            U_S149_0.primal_0_0 = U_S95_0;
                            U_S149_0.differential_0_0 = 0.0;
                            DiffPair_float_0_0 U_S150_0;
                            U_S150_0.primal_0_0 = power_1_0;
                            U_S150_0.differential_0_0 = 0.0;
                            s_bwd_prop_pow_0_0(U_S149_0, U_S150_0, U_S148_0);
                            float U_S151_0 = U_S96_0 * U_S124_0;
                            float U_S152_0 = power_1_0 * (_s_diff_ctx_1_0.U_S37_0[_dc_0_0] * U_S124_0);
                            DiffPair_float_0_0 U_S153_0;
                            U_S153_0.primal_0_0 = U_S95_0;
                            U_S153_0.differential_0_0 = 0.0;
                            DiffPair_float_0_0 U_S154_0;
                            U_S154_0.primal_0_0 = _S6;
                            U_S154_0.differential_0_0 = 0.0;
                            s_bwd_prop_pow_0_0(U_S153_0, U_S154_0, U_S152_0);
                            DiffPair_float_0_0 U_S155_0;
                            U_S155_0.primal_0_0 = U_S97_0;
                            U_S155_0.differential_0_0 = 0.0;
                            DiffPair_float_0_0 U_S156_0;
                            U_S156_0.primal_0_0 = U_S98_0;
                            U_S156_0.differential_0_0 = 0.0;
                            s_bwd_prop_atan2_0_0(U_S155_0, U_S156_0, U_S146_0);
                            DiffPair_float_0_0 U_S157_0;
                            U_S157_0.primal_0_0 = U_S99_0;
                            U_S157_0.differential_0_0 = 0.0;
                            s_bwd_prop_acos_0_0(U_S157_0, U_S147_0);
                            float U_S158_0 = U_S157_0.differential_0_0 / U_S100_0;
                            float3 U_S159_0 = U_S128_0 + U_S69_0;
                            float3 U_S160_0 = U_S129_0 + float3(U_S156_0.differential_0_0, U_S155_0.differential_0_0, U_S95_0 * U_S158_0);
                            float U_S161_0 = U_S151_0 + U_S126_0;
                            U_S130_0 = U_S149_0.differential_0_0 + U_S153_0.differential_0_0 + U_S101_0 * - U_S158_0 + U_S125_0;
                            z_2_1 = U_S160_0;
                            U_S131_0 = U_S161_0;
                            U_S69_1 = U_S159_0;
                            U_S134_0 = U_S91_0;
                        }
                        else
                        {
                            float3 U_S162_0 = U_S128_0 + U_S91_0;
                            U_S130_0 = U_S125_0;
                            z_2_1 = U_S129_0;
                            U_S131_0 = U_S126_0;
                            U_S69_1 = U_S69_0;
                            U_S134_0 = U_S162_0;
                        }
                        float U_S163_0;
                        float U_S164_0;
                        if(_bflag_2_0)
                        {
                            U_S163_0 = r_6_0 + U_S130_0;
                            U_S164_0 = 0.0;
                        }
                        else
                        {
                            U_S163_0 = U_S130_0;
                            U_S164_0 = r_6_0;
                        }
                        DiffPair_vectorx3Cfloatx2C3x3E_0_0 U_S165_0;
                        U_S165_0.primal_0_1 = _s_diff_ctx_1_0.U_S38_0[_dc_0_0];
                        U_S165_0.differential_0_1 = U_S71_0;
                        s_bwd_prop_length_0_0(U_S165_0, U_S163_0);
                        float3 U_S166_0 = U_S165_0.differential_0_1 + z_2_1;
                        U_S130_0 = U_S164_0;
                        dr_1_0 = U_S131_0;
                        z_2_1 = U_S166_0;
                        U_S91_0 = U_S134_0;
                    }
                    else
                    {
                        float3 U_S167_0 = U_S128_0 + U_S91_0;
                        U_S130_0 = r_6_0;
                        dr_1_0 = U_S126_0;
                        z_2_1 = U_S129_0;
                        U_S69_1 = U_S69_0;
                        U_S91_0 = U_S167_0;
                    }
                    if(U_S92_0)
                    {
                        r_7_0 = U_S127_0;
                        r_6_0 = U_S130_0;
                    }
                    else
                    {
                        r_7_0 = U_S130_0 + U_S127_0;
                        r_6_0 = 0.0;
                    }
                    int _S16 = _dc_0_0 - int(1);
                    int i_1 = i_0 + int(1);
                    if(i_1 < int(12))
                    {
                    }
                    else
                    {
                        z_2_0 = z_2_1;
                        U_S69_0 = U_S69_1;
                        break;
                    }
                    _dc_0_0 = _S16;
                    z_2_0 = z_2_1;
                    U_S69_0 = U_S69_1;
                    i_0 = i_1;
                }
                dppos_1_0.differential_0_1 = z_2_0 + U_S69_0;
                return;
            }
            
            void s_bwd_i_mandelbulb_0_0(inout DiffPair_vectorx3Cfloatx2C3x3E_0_0 U_S170_0, float U_S171_0, float U_S172_0, float U_S173_0)
            {
                s_bwd_prop_i_mandelbulb_Intermediates_0_0 U_S174_0;
                float _S17 = s_bwd_primal_i_mandelbulb_0_0(U_S170_0.primal_0_1, U_S171_0, U_S172_0, U_S174_0);
                s_bwd_prop_i_mandelbulb_0_0(U_S170_0, U_S171_0, U_S172_0, U_S173_0, U_S174_0);
                return;
            }
            
            float3 diff_mandelbulb_0_0(float3 pos_2_0, float bailout_2_0, float power_2_0)
            {
                float3 U_S176_0 = (float3)0.0;
                DiffPair_vectorx3Cfloatx2C3x3E_0_0 a_0_0;
                a_0_0.primal_0_1 = pos_2_0;
                a_0_0.differential_0_1 = U_S176_0;
                s_bwd_i_mandelbulb_0_0(a_0_0, bailout_2_0, power_2_0, 1.0);
                return a_0_0.differential_0_1;
            }











float i_mandelbulb_0(float3 pos, float bailout, float power){
    float3 z = pos;
    float dr = 2.0;
    float r = 0.0;
    for (int i = 0; i < 12; i++) {
        r = length(z);
        if (r > bailout) break;
        float theta = acos(z.z/r);
        float phi = atan2(z.y,z.x);
        dr = pow(r, power-1.0)*power*dr + 1.0;
        float zr = pow(r,power);
        theta = theta*power;
        phi = phi*power;
        z = zr*float3(sin(theta)*cos(phi), sin(phi)*sin(theta), cos(theta));
        z += pos;
    }
    return 0.5*log(r)*r/dr;
}






            float map(float3 p)
            {
                float res = 0;

                //res = pema(p / 0.25) * 0.25;

                res = i_mandelbulb_0(p / _Scale, 12, 8) * _Scale;
                //float sponge = i_menger(p, _E);
                //res = lerp(bulb, sponge, _D);

                //float plane = dot(p, float3(0, 1, 0));
                //return s_intersection(i_julia(p / _Scale) * _Scale, plane, 0.0);

                //return i_knighty(p.xzy / _Scale, _D*4) * _Scale;

                return res;
            }

            float3 HueShift (in float3 Color, in float Shift)
            {
                float3 P = float3(0.55735, 0.55735, 0.55735)*dot(float3(0.55735, 0.55735, 0.55735),Color);
                float3 U = Color-P;
                float3 V = cross(float3(0.55735, 0.55735, 0.55735),U);
                Color = U*cos(Shift*6.2832) + V*sin(Shift*6.2832) + P;
                return Color;
            }
            
            float4 frag (v2f i, float facing : VFACE) : SV_Target
            {
                float3 ro = i.ro_w;
                float3 rd = normalize(i.hitPos_w - i.ro_w);

                if (facing > 0)
                ro = i.hitPos_w;

                float3 res = march(ro, rd);
                float dist = res.x;
                float iters = res.y;
                float closest = res.z;

                //float ao = (iters / (_Iterations - 1));
                float ao = (iters / (_Iterations - 1));
                float3 norm = normal(ro + dist * rd) * 0.5 + 0.5;

                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - mul(unity_ObjectToWorld, float4(ro + dist * rd, 1)).xyz);
                float rim = 1.0 - saturate ( dot(viewDir, normal(ro + dist * rd)) );
                float rimLight = pow(rim, 1.5);

                float3 colA = HueShift(float3(1, 0, 0), ao * 0.8 + 0.5);
                float3 colB = HueShift(float3(1, 0, 0), (1.0-ao) * 0.8 + 0.2);
                float3 col = lerp(colA, colB, _A) * rimLight;

                col = normalize(diff_mandelbulb_0_0((ro + dist * rd) / _Scale, 12, 8));
                //col = norm;

                if (dist > _MaxDist)
                {
                    discard;
                }

                return float4(col, 1.0);


            }
            ENDCG
        }
    }
}
