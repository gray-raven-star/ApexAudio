import {recordingScale, peakOf, peakLabel} from './waveform.mjs';

export class Timeline {
  constructor(canvas, change) {
    this.canvas = canvas; this.change = change; this.start = 0; this.end = 0; this.offset = 0; this.gain = 1; this.hapticSource = 'auto'; this.audioScale = 'auto'; this.audioGain = 1;
    new ResizeObserver(() => this.draw()).observe(canvas);
    canvas.addEventListener('pointerdown', e => { if (!this.take) return; this.drag = this.time(e); canvas.setPointerCapture(e.pointerId); this.start = this.drag; this.end = this.drag; this.draw(); });
    canvas.addEventListener('pointermove', e => { if (this.drag == null) return; const t = this.time(e); this.start = Math.min(t,this.drag); this.end = Math.max(t,this.drag); this.draw(); this.change(this.start,this.end); });
    canvas.addEventListener('pointerup', () => { if (this.drag == null) return; if (this.end-this.start < .02) this.end = Math.min(this.take.duration,this.start+1); this.drag = null; this.draw(); this.change(this.start,this.end); });
  }
  set(take, track) { const fresh = take?.id !== this.take?.id || take?.waveformVersion !== this.take?.waveformVersion; this.take = take; this.track = track; if (fresh) { this.autoAudioGain = recordingScale(take); this.start = 0; this.end = Math.min(8,take?.duration || 0); this.change(this.start,this.end); } this.draw(); }
  range(start,end) { if (!this.take) return; this.start = Math.max(0,Math.min(start,this.take.duration)); this.end = Math.max(this.start,Math.min(end,this.take.duration)); this.draw(); }
  time(e) { const r = this.canvas.getBoundingClientRect(); return Math.max(0,Math.min(this.take.duration,(e.clientX-r.left-49)/(r.width-68)*this.take.duration)); }
  draw() {
    const c = this.canvas, w = c.clientWidth, h = c.clientHeight; if (!w || !h) return;
    const ratio = devicePixelRatio || 1; c.width = w*ratio; c.height = h*ratio;
    const ctx = c.getContext('2d'); ctx.scale(ratio,ratio); ctx.clearRect(0,0,w,h); if (!this.take) return;
    const left = 49, right = w-19, duration = Math.max(.01,this.take.duration), x = t => left+t/duration*(right-left);
    const light=document.documentElement.dataset.theme==='light';
    ctx.font = '10px "Segoe UI", sans-serif'; ctx.fillStyle = light?'#536d89':'#8297b5'; ctx.strokeStyle = light?'#d1ddea':'#23334a'; ctx.lineWidth = 1;
    const step = duration > 60 ? 10 : duration > 20 ? 5 : duration > 8 ? 2 : 1;
    for (let t = 0; t <= duration; t += step) { const xx=x(t); ctx.beginPath(); ctx.moveTo(xx,23); ctx.lineTo(xx,h-26); ctx.stroke(); ctx.fillText(t.toFixed(0)+'s',xx-6,h-10); }
    const lanes = [{label:'声音',y:65,color:light?'#008c91':'#50d1d1'}, {label:'左握把',y:127,color:light?'#9d50c5':'#d58cf8'}, {label:'右握把',y:185,color:light?'#7652cc':'#9c91ff'}, {label:'扳机',y:241,color:light?'#b15d85':'#e7a8cf'}];
    for (const lane of lanes) { ctx.fillStyle = light?'#536d89':'#8b9db6'; ctx.fillText(lane.label,7,lane.y+3); ctx.strokeStyle=light?'#cbd8e7':'#2c3e57'; ctx.beginPath(); ctx.moveTo(left,lane.y); ctx.lineTo(right,lane.y); ctx.stroke(); }
    this.audioGain = this.audioScale === 'auto' ? (this.autoAudioGain ?? 1) : recordingScale(this.take, this.audioScale);
    this.displayChanged?.({gain:this.audioGain, peak:peakOf(this.track), label:peakLabel(peakOf(this.track))});
    const peaks = this.track?.peaks || []; ctx.strokeStyle=lanes[0].color;
    for (let px = 0; px < right-left; px++) {
      const a = Math.floor(px/(right-left)*duration*100), b = Math.max(a+1,Math.ceil((px+1)/(right-left)*duration*100)); let peak=0;
      for(let i=a;i<b&&i<peaks.length;i++) peak=Math.max(peak,peaks[i]);
      const size=Math.min(1,peak*this.audioGain)*23; if(size>0) {ctx.beginPath();ctx.moveTo(left+px,65-size);ctx.lineTo(left+px,65+size);ctx.stroke();}
    }
    const blocks = this.take.haptics?.blocks || [];
    const supplement = blocks.some(b=>b.supplementLeft!=null||b.supplementRight!=null||b.supplementTrigger!=null);
    const mode = this.hapticSource === 'auto' ? (supplement ? 'supplement' : 'final') : this.hapticSource;
    const legacyPoll = this.take.haptics?.note?.startsWith('正式 Engine 诊断峰值');
    const discrete = legacyPoll || this.take.haptics?.samplingMethod === 'instantaneous-poll';
    const drawSeries = (keys,muted) => keys.forEach((key,index) => {
      const color = muted ? (light?'#627288':'#a4b1c5') : lanes[index+1].color;
      ctx.strokeStyle = color; ctx.fillStyle = color+'30'; ctx.lineWidth = muted ? 1 : 2;
      for(const block of blocks) {
        if(block[key] == null || block[key] <= 0) continue;
        const time = block.seconds+(legacyPoll ? block.span : 0)+this.offset/1000;
        const a=x(time), b=discrete ? a+2 : x(time+block.span); if(b<left||a>right) continue;
        const size=Math.min(1,block[key]*this.gain)*19, from=Math.max(left,a), to=Math.min(right,Math.max(a+1,b)), y=lanes[index+1].y;
        ctx.fillRect(from,y-size,to-from,size);ctx.beginPath();ctx.moveTo(from,y-size);ctx.lineTo(to,y-size);ctx.stroke();
      }
    });
    if(mode==='final'||mode==='both')drawSeries(['left','right','trigger'],mode==='both');
    if(mode==='supplement'||mode==='both')drawSeries(['supplementLeft','supplementRight','supplementTrigger'],false);
    ctx.lineWidth = 1;
    ctx.fillStyle='#4d8dff20'; ctx.fillRect(x(this.start),23,x(this.end)-x(this.start),h-49); ctx.strokeStyle='#77aeff';
    for(const t of [this.start,this.end]){ctx.beginPath();ctx.moveTo(x(t),23);ctx.lineTo(x(t),h-26);ctx.stroke();}
  }
}
