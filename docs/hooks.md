# Hooks
Hooks allow clients to get all new events with a single API call or federations to recieve new events. Hooks are on session level so you can't recieve the same update(unless they listen on same stuff) from different sessions.

Updates will be automatically sent to federations by the server.
## APIs (User level)
### addhook
Adds a hook to listen.
#### Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "ids": ["(hook)", ...]
}
```

`ids` is a string array which contains items that could be `chatslist`(which idenifies current user's chats list), or;
* Contains `:`
* Before the `:`, it can be these:
    * `chat` which idenifies a chat
    * `user` which idenifies a user
    * `group` which idenifies a group
* After the `:`, it would have ID of the idenifier.

Examples: `chatslist`, `chat:cYcfOPwMgE6X2WZo8EWrSw`, `group:cYcfOPwMgE6X2WZo8EWrSw`

### getupdates (Hooks mode)
Gets updates of all hooks. If there is none, server would wait for one for 60 seconds and if there is still none, will return a empty object (`{}`)

#### Usage
(As body)
```json
{
    "token": "(Token of the session)"
}
```

#### Response
##### Success
```json
{
    "(Hook name)": {
        // Hook data
    },
    ...
}
```