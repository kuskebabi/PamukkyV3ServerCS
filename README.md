# Pamukky V3 Server C#
https://pamukky.netlify.app/v3

Rewritten version of [PamukkyV3ServerNode](https://github.com/HAKANKOKCU/PamukkyV3ServerNode). Why? Javascript kinda sucks and the code was messy.

In this rewrite, I made almost everything classes. So it should be better.

# Used packages
* Konscious.Security.Cryptography.Argon2
* Magick.NET-Q8-AnyCPU
* Newtonsoft.Json

# How to run?
## Normal
- Install dotnet
- cd to the folder which has .csproj
- `dotnet run [--config file.json]` ([] is optional)

## Docker
- cd to the folder which has .csproj
- Build the docker: `docker build -t pamukky -f Dockerfile .`
- Run the docker: `docker run -p 4268:4268 pamukky`

## Config file
All stuff here is optional.

```json
{
    "httpPort": 4268,
    "httpsPort": 4280,
    "termsOfServiceFile": "/home/user/pamukkytos.txt",
    "publicUrl": "https://...:4280/",
    "systemProfile": {
        "name": "Pamuk but weird birb",
        "picture": "",
        "bio": "hhahi! This is a account. as expected!!!"
    }
}
```

* `httpPort` Port for http
* `httpPort` Port for https. null for none.
* `termsOfServiceFile` File path that has server terms of service. null for none.
* `publicUrl` Public URL of the server for federation. null for none.


# Status
I think it's usable, but expect some bugs.
Few functions are still partial and not implemented.

# Notes
* Https is a todo. For now you can forward the requests to https.
* C#'s HTTP listener needs firewall rules.
* Force quitting will not save chats unless autosave saved them.
