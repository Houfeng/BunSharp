# BunSharp.Benchmarks

这个项目只负责性能测量，不参与示例或正确性回归。

常用命令：

```bash
dotnet run --project BunSharp.Benchmarks/BunSharp.Benchmarks.csproj -c Release
```

```bash
dotnet publish BunSharp.Benchmarks/BunSharp.Benchmarks.csproj -c Release --ucr
```

VS Code task：

- `run BunSharp.Benchmarks (Release)`
- `publish BunSharp.Benchmarks (AOT)`

当前 benchmark 覆盖：

- 属性 set/get
- 实例方法调用
- 字符串往返
- byte[] 往返
- stable getter/method 热路径