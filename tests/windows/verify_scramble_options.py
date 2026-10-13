"""Execute grimenc source functions and compare the three actual engines."""
from pathlib import Path
import copy,hashlib,json,os,random,re,shutil,struct,subprocess,uuid,zlib
from verify_apng import EXE,decode_rgba
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'build/verification'/('reference-'+uuid.uuid4().hex);OUT.mkdir(parents=True)
NODE=os.environ.get('AIPT_NODE','node')
def run(args):
 p=subprocess.run(list(map(str,args)),capture_output=True,timeout=180)
 assert p.returncode==0,p.stdout.decode(errors='replace')+p.stderr.decode(errors='replace')
 return p.stdout.decode()
def signed(x):return x-0x100000000 if x>=0x80000000 else x
rng=random.Random(87015);samples=[]
for w,h in [(1,1),(1,31),(29,1),(3,5),(37,19),(19,37),(33,35),(128,97),(127,129),(256,256)]:
 pixels=[signed(rng.getrandbits(32)) for _ in range(w*h)]
 for strength in [i/10 for i in range(21)]:
  for reverse in [False,True]:
   o=dict(Version=1,Algorithm=0,Variant=0,Strength=strength,Block=16,Seed='',Key='',Width=w,Height=h,Green=False,ReduceGreen=False)
   samples.append(dict(Width=w,Height=h,Pixels=pixels,Options=o,Reverse=reverse))
# Original hue/rounding/clipping formula; full RGB lattice and alpha boundaries.
colors=[signed((a<<24)|(r<<16)|(g<<8)|b) for r in range(0,256,17) for g in range(0,256,17) for b in range(0,256,17) for a in [0,127,255]]
for strength in [0,.1,1,1.7,2]:
 for reverse,green,reduce in [(False,True,False),(True,False,True),(False,True,True),(True,True,True)]:
  o=dict(Version=1,Algorithm=0,Variant=0,Strength=strength,Block=16,Seed='',Key='',Width=96,Height=128,Green=green,ReduceGreen=reduce)
  samples.append(dict(Width=96,Height=128,Pixels=colors,Options=o,Reverse=reverse))
extra=[]
for sample in samples:
 if (not sample['Options']['Green'] and sample['Width'] in [3,37,33,128] and sample['Options']['Strength'] in [.1,1,1.9]):
  counts=[2,3,7,20]
 elif sample['Options']['Green'] or sample['Options']['ReduceGreen']:counts=[2,3]
 else:continue
 for count in counts:
  repeated=copy.deepcopy(sample);repeated['Options']['Rounds']=count;extra.append(repeated)
