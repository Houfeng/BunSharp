const stableCacheFrameBudgetMs = 1000 / 120;
const stableCacheIterations = 200_000;
const StableCacheBenchmarkBridge = globalThis.StableCacheBenchmarkBridge;

if (typeof StableCacheBenchmarkBridge !== 'function') {
  throw new Error('StableCacheBenchmarkBridge is not available on globalThis.');
}

const stableCacheHost = new StableCacheBenchmarkBridge();

function stableGetterHitRound() {
  let checksum = 0;
  stableCacheHost.setAlternate(false);
  for (let index = 0; index < stableCacheIterations; index++) {
    checksum += stableCacheHost.items.length;
  }
  return checksum;
}

function stableGetterChurnRound() {
  let checksum = 0;
  for (let index = 0; index < stableCacheIterations; index++) {
    stableCacheHost.setAlternate((index & 1) !== 0);
    checksum += stableCacheHost.items.length;
  }
  return checksum;
}

function stableMethodHitRound() {
  let checksum = 0;
  for (let index = 0; index < stableCacheIterations; index++) {
    checksum += stableCacheHost.getItems(false).length;
  }
  return checksum;
}

function stableMethodChurnRound() {
  let checksum = 0;
  for (let index = 0; index < stableCacheIterations; index++) {
    checksum += stableCacheHost.getItems((index & 1) !== 0).length;
  }
  return checksum;
}

function measure(round) {
  round();
  const startedAt = performance.now();
  const checksum = round();
  const elapsedMs = performance.now() - startedAt;
  return {
    elapsedMs,
    nsPerOp: Math.round((elapsedMs / stableCacheIterations) * 1_000_000),
    opsPerFrameAt120FPS: Math.floor(stableCacheFrameBudgetMs / (elapsedMs / stableCacheIterations)),
    checksum,
  };
}

const stableGetterHit = measure(stableGetterHitRound);
const stableGetterChurn = measure(stableGetterChurnRound);
const stableMethodHit = measure(stableMethodHitRound);
const stableMethodChurn = measure(stableMethodChurnRound);

globalThis.__benchmarkResult = JSON.stringify({
  benchmark: 'stable-cache-hot-path',
  frameBudgetMs: Number(stableCacheFrameBudgetMs.toFixed(3)),
  iterations: stableCacheIterations,
  getterHit: {
    elapsedMs: Number(stableGetterHit.elapsedMs.toFixed(3)),
    nsPerOp: stableGetterHit.nsPerOp,
    opsPerFrameAt120FPS: stableGetterHit.opsPerFrameAt120FPS,
    checksum: stableGetterHit.checksum,
  },
  getterChurn: {
    elapsedMs: Number(stableGetterChurn.elapsedMs.toFixed(3)),
    nsPerOp: stableGetterChurn.nsPerOp,
    opsPerFrameAt120FPS: stableGetterChurn.opsPerFrameAt120FPS,
    checksum: stableGetterChurn.checksum,
  },
  getterHotPathSpeedupVsChurn: Number((stableGetterChurn.nsPerOp / stableGetterHit.nsPerOp).toFixed(2)),
  methodHit: {
    elapsedMs: Number(stableMethodHit.elapsedMs.toFixed(3)),
    nsPerOp: stableMethodHit.nsPerOp,
    opsPerFrameAt120FPS: stableMethodHit.opsPerFrameAt120FPS,
    checksum: stableMethodHit.checksum,
  },
  methodChurn: {
    elapsedMs: Number(stableMethodChurn.elapsedMs.toFixed(3)),
    nsPerOp: stableMethodChurn.nsPerOp,
    opsPerFrameAt120FPS: stableMethodChurn.opsPerFrameAt120FPS,
    checksum: stableMethodChurn.checksum,
  },
  methodHotPathSpeedupVsChurn: Number((stableMethodChurn.nsPerOp / stableMethodHit.nsPerOp).toFixed(2)),
});