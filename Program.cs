using System.Collections.Concurrent;
Console.WriteLine($"ThreadPool number is : { Environment.ProcessorCount}");

AsyncLocal<int> myValue = new();
for (int i = 0; i < 1000; i++)
{
    myValue.Value = i;
    MyThreadPool.QueueUserWorkItem(delegate {
        Console.WriteLine(myValue.Value);
        Thread.Sleep(1000);
    });
}
Console.ReadLine();

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
            new Thread(() => {
                while(true){
                    (Action workItem, ExecutionContext? context) = s_workItems.Take();
                    if(context is null)
                    {
                        workItem();
                    }
                    else{
                        ExecutionContext.Run(context, state => ((Action)state!).Invoke() , workItem);
                    }
                }
            })
            {IsBackground = true}.Start();
        }
    }
}
