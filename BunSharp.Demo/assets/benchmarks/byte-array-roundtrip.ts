const byteArrayRoundtripFrameBudgetMs = 1000 / 120;
const byteArrayRoundtripIterations = 64;
const byteArrayRoundtripPayload = new Uint8Array(256 * 1024);
const ByteArrayRoundtripBenchmarkBridge = globalThis.BenchmarkBridge;

if (typeof ByteArrayRoundtripBenchmarkBridge !== 'function') {
  throw new Error('BenchmarkBridge is not available on globalThis.');
}

const byteArrayRoundtripHost = new ByteArrayRoundtripBenchmarkBridge();

for (let index = 0; index < byteArrayRoundtripPayload.length; index++) {
  byteArrayRoundtripPayload[index] = index & 0xff;
}

function byteArrayRoundtripRound() {
  let checksum = 0;
  for (let index = 0; index < byteArrayRoundtripIterations; index++) {
    const returned = byteArrayRoundtripHost.echoBytes(byteArrayRoundtripPayload);
    checksum += returned[index & 0xff] + returned.length;
  }
  return checksum;
}

byteArrayRoundtripRound();
const byteArrayRoundtripStart = performance.now();
const byteArrayRoundtripChecksum = byteArrayRoundtripRound();
const byteArrayRoundtripElapsedMs = performance.now() - byteArrayRoundtripStart;
const byteArrayRoundtripMsPerCall = byteArrayRoundtripElapsedMs / byteArrayRoundtripIterations;
const byteArrayRoundtripTransferredMiB = (byteArrayRoundtripPayload.byteLength * byteArrayRoundtripIterations * 2) / (1024 * 1024);

globalThis.__benchmarkResult = JSON.stringify({
  benchmark: 'byte-array-roundtrip',
  frameBudgetMs: Number(byteArrayRoundtripFrameBudgetMs.toFixed(3)),
  iterations: byteArrayRoundtripIterations,
  payloadKiB: byteArrayRoundtripPayload.byteLength / 1024,
  elapsedMs: Number(byteArrayRoundtripElapsedMs.toFixed(3)),
  msPerRoundtrip: Number(byteArrayRoundtripMsPerCall.toFixed(3)),
  roundtripsPerFrameAt120FPS: Math.floor(byteArrayRoundtripFrameBudgetMs / byteArrayRoundtripMsPerCall),
  effectiveMiBPerSecond: Number((byteArrayRoundtripTransferredMiB / (byteArrayRoundtripElapsedMs / 1000)).toFixed(3)),
  checksum: byteArrayRoundtripChecksum,
});