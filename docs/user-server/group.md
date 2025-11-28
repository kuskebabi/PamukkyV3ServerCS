# Group APIs
## getgroup
Gets basic info about the group.
### Usage
(As body)
```json
{
    "token": "(Token of the session, optional if group is set as public)",
    "groupid": "(ID of the group)"
}
```
### Responses
#### Success
```json
{
    "name": "(Name of the group)",
    "picture": "(Picture URL of the group)",
    "info": "(Info of the group)",
    "isPublic": boolean //True if group can be seen without joining. 
} 
```

## getgroupmembers
Gets members list of the group.
## Usage
(As body)
```json
{
    "token": "(Token of the session, optional if group is set as public)",
    "groupid": "(ID of the group)"
}
```
### Responses
#### Success
```json
{
    "(ID of the user)": {
        "userID": "(ID of the user)",
        "role": "(Role of the user)",
        "jointime": "(Date)"
    },
    ...
} 
```
## getgroupmemberscount
Gets members count of the group.
## Usage
(As body)
```json
{
    "token": "(Token of the session, optional if group is set as public)",
    "groupid": "(ID of the group)"
}
```
### Responses
#### Success
(integer string)

## getbannedgroupmembers
Gets banned members of the group.
## Usage
(As body)
```json
{
    "token": "(Token of the session, optional if group is set as public)",
    "groupid": "(ID of the group)"
}
```
### Responses
#### Success
```json
["(ID of the user)", ...]
```

## getgrouproles
Gets all roles of the group
## Usage
(As body)
```json
{
    "token": "(Token of the session, optional if group is set as public)",
    "groupid": "(ID of the group)"
}
```
### Responses
#### Success
```json
{
    "(Name of the role, giving a owner role as exapmle)": {
        "AdminOrder": 0,
        "AllowMessageDeleting": true,
        "AllowEditingSettings": true,
        "AllowKicking": true,
        "AllowBanning": true,
        "AllowSending": true,
        "AllowEditingUsers": true,
        "AllowSendingReactions": true,
        "AllowPinningMessages": true
    },
    ...
}
```

## getgrouprole
Gets current user's role in the group.
## Usage
(As body)
```json
{
    "token": "(Token of the session, optional if group is set as public)",
    "groupid": "(ID of the group)"
}
```
### Responses
#### Success
```json
{
    "AdminOrder": 0,
    "AllowMessageDeleting": true,
    "AllowEditingSettings": true,
    "AllowKicking": true,
    "AllowBanning": true,
    "AllowSending": true,
    "AllowEditingUsers": true,
    "AllowSendingReactions": true,
    "AllowPinningMessages": true
}
```

## creategroup
Makes user create a group (and join it)
## Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "name": "(Name of the group)",
    // Optional:
    "picture": "(Picture URL of the group)",
    "info": "(Info of the group)",
}
```
### Responses
#### Success
```json
{
    "groupid": "(ID of the group)"
}
```

## editgroup
Makes user edit a group (if they have permission to)
## Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "groupid": "(ID of the group)",
    // Optional:
    "name": "(Name of the group)",
    "picture": "(Picture URL of the group)",
    "info": "(Info of the group)",
    "ispublic": boolean, // If the group is public or not
    "roles": { } //Group roles, see getgrouproles.
}
```

## editmember
Makes user change role of user (if they have permission to)
## Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "groupid": "(ID of the group)",
    "userid": "(ID of the user to edit)",
    "role": "(New rule of the user)"
}
```

## kickmember
Makes user kick user (if they have permission to)
## Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "groupid": "(ID of the group)",
    "userid": "(ID of the user to kick)"
}
```

## banmember
Makes user ban user (if they have permission to)
## Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "groupid": "(ID of the group)",
    "userid": "(ID of the user to ban)"
}
```

## unbanmember
Makes user ubban user (if they have permission to)
## Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "groupid": "(ID of the group)",
    "userid": "(ID of the user to unban)"
}
```

## joingroup
Makes user join the group (if they aren't banned)
## Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "groupid": "(ID of the group)"
}
```

## leavegroup
Makes user leave the group
## Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "groupid": "(ID of the group)"
}
```

# Group hook
Updates from a chat hook could be like these:

* Update name being like `USER|(User ID)`, if value is a empty string the user has left, if value is `BANNED` the user has been banned, if value is a role, user has joined or role has been changed.
* Update name being `edit`, sending what `/getgroup` does or also with `roles` object included. This update also contains who edited the group, in the `userID` key.
* Update name being `publicTagChange`, sending group's new public tag as string.