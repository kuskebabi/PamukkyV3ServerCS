# Chat APIs
## getmessages
Gets messages in the range. For example, `#0-#48` is from first to 48. relative to end of chat, using `#^` instead would do it relative to oldest and giving an message id will point to it.
### Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "chatid": "(ID of the chat)",
    "range": "(A range, look explaination at the top)"
}
```
### Responses
#### Success
```json
{
    "(ID of the message)": { Message Object },
    ...
} 
```

## sendmessage
Sends a message.
### Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "chatid": "(ID of the chat)",
    "content": "(Content of the message)",
    // Optional
    "replymessageid": "(ID of the message to reply)",
    "files": ["(File url)", ...]
}
```

## deletemessage
Deletes messages in the `messageids` array.
### Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "chatid": "(ID of the chat)",
    "messageids": ["(Message ID)", ...]
}
```

## pinmessage
Pins/unpins messages in the `messageids` array.
### Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "chatid": "(ID of the chat)",
    "messageids": ["(Message ID)", ...]
}
```

## savemessage
Sends messages in the `messageids` array to "Saved messages" chat. Saved messages would be user chatting with theirselves.
### Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "chatid": "(ID of the chat)",
    "messageids": ["(Message ID)", ...]
}
```

## forwardmessage
Forwards messages in the `messageids` array to chats (with their IDs) to chats in the `chatidstosend` array.
### Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "chatid": "(ID of the chat)",
    "messageids": ["(Message ID)", ...],
    "chatidstosend": ["(Chat ID)", ...]
}
```

## sendreaction
Reacts/unreacts to the message (in the `messageid` string) with the emoji at `reaction`.
### Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "chatid": "(ID of the chat)",
    "messageid": "(Message ID)",
    "reaction": "(Emoji to react)"
}
```

## settyping
Sets user as typing in the chat for 3 seconds.
### Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "chatid": "(ID of the chat)"
}
```

## getupdates (Chat only mode)
Gets(With `since` string) or waits for new updates. See "#Chat updater/hook" for format.
### Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "id": "(ID of the chat)",
    // "normal" since mode, no wait.
    "mode": "normal",
    "since": "(Update[or message] ID to get updates later that.)",
    // "updater" waiting mode, no "since". this only waits for new updates (until timeout).
    "mode": "updater"
}
```

## gettyping
Gets or waits for new typing updates. Different than format in "#Chat updater/hook", this will return all typing users.
### Usage
(As body)
```json
{
    "token": "(Token of the session)",
    "chatid": "(ID of the chat)",
    // "normal" mode, gets all typing users
    "mode": "normal",
    // "updater" waiting mode,. this waits for new typing updates (until timeout).
    "mode": "updater"
}
```

# Message Object
(The ones that end with `?` mean they might not exist. **They are not in the actual property name**)
```json
{
    "forwardedFromUID?":"(User ID of original sender of the message)",
    "replyMessageContent?":"(Content of the replied message)",
    "replyMessageSenderUID?":"(Sender user ID of the replied message)",
    "replyMessageID?":"(ID of the message)",
    "senderUID":"(User ID of the sender)",
    "content":"(Content of the message)",
    "sendTime":"(Send time of the message)",
    // Please look to media.md(##Message attachments format) for format of these 5 arrays.
    "files?": [...],
    "gImages?": [...],
    "gVideos?": [...],
    "gAudio?": [...],
    "gFiles?": [...],
    "reactions?":{
        "(Emoji)":{
            "(ID of the user)":{
                "reaction":"(Emoji)",
                "senderUID":"(ID of the user)",
                "sendTime":"(Send time of the reaction)"
                }
            }
    },
    "isPinned": boolean // If the message is pinned or not
}
```

# Chat updater/hook
Updates from a chat hook could be like these:

* (HOOK ONLY) Update name being like `TYPING|(User ID)`, if value is `true`, the user (with the id) is typing, else they aren't.
* If it's a ID instead, they are normal chat updates. These updates contain a `id` key which points to the message (with ID) and `event` key which is the event type;

## NEWMESSAGE event
Fires when a new message is sent. It's just a `{Message Object}` with normal event stuff like `id` (which is ID of the new message) and `event`.

## (UN)PINNED event
Fired when a message is (un)pinned. Message data is available in the event.

## (UN)REACTED event
Fired when a message is (un)reacted.

* `reaction` is reaction emoji.
* `senderUID` is ID of the user who sent it.
* (REACTED ONLY) `sendTime` is the time when reaction was sent.

## DELETED event
Fires when a message is deleted. It's just a normal event. Message at `id` string would be deleted.