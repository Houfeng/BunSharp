
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace BunSharp;

/// <summary>
/// 
/// </summary> 
internal sealed class BunSynchronizationContext() : SynchronizationContext {
  private readonly ConcurrentQueue<(SendOrPostCallback callback, object? state)> _queue = new();
  private readonly Thread _ownerThread = Thread.CurrentThread;

  public override void Post(SendOrPostCallback callback, object? state) {
    _queue.Enqueue((callback, state));
  }

  public override void Send(SendOrPostCallback callback, object? state) {
    if (Thread.CurrentThread == _ownerThread) {
      callback(state); // 直接执行，避免死锁
      return;
    }
    var wait = new ManualResetEvent(false);
    Exception? ex = null;
    Post(_ => {
      try {
        callback(state);
      } catch (Exception e) {
        ex = e;
      }
      finally {
        wait.Set();
      }
    }, null);
    wait.WaitOne(); // 阻塞等待
    if (ex != null) throw ex;
  }

  internal void DoQueueWorks() {
    if (Thread.CurrentThread != _ownerThread) {
      const string msg = "Only available in the initial thread";
      throw new InvalidOperationException(msg);
    }
    while (_queue.TryDequeue(out var work)) {
      work.callback(work.state);
    }
  }
}