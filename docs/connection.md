# Connection
Pamukky currently has(I mean... depends on the thing. atleast for this server) this kind of connection steps:

```
                                    [

                                        If server url starts with http/https => Direct connect, If doesnt:
Connect <= Is server compatiable <=     See .well-known on the server, trying https and http, .well-known/pamukky/v3 {"pamukkyv3.server": "..."}, else
                                        Try http/https directly on the url.
                                                                                                                                                        ]
```

If possible, .well-known would be the best.

Only "randomly" accessed port would be 80, preferred port for server is still 4268 but I didn't want to make it check "random" ports.

## /pamukky
Returns server info. example:

```json
{
    "isPamukky":true,
    "pamukkyType":3,
    "version":0,
    "publicName":"localhost:4268",
    "maxFileUploadSize":24
}
```