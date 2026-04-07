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

// @ts-ignore
const greeter = new DemoGreeter("Debugger", new Uint8Array([1, 2, 3, 4]));
// @ts-ignore
console.log("greeter:", greeter.describe(), greeter.name, DemoGreeter.version);
