using ImageMagick;

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
}

/// <summary>
/// What should server reply for /upload
/// </summary>
class FileUploadResponse
{
    public string url = ""; //success
    public string status;
    public FileUploadResponse(string stat, string furl)
    { //for easier creation
        status = stat;
        url = furl;
    }
}

public class MediaProcesser
{
    /// <summary>
    /// Constant for width and height of the new thumbnail.
    /// </summary>
    public const int ThumbSize = 256;

    /// <summary>
    /// Constant for quality of the output.
    /// </summary>
    public const int ThumbQuality = 75;

    static List<string> mediaProcesserJobs = new();

    static void MediaThread()
    { //thread function for processing media
        Console.WriteLine("mediaProcesser thread started!");
        while (!Pamukky.exit)
        {
            if (mediaProcesserJobs.Count > 0)
                try
                {
                    string job = mediaProcesserJobs[0];
                    mediaProcesserJobs.RemoveAt(0);
                    Console.WriteLine(job);
                    using (var image = new MagickImage("data/upload/" + job + ".file"))
                    {
                        if (image.Width > ThumbSize || image.Height > ThumbSize)
                        {
                            image.Resize(ThumbSize, ThumbSize);
                            image.Strip();
                        }
                        image.Quality = ThumbQuality;
                        image.Write("data/upload/" + job + ".thumb");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
            Thread.Sleep(100); //Sleep to save cpu
        }
    }


    /// <summary>
    /// Adds a media processing job to be processed (to generate thumbnails).
    /// </summary>
    /// <param name="file">File ID of the file.</param>
    public static void AddJob(string file)
    {
        if (!mediaProcesserJobs.Contains(file)) mediaProcesserJobs.Add(file);
    }


    /// <summary>
    /// Starts a new media processer thread.
    /// </summary>
    public static void StartThread()
    {
        new Thread(MediaThread).Start();
    }
}