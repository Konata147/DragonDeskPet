param([string]$OutputPath = 'dist/pet-interactions-v2-check/preview.html')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$manifest = Join-Path $root 'assets/character/animations/clips.json'
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputPath))
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
$clips = Get-Content -LiteralPath $manifest -Raw -Encoding utf8
$html = @'
<!doctype html>
<html lang="zh-CN"><meta charset="utf-8"><title>DragonDeskPet 动作预览</title>
<style>
body{margin:0;background:#eee9f6;color:#3b2e50;font:16px "Segoe UI",sans-serif;display:grid;place-items:center;min-height:100vh}
main{background:#fff;border-radius:20px;padding:22px;box-shadow:0 10px 45px #56477533;max-width:800px}
h1{font-size:20px;margin:0 0 12px}button{border:1px solid #b9a3d4;border-radius:8px;background:#f8f3ff;color:#463258;padding:8px 12px;cursor:pointer;margin:3px}
.row{display:flex;gap:22px;align-items:flex-start;flex-wrap:wrap}.stage{width:512px;height:512px;position:relative;background:#f2eef8;border-radius:12px}
.stage #pet{width:512px;height:512px;position:absolute;inset:0}.stage #companion{position:absolute;left:378px;top:157px;width:55px;height:44px;pointer-events:none;background-size:124px 124px;background-position:-36px -43px;background-repeat:no-repeat;transform-origin:center}.guide{position:absolute;left:0;right:0;top:495px;border-top:1px dashed #7d53a8;pointer-events:none}
.controls{width:230px}.controls label{display:block;margin:10px 0}.small{font-size:12px;color:#766a86}#status{min-height:24px}
.hide-guide .guide{display:none}
</style>
<main><h1>DragonDeskPet 动作预览</h1><div class="row"><div class="stage" id="stage"><img id="pet" alt="龙娘桌宠动作帧"><div id="companion" role="img" aria-label="小 AI"></div><div class="guide"></div></div><div class="controls"><button id="all">连续播放全部互动</button><div id="buttons"></div><label><input id="showGuide" type="checkbox" checked> 显示脚底参考线</label><label><input id="showLabel" type="checkbox"> 显示动作名称</label><p id="status"></p><p class="small">逐帧透明 PNG；小 AI 的表情及一次性动作按运行节奏示意。可隐藏动作名称，只看姿势和过程。</p></div></div></main>
<script>
const clips = __CLIPS__;
const byId = Object.fromEntries(clips.map(c => [c.Id, c]));
const actions = ['Hover','Greet','Pet','Feed','Cuddle','Hop','Dance','Stretch','LookAround','Land','Sleep','Wake','Celebrate'];
const labels = ['指向时“嗯？”','打招呼','摸摸头','喂点心','贴贴','蹦一蹦','晃晃舞','伸懒腰','张望','拖动落地','打个盹','叫醒','庆祝'];
const prefix = '../../assets/character/animations/';
const cloudPrefix = '../../assets/character/companion/';
const pet = document.getElementById('pet'), companion = document.getElementById('companion'), status = document.getElementById('status');
let token = 0;
function mood(name){companion.style.backgroundImage=`url('${cloudPrefix+name}.png')`;companion.dataset.mood=name}
function frame(path){pet.src=prefix+path;companion.style.display=path.startsWith('Sleep/')||path==='Wake/00.png'?'none':''}
function react(id){
  const face = ({Hover:'curious',Pet:'blink',Feed:'curious',Hop:'curious',Stretch:'thinking',LookAround:'curious',Land:'curious',Wake:'curious'})[id]||'happy';
  mood(face);
  companion.getAnimations().forEach(a=>a.cancel());
  const poses = ({Greet:[0,-7,0,-7,0],Pet:[0,3,0],Feed:[0,4,-6,0],Cuddle:[0,-3,0],Hop:[0,-5,-15,-7,0],Dance:[0,-4,0,-4,0],Stretch:[0,-4,0],LookAround:[0,-3,0],Land:[0,4,0],Wake:[0,-7,0],Celebrate:[0,-9,0,-9,0],Hover:[0,-4,0]})[id];
  if(poses) companion.animate(poses.map(y=>({transform:`translateY(${y}px)`})),{duration:id==='Hop'?1250:750,easing:'ease-in-out'});
}
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function play(id, sequence){
  const clip=byId[id]; if(!clip)return;
  const own=sequence ?? ++token;
  status.textContent=document.getElementById('showLabel').checked ? id : '';
  react(id);
  for(let i=0;i<clip.Frames.length;i++){
    if(own!==token)return;
    frame(clip.Frames[i]); await delay(clip.DurationsMs[i]);
  }
  if(own===token){frame(id==='Hover' ? 'Hover/03.png' : id==='Sleep' ? 'Sleep/00.png' : 'Blink/00.png'); if(id!=='Sleep')mood(id==='Hover'?'curious':'normal'); status.textContent='';}
}
actions.forEach((id,i)=>{
  const b=document.createElement('button');b.textContent=labels[i];b.onclick=()=>play(id);
  document.getElementById('buttons').appendChild(b);
});
document.getElementById('all').onclick=async()=>{
  const own=++token;
  for(const id of actions){if(own!==token)return;await play(id,own);await delay(350)}
  if(own===token)status.textContent=document.getElementById('showLabel').checked ? '全部播放完成' : '';
};
document.getElementById('showGuide').onchange=e=>document.getElementById('stage').classList.toggle('hide-guide',!e.target.checked);
mood('normal');frame('Blink/00.png');
</script></html>
'@
[IO.File]::WriteAllText($output, $html.Replace('__CLIPS__', $clips), [Text.UTF8Encoding]::new($false))
Write-Host $output
