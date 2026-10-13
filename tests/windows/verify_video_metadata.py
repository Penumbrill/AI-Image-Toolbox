"""Generate tagged videos with FFmpeg; verify actual Windows/Java cleaners using ffprobe and encoded/decoded stream hashes.
Set AIPT_FFMPEG, AIPT_FFPROBE and AIPT_JAVA_HOME. Tools are test dependencies only.
"""
from pathlib import Path
import os,subprocess,json,shutil,hashlib
r=Path(__file__).resolve().parents[2];out=r/'build/verification/video-clean';out.mkdir(parents=True,exist_ok=True)
ff=os.environ['AIPT_FFMPEG'];probe=os.environ['AIPT_FFPROBE'];jdk=Path(os.environ['AIPT_JAVA_HOME'])/'bin';exe=r/'build/windows/AI-Image-Editing-Tools.exe'
def run(args,ok=True):
 p=subprocess.run(list(map(str,args)),capture_output=True,timeout=120)
 if ok:assert p.returncode==0,p.stderr.decode(errors='replace')+p.stdout.decode(errors='replace')
 return p
base=out/'base.mp4'
run([ff,'-v','error','-y','-f','lavfi','-i','testsrc2=size=96x64:rate=12','-f','lavfi','-i','sine=frequency=440:sample_rate=48000','-t','1','-c:v','libx264','-pix_fmt','yuv420p','-c:a','aac',base])
metadata=['-metadata','comment={"workflow":{"nodes":[{"type":"KSampler"}]}},parameters: Steps: 20, Model: TEST_ONLY','-metadata','workflow=COMFYUI_WORKFLOW_TEST_ONLY','-metadata','parameters=WEBUI_PROMPT_TEST_ONLY','-metadata','title=PRIVATE_GENERATION_TITLE','-metadata','creation_time=2026-01-01T00:00:00Z']
cases=[]
for name,extra in [('plain.mp4',[]),('mdta.mp4',['-movflags','use_metadata_tags']),('faststart.mp4',['-movflags','+faststart']),('rotated.mov',['-metadata:s:v:0','rotate=90']),('video.mkv',[])]:
 path=out/name;run([ff,'-v','error','-y','-i',base,'-map','0','-c','copy',*metadata,*extra,path]);cases.append(path)
path=out/'video.webm';run([ff,'-v','error','-y','-i',base,'-c:v','libvpx-vp9','-c:a','libopus',*metadata,path]);cases.append(path)
# JSON workflow attachment as emitted by some custom nodes.
workflow=out/'workflow.json';workflow.write_text('{"workflow":"ATTACHMENT_PRIVATE_TEST_ONLY"}')
path=out/'attachment.mkv';run([ff,'-v','error','-y','-i',base,'-map','0','-c','copy',*metadata,'-attach',workflow,'-metadata:s:t','mimetype=application/json',path]);cases.append(path)
pkg='com.guangyingruoshui.imagetools';shutil.copyfile(r/'android/src'/pkg.replace('.','/')/'VideoMetadata.java',out/'VideoMetadata.java')
(out/'Files.java').write_text('package '+pkg+''';import java.io.*;import java.nio.charset.*;class Files{static Charset LATIN=StandardCharsets.ISO_8859_1;static class Job{boolean cancelled;void check()throws IOException{if(cancelled)throw new InterruptedIOException();}}static File unique(File d,String n){d.mkdirs();return new File(d,n);}static File temp(File d)throws Exception{return File.createTempFile("video",".tmp",d);}static String stem(String s){return s.substring(0,s.lastIndexOf('.'));}static void space(File d,long n){}static void copy(InputStream i,OutputStream o,Job j)throws Exception{byte[] b=new byte[8192];int n;while((n=i.read(b))>0){j.check();o.write(b,0,n);}}static void commit(File a,File b,Job j)throws Exception{j.check();if(!a.renameTo(b))throw new IOException("move");}}''','utf-8')
(out/'Runner.java').write_text('package '+pkg+''';import java.io.*;class Runner{public static void main(String[] a)throws Exception{Files.Job j=new Files.Job();if(a.length>2)j.cancelled=true;VideoMetadata.clean(new File(a[0]),new File(a[1]),j);}}''','utf-8');classes=out/'classes';classes.mkdir(exist_ok=True);run([jdk/'javac.exe','-encoding','UTF-8','-d',classes,*out.glob('*.java')])
def inspect(path):return json.loads(run([probe,'-v','error','-show_format','-show_streams','-of','json',path]).stdout)
def hashes(path,decode=False):return run([ff,'-v','error','-i',path,'-map','0:v','-map','0:a?',*(['-c:v','rawvideo','-c:a','pcm_s16le'] if decode else ['-c','copy']),'-f','streamhash','-hash','sha256','-']).stdout
reports=[]
for path in cases:
 before=inspect(path);folder=out/('cs-'+path.stem);folder.mkdir(exist_ok=True);javafolder=out/('java-'+path.stem);javafolder.mkdir(exist_ok=True)
 for stale in list(folder.iterdir())+list(javafolder.iterdir()):stale.unlink()
 run([exe,'--video-clean-test',path,folder]);run([jdk/'java.exe','-cp',classes,pkg+'.Runner',path,javafolder]);clean=next(folder.glob('*'+path.suffix));java=next(javafolder.glob('*'+path.suffix));assert java.read_bytes()==clean.read_bytes(),'Java/Windows output differs'
 after=inspect(clean);assert clean.stat().st_size==path.stat().st_size
 for stream in after['streams']:
  if stream['codec_type'] in ['video','audio']:
   original=next(s for s in before['streams'] if s['index']==stream['index']);fields=['codec_name','width','height','sample_rate','channels','r_frame_rate','time_base','duration','side_data_list'];assert {f:original.get(f) for f in fields}=={f:stream.get(f) for f in fields},(path,original,stream)
 tags=json.dumps(after).lower();assert not any(x in tags for x in ['comfyui_workflow_test_only','webui_prompt_test_only','private_generation_title','ksampler','attachment_private_test_only'])
 assert hashes(path)==hashes(clean),'Encoded A/V packet hashes changed';assert hashes(path,True)==hashes(clean,True),'Decoded A/V hashes changed'
 reports.append(dict(File=path.name,MetadataRemoved=True,AudioVideoPacketsIdentical=True,DecodedIdentical=True,JavaWindowsIdentical=True))
# Malformed/truncated input must fail atomically; cancellation must leave no output.
for name,data in [('broken.mp4',base.read_bytes()[:-17]),('broken.webm',(out/'video.webm').read_bytes()[:80])]:
 path=out/name;path.write_bytes(data);folder=out/('bad-'+name);folder.mkdir(exist_ok=True)
 for stale in folder.iterdir():stale.unlink()
 assert run([exe,'--video-clean-test',path,folder],False).returncode!=0;assert not list(folder.iterdir())
 assert run([jdk/'java.exe','-cp',classes,pkg+'.Runner',path,folder],False).returncode!=0;assert not list(folder.iterdir())
folder=out/'cancel';folder.mkdir(exist_ok=True);assert run([jdk/'java.exe','-cp',classes,pkg+'.Runner',base,folder,'cancel'],False).returncode!=0;assert not list(folder.iterdir())
report=dict(Status='PASS',Cases=reports,CorruptInputAtomic=True,JavaCancellation=True,PhysicalAndroid=False);(out/'report.json').write_text(json.dumps(report,indent=2),'utf-8');print('PASS: seven tagged videos, actual C#/Java byte parity, A/V packet + decoded hashes, rotation, corrupt input and cancellation')
