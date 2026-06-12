for (int i = 0; i < 1000; i++)
{
    int localVal = i;
    ThreadPool.QueueUserWorkItem(delegate
    {
        Console.WriteLine(localVal); // print all 1000s
        Thread.Sleep(1000);
    });
    
}

Console.ReadLine();