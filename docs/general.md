# General stuff
* HTTP response parameters are usually made in the body except APIs like upload.
* If a url has %SERVER%, it means it's owned by the current server. Which also means you can request thumbnails from it. See media.md for info.

# General APIs
## getinfo
Automatically works as getuser or getgroup depending on what the request was. This will return what getuser/getgroup would return as a success response.
### Usage
(As body)
```json
{
    "id": "(Id of the user or group)"
}
```

## multi
Allows to make multiple API calls at once.
### Usage
(As body)
```json
{
    "(API name)|(Some idenifier, if you want to do multiple APIs. else '|' and other parts are not needed)": "(Request body of the request)",
    ...
}
```
### Responses
#### Success
```json
{
    "(Name of the request)": "(Response of the request)",
    ...
}
```

# General Responses
## Basic done response
Speaks for itself, has no useful data. If there is no success response area, it's probably this.