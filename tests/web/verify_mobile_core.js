// Packed storage must preserve the unmodified traversal and all original pixel operations.
const fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
const root=path.resolve(__dirname,'../..'),html=fs.readFileSync(path.join(root,'obfuscation/index.html'),'utf8');
const current=vm.createContext({});vm.runInContext(html.match(/<script id="core">([\s\S]*?)<\/script>/)[1],current);
const original=vm.createContext({});vm.runInContext(fs.readFileSync(path.join(root,'tests/fixtures/algorithm-baseline.js'),'utf8'),original);
let count=0;
for(let w=1;w<=24;w++)for(let h=1;h<=24;h++){
 const baseline=original.gilbert2dIterative(w,h).map(p=>p[0]+p[1]*w),packed=Array.from(current.curve(w,h));
 if(JSON.stringify(baseline)!==JSON.stringify(packed))throw Error(`Path differs: ${w}x${h}`);
 for(const strength of [0,.3,1,1.7,2])for(const rounds of [1,2,3])for(const green of [false,true]){
  const pixels=Uint32Array.from({length:w*h},(_,i)=>(0xff000000|((i*73)&255)<<16|((i*19)&255)<<8|((i*37)&255))>>>0);
  const reference=original.gilbert2dIterative(w,h);let rgba=new Uint8ClampedArray(pixels.buffer.slice(0));
  for(let round=0;round<rounds;round++){
   if(green)rgba=original.adjustGreenColor(rgba,true);
   const offset=Math.round(strength*(Math.sqrt(5)-1)/2*w*h),out=new Uint8ClampedArray(rgba.length);
   for(let i=0;i<w*h;i++){const a=reference[i],b=reference[(i+offset)%(w*h)];out.set(rgba.subarray(4*(a[0]+a[1]*w),4*(a[0]+a[1]*w)+4),4*(b[0]+b[1]*w));}rgba=out;
  }
  const o={Version:1,Algorithm:0,Strength:strength,Rounds:rounds,Green:green,ReduceGreen:false};
  const actual=current.transform(pixels,w,h,o,false);
  if(!Buffer.from(actual.buffer).equals(Buffer.from(rgba.buffer)))throw Error(`Pixels differ: ${w}x${h}/${strength}/${rounds}/${green}`);
  if(!green&&!Buffer.from(current.transform(actual,w,h,o,true).buffer).equals(Buffer.from(pixels.buffer)))throw Error('Reverse differs');
 }
 count++;
}
const large=current.curve(4032,3024);if(large.length!==12192768||large.byteLength!==48771072)throw Error('Large photo path allocation differs');
console.log(`PASS: ${count} traversal sizes, strengths/rounds/green output parity and inverse, packed 12 MP path uses ${large.byteLength} bytes`);
