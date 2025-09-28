using System.Net;
using System.Text;
using System.Web;
using Newtonsoft.Json;

namespace PamukkyV3;

/// <summary>
/// Class that creates a http listener and listens to requests on it.
/// </summary>
public class HTTPHandler
{
    /// <summary>
    /// Converts HttpListenerContext to RequestHandler.Request.
    /// </summary>
    /// <param name="context">HttpListenerContext to convert</param>
    /// <returns>RequestHandler.Request or null if Url is null.</returns>
    public static async Task<RequestHandler.Request?> HTTPtoRequest(HttpListenerContext context)
    {
        if (context.Request.Url == null) return null;
        return new(context.Request.Url.ToString().Split("/").Last(), await new StreamReader(context.Request.InputStream).ReadToEndAsync(), context.Request.HttpMethod);
    }

    static HttpListener _httpListener = new HttpListener();

    /// <summary>
    /// Handles new HTTP calls.
    /// </summary>
    /// <param name="result"></param>
    static async void RespondToHTTPCall(IAsyncResult result)
    {
        HttpListener? listener = (HttpListener?)result.AsyncState;
        if (listener == null) return;
        HttpListenerContext? context = listener.EndGetContext(result);
        if (context.Request.Url == null) return;
        _httpListener.BeginGetContext(new AsyncCallback(RespondToHTTPCall), _httpListener);

        context.Response.KeepAlive = false;
        //Added these so web client can access it
        context.Response.AddHeader("Access-Control-Allow-Headers", "*");
        context.Response.AddHeader("Access-Control-Allow-Methods", "*");
        context.Response.AddHeader("Access-Control-Allow-Origin", "*");

        string url = context.Request.Url.ToString().Split("/").Last();
        if ((url == "upload" && context.Request.HttpMethod.ToLower() == "post") || url.StartsWith("getmedia"))
        {
            int statuscode = 200;
            bool errorResponse = true;
            string res = "";
            if (url == "upload") // Http upload API
            {
                if (context.Request.Headers["token"] != null)
                {
                    string? uid = Pamukky.GetUIDFromToken(context.Request.Headers["token"]);
                    if (uid != null)
                    {
                        string? contentLengthString = context.Request.Headers["content-length"];
                        if (contentLengthString != null)
                        {
                            int contentLength = int.Parse(contentLengthString);
                            if (contentLength != 0)
                            {
                                string type = context.Request.Headers["type"] == "thumb" ? "thumb" : "file";
                                string id = "";
                                if (type == "file")
                                {
                                    do
                                    {
                                        id = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=", "").Replace("+", "").Replace("/", "");
                                    }
                                    while (File.Exists("data/upload/" + id));
                                }
                                else if (type == "thumb")
                                {
                                    string? expectedID = context.Request.Headers["id"];
                                    if (expectedID != null)
                                    {
                                        var upload = FileUpload.Get(expectedID); // Get the upload
                                        if (upload?.sender == uid) // Check if user owns that file
                                        {
                                            id = expectedID;
                                        }
                                    }
                                }


                                if (id != "")
                                {
                                    errorResponse = false;

                                    var stream = context.Request.InputStream;

                                    var fileStream = File.Create("data/upload/" + id + "." + type);
                                    await stream.CopyToAsync(fileStream);
                                    fileStream.Close();
                                    fileStream.Dispose();

                                    if (type == "file")
                                    {
                                        FileUpload uploadData = new()
                                        {
                                            size = contentLength,
                                            actualName = HttpUtility.UrlEncode(context.Request.Headers["filename"] ?? id),
                                            sender = uid,
                                            contentType = context.Request.Headers["content-type"] ?? ""
                                        };

                                        string? uf = JsonConvert.SerializeObject(uploadData);
                                        if (uf == null) throw new Exception("???");
                                        File.WriteAllText("data/upload/" + id, uf);
                                    }

                                    res = JsonConvert.SerializeObject(new FileUploadResponse(id));
                                    context.Response.StatusCode = statuscode;
                                    context.Response.ContentType = "text/json";
                                    byte[] bts = Encoding.UTF8.GetBytes(res);
                                    context.Response.OutputStream.Write(bts, 0, bts.Length);
                                    context.Response.KeepAlive = false;
                                    context.Response.Close();
                                }
                                else
                                {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new RequestHandler.ServerResponse("error", "NOFILE", "No file."));
                                }
                            }
                            else
                            {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new RequestHandler.ServerResponse("error", "NOFILE", "No file."));
                            }
                        }
                        else
                        {
                            statuscode = 404;
                            res = JsonConvert.SerializeObject(new RequestHandler.ServerResponse("error", "NOFILE", "No file."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new RequestHandler.ServerResponse("error", "NOUSER", "User doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 411;
                    res = JsonConvert.SerializeObject(new RequestHandler.ServerResponse("error"));
                }
            }
            else if (url.StartsWith("getmedia")) // HTTP getmedia API
            {
                if (context.Request.QueryString["file"] != null)
                {
                    string file = context.Request.QueryString["file"] ?? "";
                    string type = context.Request.QueryString["type"] ?? "";
                    if (File.Exists("data/upload/" + file))
                    {
                        FileUpload? f = JsonConvert.DeserializeObject<FileUpload>(await File.ReadAllTextAsync("data/upload/" + file));
                        if (f != null)
                        {
                            //context.Response.AddHeader("Content-Length", f.size.ToString());
                            if (context.Request.Headers["sec-fetch-dest"] != "document")
                            {
                                context.Response.AddHeader("Content-Disposition", "attachment; filename=" + HttpUtility.UrlEncode(f.actualName));
                            }
                            context.Response.StatusCode = statuscode;

                            string path = "data/upload/" + file + "." + (type == "thumb" ? "thumb" : "file");
                            if (File.Exists(path))
                            {
                                errorResponse = false;
                                var fileStream = File.OpenRead(path);
                                context.Response.KeepAlive = false;
                                try
                                {
                                    await fileStream.CopyToAsync(context.Response.OutputStream);
                                }
                                catch
                                {

                                }
                                context.Response.Close();
                                fileStream.Close();
                                fileStream.Dispose();
                            }
                            else
                            {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new RequestHandler.ServerResponse("error", "File doesn't exist, could be the thumbnail."));
                            }
                        }
                        else
                        {
                            statuscode = 500;
                            res = JsonConvert.SerializeObject(new RequestHandler.ServerResponse("error"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new RequestHandler.ServerResponse("error", "NOFILE", "File doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 411;
                    res = JsonConvert.SerializeObject(new RequestHandler.ServerResponse("error"));
                }
            }
            if (errorResponse) // Send error stuff
                try
                {
                    context.Response.StatusCode = statuscode;
                    context.Response.ContentType = "text/json";
                    byte[] bts = Encoding.UTF8.GetBytes(res);
                    context.Response.OutputStream.Write(bts, 0, bts.Length);
                    context.Response.Close();
                }
                catch { }
        }
        else
        {
            var request = await HTTPtoRequest(context);
            if (request != null)
            {
                var response = await RequestHandler.respondToRequest(request);
                try
                {
                    context.Response.StatusCode = response.statusCode;
                    context.Response.ContentType = "text/json";
                    byte[] bts = Encoding.UTF8.GetBytes(response.res);
                    context.Response.OutputStream.Write(bts, 0, bts.Length);
                    context.Response.Close();
                }
                catch { }
            }
        }
    }


    /// <summary>
    /// Starts the HTTP listener.
    /// </summary>
    /// <param name="httpport">HTTP port</param>
    /// <param name="httpsport">HTTPS port.</param>
    public void Start(int httpport, int? httpsport = null)
    {
        Console.WriteLine("Http listener starting...");

        _httpListener.Prefixes.Add("http://*:" + httpport + "/"); //http prefix
        Console.WriteLine("Listening on port " + httpport);

        if (httpsport != null)
        {
            _httpListener.Prefixes.Add("https://*:" + httpsport + "/"); //https prefix
            Console.WriteLine("Listening on port " + httpsport);
        }

        _httpListener.Start();
        // Start responding for server
        _httpListener.BeginGetContext(new AsyncCallback(RespondToHTTPCall), _httpListener);
    }
}