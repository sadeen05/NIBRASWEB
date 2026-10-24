namespace NibrasWeb.F1.Preparation;

// Test-host containment only. All started tasks are joined before callers dispose resources.
public static class SmtpTaskContainment
{
    public static void StopHost(string reason)
    {
        Console.Error.WriteLine("FAIL fake SMTP cleanup incomplete; stop further acceptance.");
        Environment.Exit(74);
        throw new InvalidOperationException("Unreachable isolated-host stop.");
    }
    public static async Task JoinAsync(Task? send,Task? server,Action cancel,Action stop,
        TimeSpan? budget=null,Action<string>? terminal=null)
    {
        var failures=new List<Exception>();
        try{cancel();}catch(Exception ex){failures.Add(ex);}
        try{stop();}catch(Exception ex){failures.Add(ex);}
        var limit=budget ?? TimeSpan.FromSeconds(10);
        foreach(var task in new[]{send,server}.OfType<Task>()) {
            try {
                await FkTaskFence.AwaitAsync(task,limit,()=>{cancel();stop();},limit,terminal:terminal ?? StopHost);
            } catch(OperationCanceledException) when(task.IsCanceled) {
                // Cancellation requested by the owning finally path, with the task observed.
            } catch(Exception ex){failures.Add(ex);}
        }
        if(failures.Count>0)throw new AggregateException("Fake SMTP task cleanup failed.",failures);
    }
    public static async Task RunMocksAsync()
    {
        var stopped=false;
        await JoinAsync(Task.CompletedTask,Task.CompletedTask,()=>{},()=>stopped=true);
        Program.Check(stopped,"fake SMTP containment joins successful sender/server before disposal");
        foreach(var sendFault in new[]{false,true}) {
            var task=Task.FromException(new InvalidOperationException("synthetic"));
            var failed=false;
            try{await JoinAsync(sendFault?task:Task.CompletedTask,sendFault?Task.CompletedTask:task,()=>{},()=>{});}
            catch(AggregateException){failed=true;}
            Program.Check(failed && task.IsCompleted,"fake SMTP sender/server fault is observed and cannot report cleanup PASS");
        }
        var send=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await JoinAsync(send.Task,server.Task,()=>{send.TrySetCanceled();server.TrySetCanceled();},()=>{});
        Program.Check(send.Task.IsCompleted && server.Task.IsCompleted,"fake SMTP early failure cancellation joins both started tasks");
        var pending=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminalCalled=false;var failedDeadline=false;
        try {await JoinAsync(pending.Task,Task.CompletedTask,()=>{},()=>{},TimeSpan.FromMilliseconds(5),
            _=>{terminalCalled=true;pending.TrySetException(new TimeoutException("synthetic"));});}
        catch(AggregateException){failedDeadline=true;}
        Program.Check(terminalCalled && failedDeadline && pending.Task.IsCompleted,"fake SMTP incomplete cleanup invokes containment terminal and never reports PASS");
        var stopFailed=false;
        try{await JoinAsync(Task.CompletedTask,Task.CompletedTask,()=>{},()=>throw new InvalidOperationException("synthetic"));}
        catch(AggregateException){stopFailed=true;}
        Program.Check(stopFailed,"fake SMTP listener-stop failure cannot report cleanup PASS");
    }
}
