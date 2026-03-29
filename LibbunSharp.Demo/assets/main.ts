const t = typeof Promise;
Object.create(null)
// @ts-ignore
globalThis.__t = t + String(Object.create({}));
console.log('from ts:', t);