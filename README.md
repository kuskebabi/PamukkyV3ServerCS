# Pamukky V3 Server C#
https://pamukky.netlify.app/v3

Rewritten version of [PamukkyV3ServerNode](https://github.com/HAKANKOKCU/PamukkyV3ServerNode). Why? Javascript kinda sucks and the code was messy.

In this rewrite, I made almost everything classes. So it should be better.

# Used packages
* Konscious.Security.Cryptography.Argon2
* Newtonsoft.Json

# How to run?
## Normal
- Install dotnet
- cd to the folder which has .csproj
- `dotnet run [--config file.json]` ([] is optional)

## Docker
There's 2 ways to run inside docker:
### Use prebuilt images (recommended)
- Create a compose file with ```nano docker-compose.yml```
```yaml
services:
  pamukky:
    image: ghcr.io/kuskebabi/pamukkyv3server:latest
    container_name: test-pamukky-server
    ports:
      - "4268:4268"
    volumes:
      - ./data:/App/data
#      - ./config.json:/App/config.json:ro
#      - ./tos.txt:/App/tos.txt:ro
    restart: unless-stopped
    stdin_open: true
    tty: true
    environment:
      - ASPNETCORE_URLS=http://+:4268
    networks:
      - default
    deploy:
```
- (Optional) Create a Terms of Service file with ```nano tos.txt```. It will be shown to the users when they connect to your server.
- (Optional) Create a Config file with ```nano config.json```.
- Then, run it with ```docker compose up -d```
Note: If you created your config file or tos file, uncomment these lines in your compose file, under the `volumes` section:
```yaml
      - ./config.json:/App/config.json:ro
      - ./tos.txt:/App/tos.txt:ro
```
### Build locally
- cd to the folder which has .csproj (./PamukkyV3Server)
- Run the container: `docker compose up -d`
- If you want to build the container image manually: `docker build -t pamukky -f Dockerfile .`

## Config file
Everything here is optional.

```json
{
    "httpPort": 4268,
    "httpsPort": 4280,
    "termsOfServiceFile": "/home/user/pamukkytos.txt",
    "publicUrl": "https://.../",
    "publicName": "...",
    "maxFileUploadSize": 24,
    "autoSaveInterval": 300000,
    "allowSignUps": true,
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
* `publicName` Public Name(short url) of the server for federation. null for none.
* `maxFileUploadSize` Max file size for uploads in megabytes.
* `autoSaveInterval` Sets interval of auto-save. Default is 300000. 0 or smaller to disable auto-save.
* `allowSignUps` Sets if new a new account can be created by users.


# Status
I think it's usable, but expect some bugs.
Few functions are still partial and not implemented.

# Notes
* Https is a todo. For now you can forward the requests to https.
* C#'s HTTP listener needs firewall rules.
* Force quitting will not save chats unless autosave saved them.
