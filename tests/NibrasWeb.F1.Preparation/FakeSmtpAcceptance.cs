using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NIBRAS.API.Services;
namespace NibrasWeb.F1.Preparation;

// Explicit network mode only; never used by --static. Bound to loopback, with dummy mail.
public static class FakeSmtpAcceptance
{
    private const string ReplyMarker="smtp-response-sanitization-marker";
    private sealed class Capture : ILogger<SmtpEmailSender>
    {
        public List<(string Text,bool HasException)> Messages {get;}=[];
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
        public bool IsEnabled(LogLevel level)=>true;
        public void Log<TState>(LogLevel level,EventId id,TState state,Exception? exception,Func<TState,Exception?,string> format) =>
            Messages.Add((format(state,exception),exception is not null));
        public void CheckSanitized() => Program.Check(Messages.All(m=>!m.HasException &&
            !m.Text.Contains(ReplyMarker,StringComparison.Ordinal) &&
            !m.Text.Contains("recipient@example.test",StringComparison.Ordinal) &&
            !m.Text.Contains("Dummy acceptance only",StringComparison.Ordinal)),
            "SMTP logs omit server reply, raw recipient, mail body and exception payload");
    }
    private static SmtpEmailSender Sender(TcpListener listener,Capture log,bool tls=false)=>new(
        Options.Create(new EmailOptions {SmtpHost="127.0.0.1",
            SmtpPort=((IPEndPoint)listener.LocalEndpoint).Port,SmtpEnableSsl=tls,FromAddress="sender@example.test"}),log);
    private static EmailMessage Dummy()=>new("recipient@example.test","Fake acceptance / اختبار","Dummy acceptance only");
    public static async Task RunAsync()
    {
        foreach(var reject in new[]{false,true}) {
            using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var listener=new TcpListener(IPAddress.Loopback,0);
            Task? server=null;Task<EmailSendResult>? send=null;
            var log=new Capture();listener.Start();
            try {
                server=ServeAsync(listener,reject,limit.Token);
                send=Sender(listener,log).SendAsync(Dummy(),limit.Token);
                var result=await send;
                Program.Check(result.Sent==!reject,"fake SMTP success/rejection classified");
                await server;log.CheckSanitized();
            } finally {
                await SmtpTaskContainment.JoinAsync(send,server,limit.Cancel,listener.Stop);
            }
        }
        await MissingTlsAsync();
        await DeadlineAsync(false);
        await DeadlineAsync(true);
    }
    private static async Task DeadlineAsync(bool cancelCaller)
    {
        using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(40));
        using var caller=CancellationTokenSource.CreateLinkedTokenSource(limit.Token);
        var listener=new TcpListener(IPAddress.Loopback,0);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var log=new Capture();Task? server=null;Task<EmailSendResult>? send=null;listener.Start();
        try {
            server=ServeAsync(listener,false,limit.Token,true,entered);
            send=Sender(listener,log).SendAsync(Dummy(),caller.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if(cancelCaller) {
                caller.Cancel();var cancelled=false;
                try{await send;}catch(OperationCanceledException){cancelled=true;}
                Program.Check(cancelled,"SMTP caller cancellation propagated");
            } else {
                var result=await send.WaitAsync(TimeSpan.FromSeconds(35));
                Program.Check(!result.Sent&&result.FailureReason=="smtp_timeout","async SMTP deadline bounded and classified");
            }
            log.CheckSanitized();
        } finally {
            await SmtpTaskContainment.JoinAsync(send,server,()=>{caller.Cancel();limit.Cancel();},listener.Stop);
        }
    }
    private static async Task MissingTlsAsync()
    {
        using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener=new TcpListener(IPAddress.Loopback,0);
        var log=new Capture();Task? server=null;Task<EmailSendResult>? send=null;listener.Start();
        try {
            server=ServeAsync(listener,false,limit.Token);
            send=Sender(listener,log,true).SendAsync(Dummy(),limit.Token);
            var result=await send;
            Program.Check(!result.Sent,"SMTP fails closed when server lacks STARTTLS");
            await server;log.CheckSanitized();
        } finally {
            await SmtpTaskContainment.JoinAsync(send,server,limit.Cancel,listener.Stop);
        }
    }

    private static async Task ServeAsync(TcpListener listener,bool reject,CancellationToken token,bool stall=false,TaskCompletionSource? entered=null)
    {
        using var client=await listener.AcceptTcpClientAsync(token);
        entered?.TrySetResult();
        if(stall){await Task.Delay(Timeout.InfiniteTimeSpan,token);return;}
        using var stream=client.GetStream();
        using var reader=new StreamReader(stream,Encoding.UTF8,leaveOpen:true);
        using var writer=new StreamWriter(stream,Encoding.ASCII,leaveOpen:true){NewLine="\r\n",AutoFlush=true};
        await writer.WriteLineAsync("220 localhost fake SMTP");
        while(true){
            var line=await reader.ReadLineAsync(token);
            if(line is null)return;
            if(line.StartsWith("EHLO",StringComparison.OrdinalIgnoreCase)||line.StartsWith("HELO",StringComparison.OrdinalIgnoreCase))
                await writer.WriteLineAsync("250 localhost");
            else if(line.StartsWith("MAIL FROM",StringComparison.OrdinalIgnoreCase))
                await writer.WriteLineAsync("250 OK");
            else if(line.StartsWith("RCPT TO",StringComparison.OrdinalIgnoreCase))
                await writer.WriteLineAsync(reject?"550 "+ReplyMarker:"250 OK");
            else if(line.Equals("DATA",StringComparison.OrdinalIgnoreCase)){
                await writer.WriteLineAsync("354 continue");
                // Discard dummy content in memory; never print it or persist it.
                while(await reader.ReadLineAsync(token) is { } data&&data!="."){}
                await writer.WriteLineAsync("250 accepted");
            }else if(line.Equals("QUIT",StringComparison.OrdinalIgnoreCase)){
                await writer.WriteLineAsync("221 bye");return;
            }else await writer.WriteLineAsync("250 OK");
        }
    }
}
