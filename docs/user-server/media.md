# Media management
## upload API
Allows users to upload files. There is currently no size limit. File is directly sent as body (no form and etc.)
### Usage
(Headers)
```
token = (token of the user)
content-length = (file size)
content-type = (file type)

// Only for full upload

filename = (Actual name of the file)
type = file

// Only for thumbnails

id = (ID of the file)
type = thumb
```

## getmedia API
Gets file from uploads. As the type, thumb would show thumbnails of images, file will give full file.
### Usage
(URL)
```
/getmedia?file=(ID of the file)&type=(thumb or file)
```
## Message attachments format
This section talks about "g(Images/Videos/Audio/Files)".

If the file(in the files array) exists in the server, server would automatically categorize their types. "g" stands for "groupped".

Here is a example of what objects inside those arrays will look like, The ones that end with `?` mean they might not exist. **They are not in the actual property name**:
```json
{
    "url": "(URL of the file, replace %SERVER% to current server url with /)",
    "name": "(Actual name of the file which is 'filename' in upload call)",
    "size?": (Size of the file as integer),
    "contentType?": "(Type of the file)",
    "hasThumbnail": (True if upload has thumbnail)
}
```