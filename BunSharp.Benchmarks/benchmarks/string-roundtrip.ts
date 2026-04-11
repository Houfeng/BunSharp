const stringRoundtripFrameBudgetMs = 1000 / 120;
const stringRoundtripIterations = 10_000;
const StringRoundtripBenchmarkBridge = globalThis.BenchmarkBridge;

if (typeof StringRoundtripBenchmarkBridge !== 'function') {
  throw new Error('BenchmarkBridge is not available on globalThis.');
}

const stringRoundtripHost = new StringRoundtripBenchmarkBridge();
const stringRoundtripSamples = [
  'button.primary|label=Save|state=enabled|theme=light',
  'button.secondary|label=Cancel|state=enabled|theme=light',
  'dialog.title|label=Preferences|state=visible|theme=light',
  'menu.item|label=Reload Window|state=hovered|theme=light',
  'toolbar.icon|label=Search|state=focused|theme=light',
  'status.badge|label=Updated|state=active|theme=light',
  'list.row|label=Document 42|state=selected|theme=light',
  'input.placeholder|label=Type to search|state=idle|theme=light',
];

function stringRoundtripRound() {
  let checksum = 0;
  for (let index = 0; index < stringRoundtripIterations; index++) {
    checksum += stringRoundtripHost.echoString(stringRoundtripSamples[index & 7]).length;
  }
  return checksum;
}

stringRoundtripRound();
const stringRoundtripStart = performance.now();
const stringRoundtripChecksum = stringRoundtripRound();
const stringRoundtripElapsedMs = performance.now() - stringRoundtripStart;
const stringRoundtripMsPerCall = stringRoundtripElapsedMs / stringRoundtripIterations;

globalThis.__benchmarkResult = JSON.stringify({
  benchmark: 'string-roundtrip',
  frameBudgetMs: Number(stringRoundtripFrameBudgetMs.toFixed(3)),
  iterations: stringRoundtripIterations,
  elapsedMs: Number(stringRoundtripElapsedMs.toFixed(3)),
  nsPerCall: Math.round(stringRoundtripMsPerCall * 1_000_000),
  callsPerFrameAt120FPS: Math.floor(stringRoundtripFrameBudgetMs / stringRoundtripMsPerCall),
  frameCostFor1kCallsMs: Number((stringRoundtripMsPerCall * 1_000).toFixed(3)),
  checksum: stringRoundtripChecksum,
});