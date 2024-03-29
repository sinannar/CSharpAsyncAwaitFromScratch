
#region V1
for (int i = 0; i < 1000; i++)
{
    ThreadPool.QueueUserWorkItem(delegate {
        Console.WriteLine(i);
        Thread.Sleep(1000);
    });
}
Console.ReadLine();
/*
Output
...
1000
1000
1000
1000
1000
1000
1000
1000
...
*/ 
#endregion
