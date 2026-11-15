import{A as e,C as t,D as n,E as r,M as i,O as a,P as o,S as s,T as c,_ as l,a as u,b as d,c as f,d as p,f as m,g as h,h as g,i as _,j as v,k as y,l as b,m as x,n as S,o as C,p as w,r as T,s as E,t as D,u as O,v as k,w as A,x as j,y as M}from"./particleInteractionYield.yB1moHrK.js";var N=o(C(),1),P=(e,t,n,r,i)=>(e-t)*(i-r)/(n-t)+r,F=class{constructor(t){this.scene=t,this.renderer=t.renderer,this.gl=this.gl,this.camera=t.camera,this.lastTime=0,this.everRendered=!1,this.ringPos=new e(0,0),this.cursorPos=new e(0,0),this.colorScheme=t.theme===`dark`?0:1,this.particleScale=this.scene.renderer.domElement.width/this.scene.pixelRatio/2e3*this.scene.particlesScale,this.createPoints(),this.init()}createPoints(){let e=!!(this.scene.isSlowConnection||this.scene.prefersReducedMotion||typeof window<`u`&&(typeof window.__agyIsSlowConnection==`function`&&window.__agyIsSlowConnection()||window.matchMedia&&window.matchMedia(`(prefers-reduced-motion: reduce)`).matches)),t=e?Math.min(this.scene.density,110):this.scene.density,n=new N.default({shape:[500,500],minDistance:P(t,0,300,10,2),maxDistance:P(t,0,300,11,3),tries:e?12:20}).fill();this.pointsData=[];for(let e=0;e<n.length;e++)this.pointsData.push(n[e][0]-250,n[e][1]-250);this.count=this.pointsData.length/2}createDataTexturePosition(){let e=new Float32Array(this.length*4);for(let t=0;t<this.count;t++){let n=t*4;e[n+0]=this.pointsData[t*2+0]*(1/250),e[n+1]=this.pointsData[t*2+1]*(1/250),e[n+2]=0,e[n+3]=0}let t=new g(e,this.size,this.size,c,h);return t.needsUpdate=!0,t}createRenderTarget(){return new i(this.size,this.size,{wrapS:n,wrapT:n,minFilter:d,magFilter:d,texture:this.posTex,format:c,type:l,depthBuffer:!1,stencilBuffer:!1})}init(){this.size=256,this.length=this.size*this.size,this.posTex=this.createDataTexturePosition(),this.rt1=this.createRenderTarget(),this.rt2=this.createRenderTarget(),this.renderer.setRenderTarget(this.rt1),this.renderer.setClearColor(0,0),this.renderer.clear(),this.renderer.setRenderTarget(this.rt2),this.renderer.setClearColor(0,0),this.renderer.clear(),this.renderer.setRenderTarget(null),this.noise=new u,this.simScene=new a,this.simCamera=new j(-1,1,1,-1,0,1),this.simMaterial=new y({uniforms:{uPosition:{value:this.posTex},uPosRefs:{value:this.posTex},uRingPos:{value:new e(0,0)},uRingRadius:{value:.2},uDeltaTime:{value:0},uRingWidth:{value:.05},uRingWidth2:{value:.015},uRingDisplacement:{value:this.scene.ringDisplacement},uTime:{value:0}},vertexShader:`
                void main() {
                    gl_Position = vec4(position, 1.0);
                }
            `,fragmentShader:`
                precision highp float;
                uniform sampler2D uPosition;
                uniform sampler2D uPosRefs;
                uniform vec2 uRingPos;
                uniform float uTime;
                uniform float uDeltaTime;
                uniform float uRingRadius;

                uniform float uRingWidth;
                uniform float uRingWidth2;
                uniform float uRingDisplacement;

                ${E.noise}

                void main() {

                    vec2 simTexCoords = gl_FragCoord.xy / vec2(${this.size.toFixed(1)}, ${this.size.toFixed(1)});
                    vec4 pFrame = texture2D(uPosition, simTexCoords);
                    // float pTime = pFrame.w - uDeltaTime;

                    float scale = pFrame.z;
                    float velocity = pFrame.w;
                    vec2 refPos = texture2D(uPosRefs, simTexCoords).xy;

                    float time = uTime * .5;
                    vec2 curentPos = refPos;

                    vec2 pos = pFrame.xy;
                    pos *= .8;

                    float dist = distance(curentPos.xy, uRingPos);
                    float noise0 = snoise(vec3(curentPos.xy * .2 + vec2(18.4924, 72.9744), time * 0.5));
                    float dist1 = distance(curentPos.xy + (noise0 * .005), uRingPos);


                    float t = smoothstep(uRingRadius - (uRingWidth * 2.), uRingRadius, dist) - smoothstep(uRingRadius, uRingRadius + uRingWidth, dist1);
                    float t2 = smoothstep(uRingRadius - (uRingWidth2 * 2.), uRingRadius, dist) - smoothstep(uRingRadius, uRingRadius + uRingWidth2, dist1);
                    float t3 = smoothstep(uRingRadius + uRingWidth2, uRingRadius, dist);

                    t = pow(t, 2.);
                    t2 = pow(t2, 3.);

                    t += t2 * 3.;
                    t += t3 * .4;
                    t += snoise(vec3(curentPos.xy * 30. + vec2(11.4924, 12.9744), time * 0.5)) * t3 * .5;

                    float nS = snoise(vec3(curentPos.xy * 2. + vec2(18.4924, 72.9744), time * 0.5));
                    t += pow((nS + 1.5) * .5, 2.) * .6;

                    // Mid scale noise
                    float noise1 = snoise(vec3(curentPos.xy * 4. + vec2(88.494, 32.4397), time * 0.35));
                    float noise2 = snoise(vec3(curentPos.xy * 4. + vec2(50.904, 120.947), time * 0.35));

                    // Close scale noise
                    float noise3 = snoise(vec3(curentPos.xy * 20. + vec2(18.4924, 72.9744), time * .5));
                    float noise4 = snoise(vec3(curentPos.xy * 20. + vec2(50.904, 120.947), time * .5));

                    // Far scale noise
                    // float noise5 = snoise(vec3(curentPos.xy * .5 + vec2(89.4924, 12.9744), time * 0.1));
                    // float noise6 = snoise(vec3(curentPos.xy * .5 + vec2(70.904, 120.947), time * 0.1));

                    vec2 disp = vec2(noise1, noise2) * .03;
                    disp += vec2(noise3, noise4) * .005;
                    // disp += vec2(noise5, noise6) * .05;

                    // Sin wave
                    disp.x += sin((refPos.x * 20.) + (time * 4.)) * .02 * clamp(dist, 0., 1.);
                    disp.y += cos((refPos.y * 20.) + (time * 3.)) * .02 * clamp(dist, 0., 1.);

                    pos -= (uRingPos - (curentPos + disp)) * pow(t2, .75) * uRingDisplacement;

                    // Add min scale
                    // t += .25;


                    // Add scale
                    float scaleDiff = t - scale;
                    scaleDiff *= .2;
                    scale += scaleDiff;


                    // Final position
                    vec2 finalPos = curentPos + disp + (pos * .25);

                    velocity *= .5;
                    velocity += scale * .25;

                    vec4 frame = vec4(finalPos, scale, velocity);

                    gl_FragColor = frame;

                }
            `});let n=new k(new t(2,2),this.simMaterial);this.simScene.add(n);let r=new p,i=new Float32Array(this.count*2),o=new Float32Array(this.count*3),s=new Float32Array(this.count*4);for(let e=0;e<this.count;e++){let t=e%this.size,n=Math.floor(e/this.size);i[e*2]=t/this.size,i[e*2+1]=n/this.size}for(let e=0;e<this.count;e++)s[e*4]=Math.random(),s[e*4+1]=Math.random(),s[e*4+2]=Math.random(),s[e*4+3]=Math.random();r.setAttribute(`position`,new O(o,3)),r.setAttribute(`uv`,new O(i,2)),r.setAttribute(`seeds`,new O(s,4)),this.renderMaterial=new y({uniforms:{uPosition:{value:this.posTex},uTime:{value:0},uColor1:{value:new w(this.scene.colorControls.color1)},uColor2:{value:new w(this.scene.colorControls.color2)},uColor3:{value:new w(this.scene.colorControls.color3)},uAlpha:{value:1},uRingPos:{value:new e(0,0)},uRez:{value:new e(this.scene.renderer.domElement.width,this.scene.renderer.domElement.height)},uParticleScale:{value:this.particleScale},uPixelRatio:{value:this.scene.pixelRatio},uColorScheme:{value:this.colorScheme}},vertexShader:`
                precision highp float;
                attribute vec4 seeds;

                uniform sampler2D uPosition;
                uniform float uTime;
                uniform float uParticleScale;
                uniform float uPixelRatio;
                uniform int uColorScheme;

                varying vec4 vSeeds;
                varying float vVelocity;
                varying vec2 vLocalPos;
                varying vec2 vScreenPos;
                varying float vScale;

                void main() {

                    vec4 pos = texture2D(uPosition, uv);
                    vSeeds = seeds;

                    vVelocity = pos.w;
                    vScale = pos.z;
                    vLocalPos = pos.xy;
                    vec4 viewSpace  = modelViewMatrix * vec4(vec3(pos.xy, 0.), 1.0);

                    gl_Position = projectionMatrix * viewSpace;
                    vScreenPos = gl_Position.xy;

                    gl_PointSize = ((vScale * 7.) * (uPixelRatio * 0.5) * uParticleScale);

                }
            `,fragmentShader:`
                precision highp float;

                varying vec4 vSeeds;
                varying vec2 vScreenPos;
                varying vec2 vLocalPos;
                varying float vScale;
                varying float vVelocity;

                uniform vec3 uColor1;
                uniform vec3 uColor2;
                uniform vec3 uColor3;

                uniform vec2 uRingPos;
                uniform vec2 uRez;

                uniform float uAlpha;
                uniform float uTime;

                uniform int uColorScheme;

                ${E.noise}

                #define PI 3.1415926535897932384626433832795

                float sdRoundBox( in vec2 p, in vec2 b, in vec4 r )
                {
                    r.xy = (p.x>0.0)?r.xy : r.zw;
                    r.x  = (p.y>0.0)?r.x  : r.y;
                    vec2 q = abs(p)-b+r.x;
                    return min(max(q.x,q.y),0.0) + length(max(q,0.0)) - r.x;
                }

                // rotate uv by angle
                vec2 rotate(vec2 v, float a) {
                    float s = sin(a);
                    float c = cos(a);
                    mat2 m = mat2(c, s, -s, c);
                    return m * v;
                }

                void main() {

                    float uBorderSize = 0.2;
                    vec2 center = vec2(.48, .4);
                    float ratio = uRez.x / uRez.y;

                    // Noise
                    float noiseAngle = snoise(vec3(vLocalPos * 10. + vec2(18.4924, 72.9744), uTime * .85));
                    float noiseColor = snoise(vec3(vLocalPos * 2. + vec2(74.664, 91.556), uTime * .5));
                    noiseColor = (noiseColor + 1.) * .5;

                    // get angle between
                    float angle = atan(vLocalPos.y - uRingPos.y, vLocalPos.x - uRingPos.x);

                    vec2 uv = gl_PointCoord.xy;
                    uv -= vec2(0.5);
                    uv.y *= -1.;
                    uv = rotate(uv, -angle + (noiseAngle * .5));

                    vec2 tuv = vScreenPos;
                    tuv = rotate(tuv, uTime * 1.);
                    tuv.y *= 1./ratio;
                    tuv += .5;

                    float h = 0.8; // adjust position of middleColor
                    float progress = smoothstep(0., .75, pow(noiseColor, 2.));
                    vec3 col = mix(mix(uColor1, uColor2, progress/h), mix(uColor2, uColor3, (progress - h)/(1.0 - h)), step(h, progress));
                    vec3 color = col;

                    float dist = sqrt(dot(uv, uv));

                    float dr = .5;
                    float t = smoothstep(dr+(uBorderSize + .0001), dr-uBorderSize, dist);
                    t = clamp(t, 0., 1.);

                    float rounded = sdRoundBox(uv, vec2(0.5, 0.2), vec4(.25));
                    rounded = smoothstep(.1, 0., rounded);

                    float a = uAlpha * rounded * smoothstep(0.1, 0.2, vScale);

                    if(a < 0.01){
                        discard;
                    }

                    color = clamp(color, 0., 1.);
                    color = mix(color, color * clamp(vVelocity, 0., 1.), float(uColorScheme));

                    gl_FragColor = vec4(color, clamp(a, 0., 1.));

                    #ifdef SRGB_TRANSFER
                        gl_FragColor = sRGBTransferOETF( gl_FragColor );
                    #endif

                }
            `,transparent:!0,depthTest:!1,depthWrite:!1}),this.mesh=new A(r,this.renderMaterial),this.mesh.position.set(0,0,0),this.mesh.scale.set(5,5,5),this.scene.scene.add(this.mesh),this.isOffscreen=!1;let c=this.scene&&this.scene.options&&this.scene.options.container||this.renderer&&this.renderer.domElement;typeof IntersectionObserver<`u`&&c&&(this.visibilityObserver=new IntersectionObserver(e=>{for(let t of e)this.isOffscreen=!t.isIntersecting},{rootMargin:`100px`}),this.visibilityObserver.observe(c))}resize(){this.renderMaterial.uniforms.uRez.value=new e(this.scene.renderer.domElement.width,this.scene.renderer.domElement.height),this.renderMaterial.uniforms.uPixelRatio.value=this.scene.pixelRatio}update(){if(this.isOffscreen&&this.everRendered){this.skippedFrame=!0,this.didUpdate=!1,this.lastTime=this.scene.clock.getElapsedTime();return}let e=typeof window<`u`&&window.innerWidth<=768;if((e||this.scene.isSlowConnection||typeof document<`u`&&(document.documentElement.dataset.connection===`slow`||document.documentElement.dataset.connection===`save-data`)||typeof window<`u`&&typeof window.__agyIsSlowConnection==`function`&&window.__agyIsSlowConnection())&&this.everRendered){if(e&&typeof document<`u`&&document.querySelector(`[data-header].menu-open`)!==null){this.skippedFrame=!0,this.didUpdate=!1,this.lastTime=this.scene.clock.getElapsedTime();return}if(this.frameSkip=(this.frameSkip||0)+1,this.frameSkip%2!=0){this.skippedFrame=!0,this.didUpdate=!1,this.lastTime=this.scene.clock.getElapsedTime();return}}this.skippedFrame=!1,this.didUpdate=!0;let t=_(this.scene.clock.getElapsedTime()-this.lastTime);this.lastTime=this.scene.clock.getElapsedTime();let n=(this.noise.getVal(this.scene.time*.66+94.234)-.5)*2,r=(this.noise.getVal(this.scene.time*.75+21.028)-.5)*2;this.cursorPos.set(n*.2,r*.1),this.scene.isIntersecting?(this.cursorPos.set(this.scene.intersectionPoint.x*.175+n*.1,this.scene.intersectionPoint.y*.175+r*.1),this.ringPos.set(this.ringPos.x+(this.cursorPos.x-this.ringPos.x)*.02,this.ringPos.y+(this.cursorPos.y-this.ringPos.y)*.02)):(this.cursorPos.set(n*.2,r*.1),this.ringPos.set(this.ringPos.x+(this.cursorPos.x-this.ringPos.x)*.01,this.ringPos.y+(this.cursorPos.y-this.ringPos.y)*.01)),this.particleScale=this.scene.renderer.domElement.width/this.scene.pixelRatio/2e3*this.scene.particlesScale,this.simMaterial.uniforms.uPosition.value=this.everRendered?this.rt1.texture:this.posTex,this.simMaterial.uniforms.uTime.value=this.scene.time,this.simMaterial.uniforms.uDeltaTime.value=t,this.simMaterial.uniforms.uRingRadius.value=.175+Math.sin(this.scene.time*1)*.03+Math.cos(this.scene.time*3)*.02,this.simMaterial.uniforms.uRingPos.value=this.ringPos,this.simMaterial.uniforms.uRingWidth.value=this.scene.ringWidth,this.simMaterial.uniforms.uRingWidth2.value=this.scene.ringWidth2,this.simMaterial.uniforms.uRingDisplacement.value=this.scene.ringDisplacement,this.renderer.setRenderTarget(this.rt2),this.renderer.render(this.simScene,this.simCamera),this.renderer.setRenderTarget(null),this.renderMaterial.uniforms.uPosition.value=this.everRendered?this.rt2.texture:this.posTex,this.renderMaterial.uniforms.uTime.value=this.scene.time,this.renderMaterial.uniforms.uRingPos.value=this.ringPos,this.renderMaterial.uniforms.uParticleScale.value=this.particleScale}postRender(){if(this.skippedFrame||this.didUpdate===!1)return;let e=this.rt1;this.rt1=this.rt2,this.rt2=e,this.everRendered=!0}kill(){this.visibilityObserver&&=(this.visibilityObserver.disconnect(),null),this.mesh.geometry.dispose(),this.mesh.material.dispose(),this.rt1.dispose(),this.rt2.dispose(),this.posTex.dispose(),this.simMaterial.dispose(),this.renderMaterial.dispose()}},I=class{constructor(n){this.loaded=!1,this.texture=null,this.options=n,this.theme=n.theme||`dark`,this.interactive=!!n.interactive,this.options.background=this.theme===`dark`?new w(0):new w(16777215),this.pixelRatio=Math.min(n.pixelRatio||window.devicePixelRatio||1,2),this.particlesScale=n.particlesScale||1,this.density=n.density||200,this.verbose=n.verbose||!1,this.scene=new a,this.scene.background=this.options.background,this.canvas=document.createElement(`canvas`),this.options.container.appendChild(this.canvas),this.canvas.width=this.options.container.offsetWidth,this.canvas.height=this.options.container.offsetHeight,this.canvasRect=null,this.rectDirty=!0,x.enabled=!1,this.renderer=new b({canvas:this.canvas,antialias:!0,alpha:!0,powerPreference:`high-performance`,preserveDrawingBuffer:!1,stencil:!1,precision:`highp`}),this.gl=this.renderer.getContext(),this.renderer.extensions.get(`EXT_color_buffer_float`),this.renderer.setSize(this.canvas.width,this.canvas.height),this.renderer.setPixelRatio(this.pixelRatio),this.onWindowResize=this.onWindowResize.bind(this),this.onWindowScroll=()=>{this.rectDirty=!0},this.initCamera(),this.initScene(),this.initEvents(),this.clock=new m,this.time=0,this.lastTime=0,this.dt=0,this.skipFrame=!1,this.isPaused=!1,this.raycaster=new r,this.mouse=new e,this.intersectionPoint=new v,this.isIntersecting=!1,this.mouseIsOver=!1,this.raycastPlane=new k(new t(12.5,12.5),new M({color:16711680,visible:!1,side:2})),this.scene.add(this.raycastPlane)}initEvents(){window.addEventListener(`resize`,this.onWindowResize,{passive:!0}),this.interactive&&window.addEventListener(`scroll`,this.onWindowScroll,{passive:!0})}onWindowResize(){this.rectDirty=!0,this.canvas.width=this.options.container.offsetWidth,this.canvas.height=this.options.container.offsetHeight,this.renderer.setSize(this.canvas.width,this.canvas.height),this.camera.aspect=this.canvas.width/this.canvas.height,this.camera.updateProjectionMatrix(),this.particles&&this.particles.resize()}initCamera(){this.camera=new s(40,this.gl.drawingBufferWidth/this.gl.drawingBufferHeight,.1,1e3),this.camera.position.z=3.1}initScene(){this.colorControls={color1:this.theme===`dark`?`#7189ff`:`#2c64ed`,color2:this.theme===`dark`?`#3074f9`:`#f84242`,color3:this.theme===`dark`?`#000000`:`#ffcf03`},this.ringWidth=this.options.ringWidth||.107,this.ringWidth2=this.options.ringWidth2||.05,this.ringDisplacement=this.options.ringDisplacement||.15,this.initParticles(),this.options.gui&&this.initGUI(),this.onLoaded()}initParticles(){this.particles=new F(this)}initGUI(){this.gui=new f({autoPlace:!1}),this.options.container.appendChild(this.gui.domElement),this.gui.domElement.style.position=`absolute`,this.gui.domElement.style.top=`0`,this.gui.domElement.style.right=`0`,this.gui.domElement.style.zIndex=`1000`;let e=this.gui.addFolder(`Colors`);e.addColor(this.colorControls,`color1`).name(`Color 1`).onChange(e=>{this.particles.renderMaterial.uniforms.uColor1.value.set(new w(e))}),e.addColor(this.colorControls,`color2`).name(`Color 2`).onChange(e=>{this.particles.renderMaterial.uniforms.uColor2.value.set(new w(e))}),e.addColor(this.colorControls,`color3`).name(`Color 3`).onChange(e=>{this.particles.renderMaterial.uniforms.uColor3.value.set(new w(e))}),e.add(this,`ringWidth`).name(`Ring Width`).min(.001).max(.2).step(.001).onChange(e=>{this.ringWidth=e}),e.add(this,`ringWidth2`).name(`Ring Width 2`).min(.001).max(.2).step(.001).onChange(e=>{this.ringWidth2=e}),e.add(this,`particlesScale`).name(`Particles Scale`).min(.1).max(2).step(.01).onChange(e=>{this.particlesScale=e}),e.add(this,`ringDisplacement`).name(`Displacement`).min(.01).max(1).step(.01).onChange(e=>{this.ringDisplacement=e}),e.add(this,`density`).name(`Density`).min(100).max(300).step(10).onChange(e=>{this.density=e,this.verbose,this.killParticles(),this.initParticles()}),e.open()}stop(){this.isPaused=!0,this.clock.stop(),this.verbose}resume(){this.isPaused=!1,this.clock.start(),this.lastTime=0,this.verbose}killParticles(){this.scene.remove(this.particles.mesh),this.particles.kill()}kill(){this.stop(),window.removeEventListener(`resize`,this.onWindowResize),window.removeEventListener(`scroll`,this.onWindowScroll),this.raycastPlane&&(this.scene.remove(this.raycastPlane),this.raycastPlane.geometry.dispose(),this.raycastPlane.material.dispose()),this.renderer&&this.renderer.dispose(),this.canvas&&this.canvas.parentElement&&this.canvas.parentElement.removeChild(this.canvas)}onLoaded(){this.loaded=!0}preRender(){let e=this.clock.getElapsedTime();if(this.dt=_(e-this.lastTime),this.lastTime=e,this.time+=this.dt,this.particles.update(),!this.interactive){this.isIntersecting=!1;return}if(this.skipFrame=!this.skipFrame,this.skipFrame||!T.hasMoved)return;(this.rectDirty||!this.canvasRect)&&(this.canvasRect=this.canvas.getBoundingClientRect(),this.rectDirty=!1);let t=this.canvasRect;if(!t.width||!t.height){this.mouseIsOver=!1,this.isIntersecting=!1;return}if(this.mouse.x=(T.cursor.x-t.left)*(T.screenWidth/t.width),this.mouse.y=(T.cursor.y-t.top)*(T.screenHeight/t.height),this.mouse.x=this.mouse.x/T.screenWidth*2-1,this.mouse.y=-(this.mouse.y/T.screenHeight)*2+1,this.mouse.x<-1||this.mouse.x>1||this.mouse.y<-1||this.mouse.y>1){this.mouseIsOver=!1,this.isIntersecting=!1;return}this.mouseIsOver=!0,this.raycaster.setFromCamera(this.mouse,this.camera);let n=this.raycaster.intersectObject(this.raycastPlane);n.length>0?(this.intersectionPoint.copy(n[0].point),this.isIntersecting=!0):this.isIntersecting=!1}render(){!this.loaded||this.isPaused||(this.preRender(),!(this.particles&&(this.particles.skippedFrame||this.particles.didUpdate===!1))&&(this.renderer.setRenderTarget(null),this.renderer.autoClear=!1,this.renderer.clear(),this.renderer.render(this.scene,this.camera),this.postRender()))}postRender(){this.particles.postRender()}},L=D(),R=()=>!!document.querySelector(`[data-custom-select-open="true"], .header.menu-open, dialog[open]`);document.addEventListener(`DOMContentLoaded`,()=>{document.querySelectorAll(`[data-main-particles-component]`).forEach(e=>{let t=e.querySelector(`[data-container]`);if(!t)return;let n=e.getAttribute(`data-theme`)||`light`,r=parseFloat(e.getAttribute(`data-ring-width`)||`0.15`),i=parseFloat(e.getAttribute(`data-ring-width2`)||`0.05`),a=parseFloat(e.getAttribute(`data-ring-displacement`)||`0.15`),o=parseInt(e.getAttribute(`data-density`)||`200`),s=parseFloat(e.getAttribute(`data-particles-scale`)||`0.75`),c=new URLSearchParams(window.location.search).get(`gui`)===`true`,l=null,u=!1,d=null,f=L.createFrameYield(),p=()=>l||(t.offsetWidth===0||t.offsetHeight===0?null:(l=new I({container:t,theme:n,particlesScale:s,density:o,interactive:!0,gui:c,verbose:!1,ringWidth:r,ringWidth2:i,ringDisplacement:a}),l)),m=()=>{if(!u||document.hidden){d=null;return}d=requestAnimationFrame(m),!(f()||S()||R())&&l?.render()},h=()=>{let e=p();e&&(e.resume(),d===null&&(d=requestAnimationFrame(m)))},g=()=>{d!==null&&(cancelAnimationFrame(d),d=null),l?.stop()},_=new IntersectionObserver(e=>{e.forEach(e=>{u=e.isIntersecting,e.isIntersecting&&!document.hidden?h():g()})},{root:null,rootMargin:`100px 0px`,threshold:0});_.observe(t);let v=()=>{document.hidden?g():u&&h()};document.addEventListener(`visibilitychange`,v),window.addEventListener(`beforeunload`,()=>{_.disconnect(),document.removeEventListener(`visibilitychange`,v),g(),l?.kill()})})});