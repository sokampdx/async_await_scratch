// https://www.youtube.com/watch?v=R-z2Hv-7nxk
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

// AsyncLocal<int> myVal = new();
// List<MyTask> tasks = new();
// for (int i = 0; i < 30; i++)
// {
//     myVal.Value = i;
//     tasks.Add(MyTask.Run(delegate
//     {
//         Console.WriteLine(myVal.Value);
//         Thread.Sleep(1000);
//     }));
    
// }
// MyTask.WhenAll(tasks).Wait();

Console.Write("Hello, ");
MyTask.Delay(2000).ContinueWith(delegate
{
    Console.Write("World!");
    return MyTask.Delay(2000);
}).ContinueWith(delegate
{
    Console.Write(" And CS!");
    return MyTask.Delay(2000);
}).ContinueWith(delegate
{
    Console.Write(" How are you?");
}).Wait();

Console.WriteLine();
Console.WriteLine("Done");

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
            lock(this) // don't do this! public object being lock
            return _completed;
        }
    }

    public void SetResult() => Complete(null);
    public void SetException(Exception exception) { }

    private void Complete(Exception? exception)
    {
        lock (this)
        {
            if (_completed) throw new InvalidOperationException("already completed");

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
                        ExecutionContext.Run(_context, (object? state) => ((Action) state!).Invoke(), _continuation);
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
            ExceptionDispatchInfo.Throw(_exception);
            // throw new AggregateException(_exception);
        }
    }

    public MyTask ContinueWith(Func<MyTask> action)
    {
        MyTask t = new ();

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
            catch (Exception e)
            {
                t.SetException(e);
                return;
            }
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

    public MyTask ContinueWith(Action action)
    {
        MyTask t = new ();

        Action callback = () =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                t.SetException(e);
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

    public static MyTask Run(Action action)
    {
        MyTask t = new();

        MyThreadPool.QueueUserWorkItem(() =>
        {
           try
            {
                action();
            } 
            catch (Exception e)
            {
                t.SetException(e);
                return;
            }
            t.SetResult();
        });
        return t;
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
                if (Interlocked.Decrement(ref remaining) == 0) // atomic lock
                {
                    // TODO: exceptions;
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
        new Timer(_ => t.SetResult()).Change(timeout, -1); // not sleep because thread is blocked

        return t;
    }
}

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
                        ExecutionContext.Run(context, (object? state) => ((Action) state!).Invoke(), workItem);
                    }
                }
            })
            { IsBackground = true }.Start();
        }
    }
}