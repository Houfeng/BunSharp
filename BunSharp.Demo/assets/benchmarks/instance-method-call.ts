const instanceMethodFrameBudgetMs = 1000 / 120;
const instanceMethodIterations = 100_000;
const InstanceMethodBenchmarkBridge = globalThis.BenchmarkBridge;

if (typeof InstanceMethodBenchmarkBridge !== 'function') {
  throw new Error('BenchmarkBridge is not available on globalThis.');
}

const instanceMethodHost = new InstanceMethodBenchmarkBridge();

function instanceMethodRound() {
  let checksum = 0;
  for (let index = 0; index < instanceMethodIterations; index++) {
    checksum += instanceMethodHost.add(index, 1);
  }
  return checksum;
}

instanceMethodRound();
const instanceMethodStart = performance.now();
const instanceMethodChecksum = instanceMethodRound();
const instanceMethodElapsedMs = performance.now() - instanceMethodStart;
const instanceMethodMsPerCall = instanceMethodElapsedMs / instanceMethodIterations;

globalThis.__benchmarkResult = JSON.stringify({
  benchmark: 'instance-method-call',
  frameBudgetMs: Number(instanceMethodFrameBudgetMs.toFixed(3)),
  iterations: instanceMethodIterations,
  elapsedMs: Number(instanceMethodElapsedMs.toFixed(3)),
  nsPerCall: Math.round(instanceMethodMsPerCall * 1_000_000),
  callsPerFrameAt120FPS: Math.floor(instanceMethodFrameBudgetMs / instanceMethodMsPerCall),
  frameCostFor10kCallsMs: Number((instanceMethodMsPerCall * 10_000).toFixed(3)),
  checksum: instanceMethodChecksum,
});