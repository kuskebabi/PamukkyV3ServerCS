# Pamukky V3 Server C#
https://pamukky.netlify.app/v3

Rewritten version of [PamukkyV3ServerNode](https://github.com/HAKANKOKCU/PamukkyV3ServerNode). Why? Javascript kinda sucks and the code was messy.

In this rewrite, I made almost everything classes. So it should be better.
# How to run?
- Install dotnet
- cd to the folder which has .csproj
- `dotnet run [-- "port" "port for https"]` ( [] is optional, port is a number)

# Status
I think it's usable, but expect some bugs.
Few functions are still partial and not implemented.

# Notes
* Https is a todo.
* C#'s HTTP listener needs firewall rules.
* Force quitting will not save chats unless autosave saved them.
