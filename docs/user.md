# User APIs
## login
This API allows users to login.
### Usage
(As body)
```json
{
    "email": "(E-Mail of the user)",
    "password": "(Password of the user)"
}
```
### Responses
#### Success
```json
{
    "token": "(Token of the session)",
    "uid": "(ID of the user)"
} 
```
## signup
This API allows users to create accounts.
### Usage
(As body)
```json
{
    "email": "(E-Mail of the user)",
    "password": "(Password of the user)"
}
```
### Responses
#### Success
```json
{
    "token": "(Token of the session)",
    "uid": "(ID of the user)"
} 
```
## changepassword
This API allows users to change their password. This makes other sessions log out.
### Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "oldpassword": "(Current password of the user)",
    "password": "(New password of the user)"
}
```
## editprofile
This API allows users to edit their profile.
### Usage
(As body)
```json
{
    "token": "(Token of the session)",
    // Rest of these are optional.
    "name": "(Name of the user)",
    "picture": "(Picture of the user)",
    "bio": "(Bio of the user)"
} 
```
## setonline
This API sets the user as online.
### Usage
(As body)
```json
{
    "token": "(Token of the session)",
} 
```
## logout
Makes the session token invalid.
### Usage
(As body)
```json
{
    "token": "(Token of the session)",
} 
```


## getuser
This API allows to get profile of a user.
### Usage
(As body)
```json
{
    "uid": "(Id of the user)"
}
```
### Responses
#### Success
```json
{
    "name": "(Name of the user)",
    "picture": "(Picture URL of the user)",
    "bio": "(Bio of the user)",
    "onlineStatus": "(Same as getonline. Online status of the user)"
} 
```

## getonline
This API allows to get online status of a user.
### Usage
(As body)
```json
{
    "uid": "(Id of the user)"
}
```
### Responses
#### Success
"Online" or a date.

## getchatslist
Gets chats list of the current user as a array.
### Usage
(As body)
```json
{
    "token": "(Token of the session)"
}
```
### Responses
#### Success
```json
[
    {
        "type":"(user/group)",
        "chatid":"(Chat ID, null if group, Group ID can be used instead for groups)",
        "user":"(ID of the user, null if group)",
        "group":"(ID of the group, null if user)"
    },
    ...
]
```

## adduserchat
Adds a user to user's chats list (and vice versa).
### Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "email": "(E-Mail of the user)"
}
```

## getnotifications
Gets notifications of the session.
### Usage
(As body)
```json
{
    "token": "(Token of the session)",
    // Hold mode
    "mode": "hold"
}
```
### Responses
#### Success
```json
{
    "(ID of the notification)": { 
        "chatid": "(ID of the chat)",
        "userid": "(ID of the user)",
        "user": {
            "name" : "(Name of the user)",
            "picture": "(Picture URL of the user)"
        },
        "content": "(Content of the message)"
    },
    ...
} 
```

# User hook
Updates from a user hook could be like these:

* Update name being `online`, sending online status as string.
* Update name being `profileUpdate`, sending new user profile like `getuser` API.

# Chats list hook
Updates from a chatslist hook are usually like this:

* Update key containing the chat ID (or the group ID).
* If update value is `"DELETED"`, it would mean that the chat was removed from the list.
* Else, the value would be like a normal chat item and it would mean that it was added.