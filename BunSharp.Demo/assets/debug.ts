declare function helloFromDotNet(name: string): string;

declare class DemoGreeter {
  constructor(name: string, payload: Uint8Array);
  static version: string;
  name: string;
  describe(): string;
}

console.log("debug.ts loaded");

const debugState = globalThis as typeof globalThis & {
  __debugCounter?: number;
  __debugProbeTick?: number;
};

let didRunDemo = false;
let remainingProbes = 20;

const probe = setInterval(() => {
  debugState.__debugProbeTick = (debugState.__debugProbeTick ?? 0) + 1;
  console.log("debug probe:", debugState.__debugProbeTick);

  debugger;

  if (!didRunDemo) {
    didRunDemo = true;

    const greeting = helloFromDotNet("debug.ts");
    console.log("greeting:", greeting);

    const greeter = new DemoGreeter("Debugger", new Uint8Array([1, 2, 3, 4]));
    console.log("greeter:", greeter.describe(), greeter.name, DemoGreeter.version);

    debugState.__debugCounter = (debugState.__debugCounter ?? 0) + 1;
    console.log("counter:", debugState.__debugCounter);
  }

  remainingProbes -= 1;
  if (remainingProbes <= 0) {
    clearInterval(probe);
    console.log("debug probes finished");
  }
}, 1000);
