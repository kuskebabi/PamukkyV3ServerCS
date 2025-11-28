# Public tags
Users/Groups can have a short name or "tag" that users can use to access them. Tags can only be owned by one target and must only contain letters, numbers and underscore.

## publictag
This is a API that you can both get a tag and set the tag.

### Set
```json
{
    "token": "(Token of the session)",
    "tag": "(Tag to set as)",
    "target": "(ID of group or user[Can only be current user])"
}
```

### Get
```json
{
    "tag": "(Tag to get target of)"
}
```

Reply of this call is a plain text of target of the tag, empty string is given for none.