using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using FanucNav.Fanuc;

namespace FanucNav
{
    public static class Viewer3D
    {
        private static HttpListener _http;
        private static string _meshRoot;
        private static string _writeRoot;
        private static string _prefix = "http://127.0.0.1:18765/";
        private static string _sceneJson = "{}";

        public static void Open(RobotIdent ident, DcsConfig dcs, IList<CartPose> path)
        {
            _meshRoot = FindModelRoot();
            if (string.IsNullOrEmpty(_meshRoot) || !File.Exists(Path.Combine(_meshRoot, "base_link.stl")))
                throw new InvalidOperationException(
                    "ROS meshes not found. Expected models\\r2000ic270f\\*.stl next to FanucNav.dll\r\nLooked in:\r\n" + _meshRoot);

            _writeRoot = Path.Combine(Path.GetTempPath(), "FanucNav3D");
            Directory.CreateDirectory(_writeRoot);
            File.WriteAllText(Path.Combine(_writeRoot, "viewer.html"), Html, Encoding.UTF8);
            _sceneJson = BuildScene(ident, dcs, path);
            File.WriteAllText(Path.Combine(_writeRoot, "scene.json"), _sceneJson, Encoding.UTF8);
            StartServer();
            Process.Start(new ProcessStartInfo
            {
                FileName = _prefix + "viewer.html",
                UseShellExecute = true
            });
        }

