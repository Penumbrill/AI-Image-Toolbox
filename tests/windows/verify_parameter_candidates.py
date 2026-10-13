"""Actual C#/Android Java/Web joint restoration search; no private metadata used."""
from pathlib import Path
import subprocess, json, re, struct, os, shutil, random
r=Path(__file__).resolve().parents[2];out=r/'build/verification/parameter-search';out.mkdir(parents=True,exist_ok=True)
exe=r/'build/windows/AI-Image-Editing-Tools.exe';node=os.environ.get('AIPT_NODE','node');jdk=Path(os.environ['AIPT_JAVA_HOME'])/'bin'
def run(a):
 p=subprocess.run(list(map(str,a)),capture_output=True,timeout=120);assert p.returncode==0,p.stdout.decode(errors='replace')+p.stderr.decode(errors='replace')
def signed(n):return n-2**32 if n>=2**31 else n
samples=[]
for w,h,strength,rounds in [(1,1,1,1),(37,19,.3,1),(96,128,1.7,3),(99,31,.5,2),(77,101,1,7),(1,97,2,1)]:
 pixels=[signed(0xff000000|(int(x*220/max(1,w-1))<<16)|(int(y*220/max(1,h-1))<<8)|int((x+y)*110/max(1,w+h-2))) for y in range(h) for x in range(w)]
 samples.append(dict(Width=w,Height=h,Pixels=pixels,Options=dict(Strength=strength,Rounds=rounds)))
(out/'original.json').write_text(json.dumps(samples),'utf-8');run([exe,'--scramble-vectors',out/'original.json',out/'encoded.json']);enc=json.loads((out/'encoded.json').read_text('utf-8-sig'))
for s in enc:s['Options']={'Strength':1,'Rounds':1}
(out/'search.json').write_text(json.dumps(enc),'utf-8');run([exe,'--parameter-vectors',out/'search.json',out/'csharp.json']);expected=json.loads((out/'csharp.json').read_text('utf-8-sig'))
core=re.search(r'<script id="core">(.*?)</script>',(r/'obfuscation/index.html').read_text('utf-8'),re.S)[1]
(out/'runner.cjs').write_text("const fs=require('fs'),vm=require('vm'),core=vm.runInNewContext("+json.dumps(core)+"+';({detectParameters})');let result=JSON.parse(fs.readFileSync(process.argv[2])).map(s=>core.detectParameters(Uint32Array.from(s.Pixels,v=>(v&0xff00ff00)|((v&255)<<16)|(v>>>16&255)),s.Width,s.Height,1,1).slice(0,3));fs.writeFileSync(process.argv[3],JSON.stringify(result));",'utf-8');run([node,out/'runner.cjs',out/'search.json',out/'web.json']);assert json.loads((out/'web.json').read_text())==expected
# Reuse test-only IO/Bitmap wrappers; execute real Java algorithms.
base=r/'build/verification/android-cover-trial';src=r/'android/src/com/guangyingruoshui/imagetools';pkg='com.guangyingruoshui.imagetools'
for name in ['ScrambleEngine.java','ReferenceCurve.java','ReferenceColors.java','ScrambleRounds.java']:shutil.copyfile(src/name,out/name)
for name in ['Files.java','Images.java']:shutil.copyfile(base/name,out/name)
(out/'Runner.java').write_text('package '+pkg+''';import java.io.*;class Runner{public static void main(String[] a)throws Exception{try(DataInputStream in=new DataInputStream(new FileInputStream(a[0]));DataOutputStream out=new DataOutputStream(new FileOutputStream(a[1]))){for(int n=in.readInt();n>0;n--){int w=in.readInt(),h=in.readInt();int[] p=new int[w*h];for(int i=0;i<p.length;i++)p[i]=in.readInt();ScrambleRounds.Candidate[] c=ScrambleRounds.rankParameters(p,w,h,1,1,new Files.Job());out.writeInt(Math.min(3,c.length));for(int i=0;i<Math.min(3,c.length);i++){out.writeInt(c[i].rounds);out.writeDouble(c[i].strength);out.writeDouble(c[i].score);}}}}}''','utf-8')
with (out/'input.bin').open('wb') as f:
 f.write(struct.pack('>i',len(enc)))
 for s in enc:f.write(struct.pack('>ii',s['Width'],s['Height'])+struct.pack('>'+str(len(s['Pixels']))+'i',*s['Pixels']))
classes=out/'classes';classes.mkdir(exist_ok=True);sdk=r/'build/toolchain/sdk/platforms/android-35/android.jar';run([jdk/'javac.exe','-encoding','UTF-8','-cp',sdk,'-d',classes,*out.glob('*.java')]);run([jdk/'java.exe','-cp',str(classes)+os.pathsep+str(sdk),pkg+'.Runner',out/'input.bin',out/'java.bin']);b=(out/'java.bin').read_bytes();at=0
for group in expected:
 count=struct.unpack_from('>i',b,at)[0];at+=4;assert count==len(group)
 for c in group:
  rounds,strength,score=struct.unpack_from('>idd',b,at);at+=20;assert dict(Rounds=rounds,Strength=strength,Score=score)==c
assert at==len(b)
# Equivalent permutations count as valid: factor/rounds can be mathematically ambiguous.
import math
for original,group in zip(samples,expected):
 n=original['Width']*original['Height'];offset=lambda strength,rounds:math.floor(strength*(math.sqrt(5)-1)/2*n+.5)*rounds%n
 wanted=offset(original['Options']['Strength'],original['Options']['Rounds']);assert any(offset(c['Strength'],c['Rounds'])==wanted for c in group),group
(out/'report.json').write_text(json.dumps(dict(Status='PASS',Cases=len(samples),ExactThreePlatformCandidateParity=True,KnownPermutationInTopThree=True,PhysicalAndroid=False),indent=2),'utf-8');print('PASS: joint shift/round candidates match C#, actual Android Java and Web; known permutations in top three')
