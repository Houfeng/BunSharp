declare class DemoGreeter {
  constructor(name: string, payload: Uint8Array);
  static version: string;
  name: string;
  describe(): string;
}

debugger;

const greeter = new DemoGreeter("Debugger", new Uint8Array([1, 2, 3, 4]));
console.log("greeter:", greeter.describe(), greeter.name, DemoGreeter.version);
