"""Compare candidate scoring across the actual C#, Java and web implementations.
Run verify_scramble_options.py first to prepare the host JVM classes.
"""
from pathlib import Path
import json,os,random,re,shutil,struct,subprocess
import gilbert2d
from verify_apng import EXE
ROOT=Path(__file__).resolve().parents[2]
OUT=max((p.parent for p in (ROOT/'build/verification').glob('reference-*/report.json')),key=lambda p:p.stat().st_mtime)
def run(args):
 p=subprocess.run(list(map(str,args)),capture_output=True,timeout=120)
 assert p.returncode==0,p.stdout.decode(errors='replace')+p.stderr.decode(errors='replace')
def signed(x):return x-0x100000000 if x>=0x80000000 else x
rng=random.Random(1593);samples=[]
for w,h in [(1,1),(37,19),(96,128)]:
 for strength in [0,1,1.7]:samples.append(dict(Width=w,Height=h,Pixels=[signed(rng.getrandbits(32)) for _ in range(w*h)],Options=dict(Strength=strength)))
w,h=96,128
pixels=[signed(0xff000000|(x*2<<16)|(y<<8)|((x+y)%256)) for y in range(h) for x in range(w)]
path=[x+y*w for x,y in gilbert2d.gilbert2d(w,h)]
import math
offset=math.floor((math.sqrt(5)-1)/2*w*h+.5)
for _ in range(2):
 result=pixels.copy()
 for i,p in enumerate(path):result[path[(i+offset)%len(path)]]=pixels[p]
 pixels=result
samples.append(dict(Width=w,Height=h,Pixels=pixels,Options=dict(Strength=1)))
(OUT/'rank.json').write_text(json.dumps(samples),encoding='utf-8')
run([EXE,'--round-vectors',OUT/'rank.json',OUT/'rank-csharp.json'])
html=(ROOT/'obfuscation/index.html').read_text(encoding='utf-8');core=re.search(r'<script id="core">(.*?)</script>',html,re.S)[1]
js="const fs=require('fs'),vm=require('vm');const core=vm.runInNewContext("+json.dumps(core)+"+';({detectRounds})');let result=JSON.parse(fs.readFileSync(process.argv[2])).map(s=>core.detectRounds(Uint32Array.from(s.Pixels,v=>(v&0xff00ff00)|((v&255)<<16)|(v>>>16&255)),s.Width,s.Height,s.Options.Strength));fs.writeFileSync(process.argv[3],JSON.stringify(result));"
(OUT/'rank.cjs').write_text(js,encoding='utf-8');run([os.environ.get('AIPT_NODE','node'),OUT/'rank.cjs',OUT/'rank.json',OUT/'rank-web.json'])
expected=json.loads((OUT/'rank-web.json').read_text(encoding='utf-8'))
assert json.loads((OUT/'rank-csharp.json').read_text(encoding='utf-8-sig'))==expected
assert any(c['Rounds']==2 for c in expected[-1][:3]),'Known two-round image missing from previews'
assert all(c['Score']==0 for c in expected[0]),'Single pixel must remain ambiguous'
package='com.guangyingruoshui.imagetools'
shutil.copyfile(ROOT/'android/src'/package.replace('.','/')/'ScrambleRounds.java',OUT/'ScrambleRounds.java')
with (OUT/'rank.bin').open('wb') as f:
 f.write(struct.pack('>i',len(samples)))
 for s in samples:
  f.write(struct.pack('>iid',s['Width'],s['Height'],s['Options']['Strength']));f.write(struct.pack('>'+str(len(s['Pixels']))+'i',*s['Pixels']))
runner='package '+package+';import java.io.*;class RankRunner{public static void main(String[] args)throws Exception{try(DataInputStream in=new DataInputStream(new FileInputStream(args[0]));DataOutputStream out=new DataOutputStream(new FileOutputStream(args[1]))){int count=in.readInt();for(int k=0;k<count;k++){int w=in.readInt(),h=in.readInt();double strength=in.readDouble();int[] p=new int[w*h];for(int i=0;i<p.length;i++)p[i]=in.readInt();for(ScrambleRounds.Candidate c:ScrambleRounds.rank(p,w,h,strength,new Files.Job())){out.writeInt(c.rounds);out.writeDouble(c.score);}}}}}'
(OUT/'RankRunner.java').write_text(runner,encoding='utf-8')
jdk=Path(os.environ['AIPT_JAVA_HOME'])/'bin';sdk=Path(os.environ['AIPT_SDK_ROOT'])/'platforms/android-35/android.jar';cp=str(OUT/'classes')+os.pathsep+str(sdk)
run([jdk/'javac.exe','-encoding','UTF-8','-cp',cp,'-d',OUT/'classes',OUT/'ScrambleRounds.java',OUT/'RankRunner.java']);run([jdk/'java.exe','-cp',cp,package+'.RankRunner',OUT/'rank.bin',OUT/'rank-java.bin'])
b=(OUT/'rank-java.bin').read_bytes();at=0
for group in expected:
 for candidate in group:
  rounds,score=struct.unpack_from('>id',b,at);at+=12;assert rounds==candidate['Rounds'] and score==candidate['Score'],'Candidate scoring differs'
assert at==len(b)
report=dict(result='PASS',cases=len(samples),checks=['Exact scores/order across C#/actual Android Java/web','Known two-round smooth image in top three','Degenerate single-pixel ambiguity retained'],limits=['Candidates require confirmation; no universal inference guarantee'])
(ROOT/'build/verification/round-candidates-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report))
