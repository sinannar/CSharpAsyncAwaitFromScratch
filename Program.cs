﻿using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

await MyTask.Delay(1000)
    .ContinueWith(() => {Console.WriteLine("Hello");})
    .ContinueWith(() => {return MyTask.Delay(1000);})
    .ContinueWith(() => {Console.WriteLine("World");})
    .ContinueWith(() => {return MyTask.Delay(1000);})
    .ContinueWith(() => {Console.WriteLine("How");})
    .ContinueWith(() => {return MyTask.Delay(1000);})
    .ContinueWith(() => {Console.WriteLine("Is");})
    .ContinueWith(() => {return MyTask.Delay(1000);})
    .ContinueWith(() => {Console.WriteLine("It");})
    .ContinueWith(() => {return MyTask.Delay(1000);})
    .ContinueWith(() => {Console.WriteLine("Going");})
    .ContinueWith(() => {return MyTask.Delay(1000);});

// await MyTask.Delay(1000)
//     .ContinueWith(() => {Console.WriteLine("Hello");})
//     .ContinueWith(async () => {await MyTask.Delay(1000);})
//     .ContinueWith(() => {Console.WriteLine("World");})
//     .ContinueWith(async () => {await MyTask.Delay(1000);})
//     .ContinueWith(() => {Console.WriteLine("How");})
//     .ContinueWith(async () => {await MyTask.Delay(1000);})
//     .ContinueWith(() => {Console.WriteLine("Is");})
//     .ContinueWith(async () => {await MyTask.Delay(1000);})
//     .ContinueWith(() => {Console.WriteLine("It");})
//     .ContinueWith(async () => {await MyTask.Delay(1000);})
//     .ContinueWith(() => {Console.WriteLine("Going");})
//     .ContinueWith(async () => {await MyTask.Delay(1000);});

class MyTask
{
    private bool _completed;
    private Exception? _exception;
    private Action? _continuation;
    private ExecutionContext? _context;

    public struct Awaiter(MyTask t) : INotifyCompletion
    {
        public Awaiter GetAwaiter() => this;
        public bool IsCompleted => t.IsCompleted;
        public void OnCompleted(Action continuation) => t.ContinueWith(continuation);
        public void GetResult() => t.Wait();
    }
    public Awaiter GetAwaiter() => new(this);

    public bool IsCompleted
    {
        get
        {
            lock (this)
            {
                return _completed;
            }
        }
    }

    public void SetResult()
    {
        Complete(null);
    }

    public void SetException(Exception exception)
    {
        Complete(exception);
    }

    private void Complete(Exception? exception)
    {
        lock (this)
        {
            if (_completed)
            {
                throw new InvalidOperationException("Stop messing up my code");
            }
            _completed = true;
            _exception = exception;
            if (_continuation is not null)
            {
                MyThreadPool.QueueUserWorkItem(delegate
                {
                    if (_context is null)
                    {
                        _continuation();
                    }
                    else
                    {
                        ExecutionContext.Run(_context, state => ((Action)state!).Invoke(), _continuation);
                    }
                });
            }
        }
    }

    public void Wait()
    {
        ManualResetEventSlim? mres = null;
        lock (this)
        {
            if (!_completed)
            {
                mres = new ManualResetEventSlim();
                ContinueWith(mres.Set);
            }
        }
        mres?.Wait();
        if (_exception is not null)
        {
            ExceptionDispatchInfo.Throw(_exception); // does not change exception stack
        }
    }

    public MyTask ContinueWith(Action action)
    {
        MyTask t = new();

        Action callback = () =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                t.SetException(ex);
                return;
            }
            t.SetResult();
        };

        lock (this)
        {
            if (_completed)
            {
                MyThreadPool.QueueUserWorkItem(callback);
            }
            else
            {
                _continuation = callback;
                _context = ExecutionContext.Capture();
            }
        }

        return t;
    }

    public MyTask ContinueWith(Func<MyTask> action)
    {
        MyTask t = new();

        Action callback = () =>
        {
            try
            {
                MyTask next = action();
                next.ContinueWith(delegate
                {
                    if (next._exception is not null)
                    {
                        t.SetException(next._exception);
                    }
                    else
                    {
                        t.SetResult();
                    }
                });
            }
            catch (Exception ex)
            {
                t.SetException(ex);
                return;
            }
            // t.SetResult();
        };

        lock (this)
        {
            if (_completed)
            {
                MyThreadPool.QueueUserWorkItem(callback);
            }
            else
            {
                _continuation = callback;
                _context = ExecutionContext.Capture();
            }
        }

        return t;
    }

    public static MyTask Run(Action action)
    {
        MyTask task = new();

        MyThreadPool.QueueUserWorkItem(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                task.SetException(e);
                return;
            }
            task.SetResult();
        });

        return task;
    }

    public static MyTask WhenAll(List<MyTask> tasks)
    {
        MyTask t = new();

        if (tasks.Count == 0)
        {
            t.SetResult();
        }
        else
        {
            int remaining = tasks.Count;

            Action continuation = () =>
            {
                if (Interlocked.Decrement(ref remaining) == 0)
                {
                    // TODO : exceptions
                    t.SetResult();
                }
            };

            foreach (var task in tasks)
            {
                task.ContinueWith(continuation);
            }
        }

        return t;
    }

    public static MyTask Delay(int timeout)
    {
        MyTask t = new();
        new Timer(_ => t.SetResult()).Change(timeout, -1);
        return t;
    }

    public static MyTask Iterate(IEnumerable<MyTask> tasks)
    {
        MyTask t = new();

        IEnumerator<MyTask> e = tasks.GetEnumerator();

        void MoveNext()
        {
            try
            {
                if(e.MoveNext())
                {
                    MyTask next = e.Current;
                    next.ContinueWith(MoveNext);
                    return;
                }
            }
            catch (Exception ex)
            {
                t.SetException(ex);
                return;
            }
            t.SetResult();
        }

        MoveNext();

        return t;
    }
}

static class MyThreadPool
{
    private static readonly BlockingCollection<(Action, ExecutionContext?)> s_workItems = new();
    public static void QueueUserWorkItem(Action action)
    {
        s_workItems.Add((action, ExecutionContext.Capture()));
    }

    static MyThreadPool()
    {
        for (int i = 0; i < Environment.ProcessorCount; i++)
        {
            new Thread(() =>
            {
                while (true)
                {
                    (Action workItem, ExecutionContext? context) = s_workItems.Take();
                    if (context is null)
                    {
                        workItem();
                    }
                    else
                    {
                        ExecutionContext.Run(context, state => ((Action)state!).Invoke(), workItem);
                    }
                }
            })
            { IsBackground = true }.Start();
        }
    }
}
