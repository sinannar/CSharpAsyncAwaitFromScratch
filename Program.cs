using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
Console.WriteLine($"ThreadPool number is : {Environment.ProcessorCount}");

AsyncLocal<int> myValue = new();
List<MyTask> tasks = new();
for (int i = 0; i < 10; i++)
{
    myValue.Value = i;
    tasks.Add(MyTask.Run(delegate
    {
        Console.WriteLine(myValue.Value);
        Thread.Sleep(1000);
    }));
}
foreach (var task in tasks)
{
    task.Wait();
}

class MyTask
{
    private bool _completed;
    private Exception? _exception;
    private Action? _continuation;
    private ExecutionContext? _context;

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

    public void ContinueWith(Action action)
    {
        lock (this)
        {
            if (_completed)
            {
                MyThreadPool.QueueUserWorkItem(action);
            }
            else
            {
                _continuation = action;
                _context = ExecutionContext.Capture();
            }
        }
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
