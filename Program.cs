using System.Collections.Concurrent;

for (int i = 0; i < 1000; i++)
{
    int capturedValue = i;
    MyThreadPool.QueueUserWorkItem(delegate {
        Console.WriteLine(capturedValue);
        Thread.Sleep(1000);
    });
}
Console.ReadLine();

static class MyThreadPool
{
    private static readonly BlockingCollection<Action> s_workItems = new();
    public static void QueueUserWorkItem(Action action)
    {
        s_workItems.Add(action);
    }

    static MyThreadPool()
    {
        for (int i = 0; i < Environment.ProcessorCount; i++)
        {
            new Thread(() => {
                while(true){
                    var workItem = s_workItems.Take();
                    workItem();
                }
            })
            {IsBackground = true}.Start();
        }
    }
}
