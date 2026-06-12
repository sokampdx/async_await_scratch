using System.Collections.Concurrent;

AsyncLocal<int> myVal = new();
for (int i = 0; i < 1000; i++)
{
    myVal.Value = i;
    MyThreadPool.QueueUserWorkItem(delegate
    {
        Console.WriteLine(myVal.Value);
        Thread.Sleep(1000);
    });
    
}

Console.ReadLine();

static class MyThreadPool
{
    private static readonly BlockingCollection<(Action, ExecutionContext?)> s_workItems = new();
    public static void QueueUserWorkItem(Action action) => s_workItems.Add((action, ExecutionContext.Capture()));

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
                        ExecutionContext.Run(context, state => ((Action) state!).Invoke(), workItem);
                    }
                }
            })
            { IsBackground = true }.Start();
        }
    }
}