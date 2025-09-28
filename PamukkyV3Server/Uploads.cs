using Newtonsoft.Json;

namespace PamukkyV3;

/// <summary>
/// Uploaded file information.
/// </summary>
public class FileUpload
{
    public string sender = "";
    public string actualName = "";
    public int size = 0;
    public string contentType = "";

    public static FileUpload? Get(string id)
    {
        id = id.Split("/").Last();
        string file = "data/upload/" + id; //Get path
        if (File.Exists(file))
        {
            return JsonConvert.DeserializeObject<FileUpload>(File.ReadAllText(file));
        }
        return null;
    }
}

/// <summary>
/// What should server reply for /upload
/// </summary>
class FileUploadResponse
{
    public string url = "";
    public string id = "";
    public string status = "done";
    public FileUploadResponse(string fid)
    { //for easier creation
        id = fid;
        url = "%SERVER%getmedia?file=" + fid;
    }
}