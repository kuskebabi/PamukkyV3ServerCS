using System.Collections.Concurrent;

namespace PamukkyV3;

class UpdateHook : ConcurrentDictionary<string, object?>
{
    /// <summary>
    /// User ID of the owner of this hook.
    /// </summary>
    public string target = "";
}
class UpdateHooks : ConcurrentDictionary<string, UpdateHook>
{
    /// <summary>
    /// "Token" of the user or server that owns these hooks.
    /// </summary>
    public string token = "";

    /// <summary>
    /// Filters all the updates to only have ones with new updates and optionally clears them.
    /// </summary>
    /// <param name="clear">If true (default), it clears update stuff</param>
    /// <returns>New updates</returns>
    public UpdateHooks GetNewUpdates(bool clear = true)
    {
        UpdateHooks rtrn = new();
        foreach (var hook in this)
        {
            if (hook.Value.Count > 0)
            {
                if (clear)
                {
                    UpdateHook uhook = new();
                    foreach (var kv in hook.Value)
                    {
                        uhook[kv.Key] = kv.Value;
                    }
                    rtrn[hook.Key] = uhook;
                    hook.Value.Clear();
                }else
                {
                    rtrn[hook.Key] = hook.Value;
                }
            }
        }
        return rtrn;
    }

    /// <summary>
    /// Waits for new updates to happen or timeout and returns them.
    /// </summary>
    /// <param name="maxWait">How long should it wait before giving up? each count adds 100ms more. Default is a minute.</param>
    /// <param name="clear">If true (default), it clears update stuff</param>
    /// <returns>All update hooks</returns>
    public async Task<UpdateHooks> waitForUpdates(int maxWait = 600, bool clear = true)
    {
        int wait = maxWait;
        var updates = GetNewUpdates(clear);

        while (updates.Count == 0 && wait > 0)
        {
            await Task.Delay(100);
            updates = GetNewUpdates(clear);
            --wait;
        }

        return updates;
    }

    /// <summary>
    /// Adds a update hook for a client
    /// </summary>
    /// <param name="target">Can be Chat, UserProfile, Group and UserChatsList.</param>
    public void AddHook(object target)
    {
        string hookName;

        if (target is Chat)
        {
            hookName = "chat:" + ((Chat)target).chatID;
        }
        else if (target is UserProfile)
        {
            hookName = "user:" + ((UserProfile)target).userID;
        }
        else if (target is Group)
        {
            hookName = "group:" + ((Group)target).groupID;
        }
        else if (target is UserChatsList)
        {
            hookName = "chatslist";
        }
        else
        {
            throw new InvalidCastException("target only can be Chat, UserProfile, Group or UserChatsList.");
        }

        if (ContainsKey(hookName))
        { //Don't do duplicates.
            this[hookName].Clear(); // Clear the hook.
            return;
        }
        string ttarget = token;
        if (ttarget.Contains(":") || ttarget.Contains("."))
        {
            Console.WriteLine("Fed create hooks");
        }
        else
        {
            ttarget = Pamukky.GetUIDFromToken(token) ?? "";
        }
        UpdateHook hook = new() {target = ttarget};
        this[hookName] = hook;

        if (target is Chat)
        {
            Chat chat = (Chat)target;
            if (chat.CanDo(ttarget, Chat.ChatAction.Read))
                chat.updateHooks.Add(hook);
        }
        else if (target is UserProfile)
        {
            UserProfile profile = (UserProfile)target;
            profile.updateHooks.Add(hook);
        }
        else if (target is Group)
        {
            Group group = (Group)target;
            if (group.CanDo(ttarget, Group.GroupAction.Read))
                group.updateHooks.Add(hook);
        }
        else if (target is UserChatsList)
        {
            UserChatsList chatsList = (UserChatsList)target;
            if (chatsList.userID == ttarget) // This check is kinda useless as you can't really select which user to and its always current one. Also ignoring federations for now...
            {
                chatsList.hooks.Add(hook);
            }
        }
    }
}

class Updaters : ConcurrentDictionary<string, UpdateHooks>
{
    static Updaters updaters = new();

    /// <summary>
    /// Gets all the updates even if there is none.
    /// </summary>
    /// <param name="userToken">Token of the client</param>
    /// <returns>All update hooks</returns>
    public static UpdateHooks Get(string userToken)
    {
        if (!updaters.ContainsKey(userToken))
        {
            updaters[userToken] = new() {token = userToken};
        }

        return updaters[userToken];
    }
}