samples.extend(extra)
(OUT/'input.json').write_text(json.dumps(samples),encoding='utf-8')
reference=(ROOT/'tests/fixtures/algorithm-baseline.js').read_text(encoding='utf-8')
html=(ROOT/'obfuscation/index.html').read_text(encoding='utf-8');core=re.search(r'<script id="core">(.*?)</script>',html,re.S)[1]
assert reference.strip() in core,'Web functions must remain verbatim reference source'
js=r'''
const fs=require('fs'),vm=require('vm');let core=vm.runInNewContext(CORE+';({transform})');let cases=JSON.parse(fs.readFileSync(process.argv[2]));
for(let s of cases){let w=s.Width,h=s.Height,o=s.Options,rgba=new Uint8ClampedArray(s.Pixels.length*4);s.Pixels.forEach((v,i)=>{rgba[i*4]=v>>>16&255;rgba[i*4+1]=v>>>8&255;rgba[i*4+2]=v&255;rgba[i*4+3]=v>>>24;});let input=rgba.slice();const curve=gilbert2dIterative(w,h),totalPixels=w*h,offset=Math.round(o.Strength*(Math.sqrt(5)-1)/2*totalPixels);for(let round=0;round<(o.Rounds||1);round++){if(!s.Reverse&&o.Green)input=adjustGreenColor(input,true);let result=new Uint8ClampedArray(input.length);for(let i=0;i<totalPixels;i++){let old_pos=curve[i],new_pos=curve[(i+offset)%totalPixels],old_p=4*(old_pos[0]+old_pos[1]*w),new_p=4*(new_pos[0]+new_pos[1]*w),src=s.Reverse?new_p:old_p,dst=s.Reverse?old_p:new_p;for(let j=0;j<4;j++)result[dst+j]=input[src+j];}if(s.Reverse&&o.ReduceGreen)result=adjustGreenColor(result,false);input=result;}let actual=core.transform(new Uint32Array(rgba.buffer),w,h,o,s.Reverse),want=new Uint32Array(input.buffer);if(actual.some((x,i)=>x!==want[i]))throw Error('Web/reference difference');s.Pixels=Array.from(want,v=>(v&0xff00ff00)|((v&255)<<16)|(v>>>16&255));}fs.writeFileSync(process.argv[3],JSON.stringify(cases));
'''
(OUT/'reference.cjs').write_text(reference+'\nconst CORE='+json.dumps(core)+';\n'+js,encoding='utf-8')
run([NODE,OUT/'reference.cjs',OUT/'input.json',OUT/'reference.json'])
expected=json.loads((OUT/'reference.json').read_text(encoding='utf-8'))
run([EXE,'--scramble-vectors',OUT/'input.json',OUT/'csharp.json'])
cs=json.loads((OUT/'csharp.json').read_text(encoding='utf-8-sig'))
assert len(cs)==len(expected)
for i,(a,b) in enumerate(zip(cs,expected)):assert a['Pixels']==b['Pixels'],f'C#/actual grimenc difference in {i}'
# Real Android engine classes; only bitmap/IO wrappers are replaced on host JVM.
package='com.guangyingruoshui.imagetools';source=ROOT/'android/src'/package.replace('.','/')
for name in ['ScrambleEngine.java','ReferenceColors.java','ReferenceCurve.java']:shutil.copyfile(source/name,OUT/name)
images=(source/'Images.java').read_text(encoding='utf-8');curve=images[images.index('    static final class Curve'):images.index('    static Bitmap transform')]
(OUT/'Images.java').write_text('package '+package+';import java.io.*;class Images{'+curve+'}',encoding='utf-8')
(OUT/'Files.java').write_text('package '+package+';import java.io.*;import java.nio.charset.*;class Files{static Charset UTF8=StandardCharsets.UTF_8,LATIN=StandardCharsets.ISO_8859_1;static class Job{void check(){}}static void le(byte[] a,int p,long x,int n){for(int i=0;i<n;i++)a[p+i]=(byte)(x>>>(8*i));}static long le(byte[] a,int p,int n){long x=0;for(int i=0;i<n;i++)x|=(a[p+i]&255L)<<(8*i);return x;}static byte[] read(RandomAccessFile f,int n)throws Exception{byte[] b=new byte[n];f.readFully(b);return b;}}',encoding='utf-8')
with (OUT/'input.bin').open('wb') as f:
 f.write(struct.pack('>i',len(samples)))
 for s in samples:
  o=s['Options'];f.write(struct.pack('>iiid???',s['Width'],s['Height'],o.get('Rounds',1),o['Strength'],s['Reverse'],o['Green'],o['ReduceGreen']));f.write(struct.pack('>'+str(len(s['Pixels']))+'i',*s['Pixels']))
