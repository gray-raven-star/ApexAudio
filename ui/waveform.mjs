export function peakOf(track) {
  let maximum = 0;
  for (const value of track?.peaks || []) if (Number.isFinite(value)) maximum = Math.max(maximum, value);
  return maximum;
}

export function recordingScale(take, mode = 'auto') {
  if (mode !== 'auto') return [1, 4, 10, 25, 100].includes(Number(mode)) ? Number(mode) : 1;
  let maximum = 0;
  for (const track of take?.tracks || []) maximum = Math.max(maximum, peakOf(track));
  return maximum > 0 ? Math.min(1000, Math.max(1, .9 / maximum)) : 1;
}

export function peakLabel(peak) { return peak > 0 ? (20 * Math.log10(peak)).toFixed(1) + ' dBFS' : '静音'; }
