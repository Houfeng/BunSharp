const s = typeof Promise;
Object.create(null)
// @ts-ignore
globalThis.__t = s + String(Object.create({}));
console.log('from ts:', s);

console.time('loop2');
let t = 0;
for (let i = 0; i <= 1000000; i++) {
  t += i;
}
console.timeEnd('loop2');