(OUT/'Runner.java').write_text('package '+package+''';import java.io.*;import java.util.*;class Runner{public static void main(String[] args)throws Exception{try(DataInputStream in=new DataInputStream(new FileInputStream(args[0]));DataOutputStream out=new DataOutputStream(new FileOutputStream(args[1]))){int count=in.readInt();for(int k=0;k<count;k++){int w=in.readInt(),h=in.readInt();ScrambleEngine.Options o=new ScrambleEngine.Options();o.Rounds=in.readInt();o.Strength=in.readDouble();boolean reverse=in.readBoolean();o.Green=in.readBoolean();o.ReduceGreen=in.readBoolean();int[] p=new int[w*h];for(int i=0;i<p.length;i++)p[i]=in.readInt();int[] r=ScrambleEngine.transform(p.clone(),w,h,o,reverse,new Files.Job());if(!reverse&&!o.Green){int[] b=ScrambleEngine.transform(r.clone(),w,h,o,true,new Files.Job());if(!Arrays.equals(p,b))throw new Exception("Java inverse");}for(int x:r)out.writeInt(x);}}}}''',encoding='utf-8')
jdk=Path(os.environ['AIPT_JAVA_HOME'])/'bin';sdk=Path(os.environ['AIPT_SDK_ROOT'])/'platforms/android-35/android.jar';(OUT/'classes').mkdir()
run([jdk/'javac.exe','-encoding','UTF-8','-cp',sdk,'-d',OUT/'classes',*OUT.glob('*.java')]);run([jdk/'java.exe','-cp',str(OUT/'classes')+os.pathsep+str(sdk),package+'.Runner',OUT/'input.bin',OUT/'java.bin'])
b=(OUT/'java.bin').read_bytes();at=0
for i,s in enumerate(expected):
 n=len(s['Pixels']);assert list(struct.unpack_from('>'+str(n)+'i',b,at))==s['Pixels'],f'Android/actual grimenc difference in {i}';at+=n*4
assert at==len(b)
def chunk(k,p):return struct.pack('>I',len(p))+k+p+struct.pack('>I',zlib.crc32(k+p))
def png(w,h,p):
 raw=b''.join(b'\0'+p[y*w*4:(y+1)*w*4] for y in range(h));return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,h,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(raw))+chunk(b'IEND',b'')
def decode(p):
 b=p.read_bytes();at=8;idat=[]
 while at<len(b):
  n=struct.unpack_from('>I',b,at)[0];k=b[at+4:at+8];v=b[at+8:at+8+n];assert zlib.crc32(k+v)==struct.unpack_from('>I',b,at+8+n)[0]
  if k==b'IHDR':ihdr=v
  if k==b'IDAT':idat.append(v)
  at+=n+12
  if k==b'IEND':break
 assert at==len(b),'Private trailing attachment or parameters'
 return decode_rgba(ihdr,b''.join(idat))
folder=ROOT/'build/verification/cross-files';folder.mkdir(exist_ok=True)
rgba=bytes((255 if i%4==3 else (i*73+37)%256) for i in range(37*19*4));source=folder/'source.png';source.write_bytes(png(37,19,rgba))
for strength in [0,.5,1,1.5,2]:
 o=dict(Strength=strength,Rounds=3);op=OUT/'options.json';op.write_text(json.dumps(o));enc=OUT/f'repeat-{strength}.png';dec=OUT/f'restored-{strength}.png'
 run([EXE,'--reference-options',source,enc,0,op,'false','false',OUT/'cli.json']);decode(enc)
 run([EXE,'--reference-options',enc,dec,0,op,'true','false',OUT/'cli.json']);assert decode(dec)==((37,19),rgba)
 shutil.copyfile(enc,folder/enc.name)
large=OUT/'large.png';large.write_bytes(png(1280,1920,bytes([121,78,40,255])*1280*1920));run([EXE,'--reference-options',large,OUT/'large-output.png',0,op,'false','false',OUT/'cli.json']);assert decode(OUT/'large-output.png')[0]==(1280,1920)
report=dict(result='PASS',vectors=len(samples),reference='https://github.com/gascy37/grimenc/blob/main/index.html',reference_functions_sha256=hashlib.sha256(reference.encode()).hexdigest(),checks=['Verbatim reference functions embedded in web','21 coefficients, both directions, 1/2/3/7/20 rounds, odd/thin/single pixels','Original HSL green formulas, clipping, alpha, processing order','Exact C#/Android/JavaScript pixels against executed original reference','Five actual three-round plain PNG round trips, no private footer','1280 x 1920 export preserves original dimensions'],limits=['No Bilibili posting/acceptance-rate test','Native encoders can produce different PNG/JPEG file bytes','Browser canvas alpha/color rounding follows the reference'])
(OUT/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');(ROOT/'build/verification/reference-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps(report,ensure_ascii=True));print('Artifacts:',OUT)