        private static string FindModelRoot()
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string p = Path.Combine(dir ?? "", "models", "r2000ic270f");
            if (Directory.Exists(p)) return p;
            p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "r2000ic270f");
            return p;
        }

        private static void StartServer()
        {
            if (_http != null && _http.IsListening) return;
            Exception last = null;
            int[] ports = new int[] { 18765, 18766, 18767, 28765 };
            foreach (int port in ports)
            {
                try
                {
                    var http = new HttpListener();
                    string pfx = "http://127.0.0.1:" + port + "/";
                    http.Prefixes.Add(pfx);
                    http.Start();
                    _http = http;
                    _prefix = pfx;
                    last = null;
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }
            if (_http == null)
                throw new InvalidOperationException(
                    "Could not start local 3D server on localhost. " +
                    (last != null ? last.Message : ""));

            ThreadPool.QueueUserWorkItem(delegate
            {
                while (_http != null && _http.IsListening)
                {
                    try { Serve(_http.GetContext()); }
                    catch { }
                }
            });
        }

        private static void Serve(HttpListenerContext ctx)
        {
            string url = ctx.Request.Url.AbsolutePath.TrimStart('/');
            if (url.IndexOf('?') >= 0) url = url.Substring(0, url.IndexOf('?'));
            url = url.Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(url) || url == "viewer.html")
                url = "viewer.html";
            byte[] data;
            string mime = "text/plain";
            if (string.Equals(url, "scene.json", StringComparison.OrdinalIgnoreCase))
            {
                data = Encoding.UTF8.GetBytes(_sceneJson ?? "{}");
                mime = "application/json";
            }
            else
            {
                string file = Path.Combine(
                    url.EndsWith(".stl", StringComparison.OrdinalIgnoreCase) ? _meshRoot : _writeRoot,
                    Path.GetFileName(url));
                if (!File.Exists(file))
                    file = Path.Combine(_meshRoot, Path.GetFileName(url));
                if (!File.Exists(file))
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }
                data = File.ReadAllBytes(file);
                if (url.EndsWith(".html")) mime = "text/html";
                else if (url.EndsWith(".stl")) mime = "application/octet-stream";
                else if (url.EndsWith(".js")) mime = "application/javascript";
            }
            ctx.Response.ContentType = mime;
            ctx.Response.ContentLength64 = data.Length;
            ctx.Response.AddHeader("Cache-Control", "no-cache");
            ctx.Response.OutputStream.Write(data, 0, data.Length);
            ctx.Response.Close();
        }

        private static string BuildScene(RobotIdent ident, DcsConfig dcs, IList<CartPose> path)
        {
            var sb = new StringBuilder();
            sb.Append("{\"model\":\"");
            sb.Append(Esc(ident != null ? ident.Model : "R-2000iC/270F"));
            sb.Append("\",\"dcsVersion\":\"");
            sb.Append(Esc(dcs != null ? dcs.Version : ""));
            sb.Append("\",\"zones\":[");
            bool first = true;
            if (dcs != null)
            {
                foreach (var z in dcs.Zones)
                {
                    if (!z.HasBox) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        "{{\"n\":{0},\"en\":{1},\"name\":\"{2}\",\"min\":[{3},{4},{5}],\"max\":[{6},{7},{8}]}}",
                        z.Number, z.Enabled ? "true" : "false", Esc(z.Comment),
                        Math.Min(z.X1, z.X2) / 1000.0, Math.Min(z.Y1, z.Y2) / 1000.0, Math.Min(z.Z1, z.Z2) / 1000.0,
                        Math.Max(z.X1, z.X2) / 1000.0, Math.Max(z.Y1, z.Y2) / 1000.0, Math.Max(z.Z1, z.Z2) / 1000.0);
                }
            }
            sb.Append("],\"userModel\":[");
            first = true;
            if (dcs != null)
            {
                foreach (var e in dcs.Elements)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        "{{\"shape\":\"{0}\",\"size\":{1},\"a\":[{2},{3},{4}],\"b\":[{5},{6},{7}]}}",
                        Esc(e.Shape), e.Size / 1000.0,
                        e.X1 / 1000.0, e.Y1 / 1000.0, e.Z1 / 1000.0,
                        e.X2 / 1000.0, e.Y2 / 1000.0, e.Z2 / 1000.0);
                }
            }
            sb.Append("],\"path\":[");
            first = true;
            if (path != null)
            {
                foreach (var p in path)
                {
                    if (!p.HasJoints && !p.HasCart) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"name\":\"").Append(Esc(p.Name)).Append("\"");
                    if (p.HasJoints && p.Joints != null && p.Joints.Length >= 6)
                    {
                        sb.AppendFormat(CultureInfo.InvariantCulture,
                            ",\"q\":[{0},{1},{2},{3},{4},{5}]",
                            p.Joints[0], p.Joints[1], p.Joints[2], p.Joints[3], p.Joints[4], p.Joints[5]);
                    }
                    if (p.HasCart)
                    {
                        sb.AppendFormat(CultureInfo.InvariantCulture,
                            ",\"xyz\":[{0},{1},{2}]", p.X / 1000.0, p.Y / 1000.0, p.Z / 1000.0);
                    }
                    sb.Append('}');
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private const string Html = @"<!DOCTYPE html>
<html><head>
<meta charset='utf-8'/>
<title>FanucNav 3D — ROS R-2000iC/270F</title>
<style>
html,body{margin:0;height:100%;font-family:Segoe UI,Arial,sans-serif;background:#1b1e23;color:#eee;overflow:hidden}
#bar{position:absolute;left:0;right:0;top:0;height:46px;background:#111;display:flex;align-items:center;gap:8px;padding:0 12px;z-index:2}
button{background:#e8b923;border:0;padding:6px 12px;cursor:pointer;font-weight:600}
button.sec{background:#333;color:#eee}
#info{font-size:12px;opacity:.85;margin-left:8px}
#c{position:absolute;top:46px;left:0;right:0;bottom:0}
</style>
</head><body>
<div id='bar'>
  <button id='play'>Play program</button>
  <button id='pause' class='sec'>Pause</button>
  <button id='home' class='sec'>Home</button>
  <button id='dcs' class='sec'>Toggle DCS</button>
  <span id='info'>loading…</span>
</div>
<div id='c'></div>
<script type='importmap'>
{""imports"":{""three"":""https://unpkg.com/three@0.160.0/build/three.module.js"",""three/addons/"":""https://unpkg.com/three@0.160.0/examples/jsm/""}}
</script>
<script type='module'>
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { STLLoader } from 'three/addons/loaders/STLLoader.js';

const jointsDef = [
  {xyz:[0,0,0.67], axis:[0,0,1]},
  {xyz:[0.312,0,0], axis:[0,1,0]},
  {xyz:[0,0,1.075], axis:[0,-1,0]},
  {xyz:[0,0,0.225], axis:[-1,0,0]},
  {xyz:[1.280,0,0], axis:[0,-1,0]},
  {xyz:[0.24,0,0], axis:[-1,0,0]}
];
const files = ['base_link.stl','link_1.stl','link_2.stl','link_3.stl','link_4.stl','link_5.stl','link_6.stl'];

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x2a3038);
const cam = new THREE.PerspectiveCamera(50, 1, 0.05, 80);
cam.position.set(4.5, 3.2, 3.2);
const renderer = new THREE.WebGLRenderer({antialias:true});
document.getElementById('c').appendChild(renderer.domElement);
const controls = new OrbitControls(cam, renderer.domElement);
controls.target.set(0.8, 0, 1.0);
scene.add(new THREE.HemisphereLight(0xffffff, 0x445566, 1.1));
const sun = new THREE.DirectionalLight(0xffffff, 0.85);
sun.position.set(3, 4, 6);
scene.add(sun);
scene.add(new THREE.GridHelper(12, 24, 0x667788, 0x3a4048));
scene.add(new THREE.AxesHelper(1.2));

function resize(){
  const w = window.innerWidth, h = window.innerHeight-46;
  cam.aspect = w/h; cam.updateProjectionMatrix();
  renderer.setSize(w,h);
}
window.addEventListener('resize', resize); resize();

const yellow = new THREE.MeshStandardMaterial({color:0xe6c200, metalness:0.25, roughness:0.55});
const gray = new THREE.MeshStandardMaterial({color:0x6a6e74, metalness:0.3, roughness:0.6});
const black = new THREE.MeshStandardMaterial({color:0x222226, metalness:0.4, roughness:0.5});

const loader = new STLLoader();
const nodes = [];
let root = new THREE.Group();
scene.add(root);

function loadMesh(name, mat){
  return new Promise((res,rej)=>{
    loader.load(name, geo=>{
      geo.computeVertexNormals();
      res(new THREE.Mesh(geo, mat));
    }, undefined, rej);
  });
}

const q = [0,0,0,0,0,0];
let sceneData = {zones:[], path:[], userModel:[]};
let dcsGroup = new THREE.Group();
scene.add(dcsGroup);
let showDcs = true;
let playing = false, playT = 0, playSeg = 0;

function applyJoints(){
  let node = root;
  for(let i=0;i<6;i++){
    node = nodes[i+1];
    if(!node) continue;
    const ax = new THREE.Vector3().fromArray(jointsDef[i].axis);
    node.quaternion.setFromAxisAngle(ax, q[i]*Math.PI/180);
  }
}

function setQ(nq){ for(let i=0;i<6;i++) q[i]=nq[i]||0; applyJoints(); }

async function boot(){
  const base = await loadMesh(files[0], gray);
  root.add(base);
  nodes[0] = root;
  let parent = root;
  for(let i=0;i<6;i++){
    const pivot = new THREE.Group();
    pivot.position.fromArray(jointsDef[i].xyz);
    parent.add(pivot);
    const mesh = await loadMesh(files[i+1], i===5?black:yellow);
    pivot.add(mesh);
    nodes[i+1] = pivot;
    parent = pivot;
  }
  sceneData = await (await fetch('scene.json?t='+Date.now())).json();
  buildDcs();
  document.getElementById('info').textContent =
    (sceneData.model||'')+'  DCS '+ (sceneData.dcsVersion||'')+'  ·  ROS-Industrial r2000ic270f  ·  '+
    (sceneData.path||[]).length+' motion points  ·  drag to orbit';
}

function buildDcs(){
  dcsGroup.clear();
  for(const z of (sceneData.zones||[])){
    const min = new THREE.Vector3().fromArray(z.min);
    const max = new THREE.Vector3().fromArray(z.max);
    const size = new THREE.Vector3().subVectors(max,min);
    const geo = new THREE.BoxGeometry(Math.max(size.x,0.01), Math.max(size.y,0.01), Math.max(size.z,0.01));
    const mat = new THREE.MeshStandardMaterial({
      color: z.en ? 0xff3333 : 0x888888,
      transparent:true, opacity: z.en ? 0.22 : 0.08,
      depthWrite:false
    });
    const m = new THREE.Mesh(geo, mat);
    m.position.copy(min).add(max).multiplyScalar(0.5);
    dcsGroup.add(m);
    const box = new THREE.BoxHelper(m, z.en ? 0xff5555 : 0x666666);
    dcsGroup.add(box);
  }
  for(const e of (sceneData.userModel||[])){
    const a = new THREE.Vector3().fromArray(e.a);
    const b = new THREE.Vector3().fromArray(e.b);
    const g = new THREE.BufferGeometry().setFromPoints([a,b]);
    dcsGroup.add(new THREE.Line(g, new THREE.LineBasicMaterial({color:0x4aa3ff})));
  }
  const pts = [];
  for(const p of (sceneData.path||[])){
    if(p.xyz) pts.push(new THREE.Vector3().fromArray(p.xyz));
  }
  if(pts.length>1){
    dcsGroup.add(new THREE.Line(
      new THREE.BufferGeometry().setFromPoints(pts),
      new THREE.LineBasicMaterial({color:0xffaa00})));
  }
}

function lerp(a,b,t){ return a+(b-a)*t; }
function stepPlay(dt){
  const path = sceneData.path||[];
  if(path.length<1) return;
  const a = path[playSeg];
  const b = path[Math.min(playSeg+1, path.length-1)];
  if(!a.q){ playSeg++; return; }
  playT += dt*0.45;
  const t = Math.min(1, playT);
  const qa = a.q, qb = (b.q||a.q);
  setQ(qa.map((v,i)=>lerp(v, qb[i], t)));
  if(t>=1){ playT=0; playSeg++; if(playSeg>=path.length-1) playing=false; }
}

document.getElementById('play').onclick = ()=>{ playing=true; playSeg=0; playT=0; };
document.getElementById('pause').onclick = ()=>{ playing=false; };
document.getElementById('home').onclick = ()=>{ playing=false; setQ([0,0,0,0,0,0]); };
document.getElementById('dcs').onclick = ()=>{ showDcs=!showDcs; dcsGroup.visible=showDcs; };

let last = performance.now();
function loop(now){
  const dt = Math.min(0.05,(now-last)/1000); last=now;
  if(playing) stepPlay(dt);
  controls.update();
  renderer.render(scene,cam);
  requestAnimationFrame(loop);
}
boot().then(()=>requestAnimationFrame(loop)).catch(e=>{
  document.getElementById('info').textContent = '3D load error: '+e;
});
</script>
</body></html>";
    }
}
