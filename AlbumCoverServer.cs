using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

public class AlbumCoverServer
{
    private static readonly string _albumCoverPath = "./album_image/current_album.jpg";

    private static Guid _albumCoverGuid;

    public static void SetAlbumCoverGuid(Guid guid)
    {
        _albumCoverGuid = guid;
    }

    public static async Task Serve(Mutex _albumCoverLock)
    {
        lock (_albumCoverLock)
        {
            if (!File.Exists(_albumCoverPath))
            {
                Console.WriteLine("No album cover found.");
                return;
            }
        }

        HttpListener listener = new();
        listener.Prefixes.Add("http://localhost:8080/");
        listener.Start();
        Console.WriteLine("Listening on http://localhost:8080/");
        while (true)
        {
            HttpListenerContext context = await listener.GetContextAsync();
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;
            if (!request.Url.AbsolutePath.Equals("/album_image/current_album.jpg"))
            {
                System.Console.WriteLine(request.Url.AbsolutePath);
                System.Console.WriteLine("Invalid request path.");
                response.StatusCode = 404;
                response.Close();
                continue;
            }


            lock (_albumCoverLock)
            {
                if(!request.Url.Query.Equals($"?id={_albumCoverGuid.ToString()}"))
                {
                    System.Console.WriteLine("Invalid GUID.");
                    response.StatusCode = 404;
                    response.Close();
                    continue;
                }
                
                System.Console.WriteLine("Serving album cover.");
                response.ContentType = "image/jpeg";

                byte[] buffer = File.ReadAllBytes(_albumCoverPath);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer);
                response.OutputStream.Close();
            }
        }
    }
}