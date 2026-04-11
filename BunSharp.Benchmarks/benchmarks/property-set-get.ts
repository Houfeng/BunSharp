const propertySetGetFrameBudgetMs = 1000 / 120;
const propertySetGetIterations = 100_000;
const PropertySetGetBenchmarkBridge = globalThis.BenchmarkBridge;

if (typeof PropertySetGetBenchmarkBridge !== 'function') {
  throw new Error('BenchmarkBridge is not available on globalThis.');
}

const propertySetGetHost = new PropertySetGetBenchmarkBridge();

function propertySetGetRound() {
  let checksum = 0;
  for (let index = 0; index < propertySetGetIterations; index++) {
    propertySetGetHost.counter = index;
    checksum += propertySetGetHost.counter;
  }
  return checksum;
}

propertySetGetRound();
const propertySetGetStart = performance.now();
const propertySetGetChecksum = propertySetGetRound();
const propertySetGetElapsedMs = performance.now() - propertySetGetStart;
const propertySetGetMsPerPair = propertySetGetElapsedMs / propertySetGetIterations;

globalThis.__benchmarkResult = JSON.stringify({
  benchmark: 'property-set-get',
  frameBudgetMs: Number(propertySetGetFrameBudgetMs.toFixed(3)),
  iterations: propertySetGetIterations,
  elapsedMs: Number(propertySetGetElapsedMs.toFixed(3)),
  nsPerSetGetPair: Math.round(propertySetGetMsPerPair * 1_000_000),
  setGetPairsPerFrameAt120FPS: Math.floor(propertySetGetFrameBudgetMs / propertySetGetMsPerPair),
  frameCostFor10kPairsMs: Number((propertySetGetMsPerPair * 10_000).toFixed(3)),
  checksum: propertySetGetChecksum,
});