using Newtonsoft.Json;

namespace PamukkyV3;

/// <summary>
/// Uploaded file information.
/// </summary>
public class FileUpload
{
    public static Dictionary<string, FileUpload> uploadCache = new();
    [JsonIgnore]
    public string id = "";
    public string sender = "";
    public string actualName = "";
    public int size = 0;
    public string contentType = "";
    public bool hasThumbnail = false;

    public static FileUpload? Get(string id)
    {
        id = id.Split("/").Last();
        if (uploadCache.ContainsKey(id)) return uploadCache[id];
        string file = "data/upload/" + id; //Get path
        if (File.Exists(file))
        {
            var upload = JsonConvert.DeserializeObject<FileUpload>(File.ReadAllText(file));
            if (upload == null) return null;
            upload.id = id;
            uploadCache[id] = upload;
            return upload;
        }
        return null;
    }

    public void Save()
    {
        string file = "data/upload/" + id; //Get path
        File.WriteAllTextAsync(file, JsonConvert.SerializeObject(this));
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