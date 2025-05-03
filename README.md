# netsocketprojuni

imma delete this after the semester

The project is split across solutions for each phase:
Phase 1 - HelloSocket
Phase 2 - Multiclient
Phase 3,4 - Broadcast

To run each project simply use 'dotnet run' on the Server and Client in order.
The implementation of phase 1 and phase 2 are quite trivial so I only discuss
Broadcast server and client implementation:

The underlying sockets are built on top of TCP to represent different 
commands (Msg, PrivateMsg, ...). The server acknowledges the new connection by requesting 
a username from the client and then adds it to the list of clients.
New connections are handled asynchronously using the C#'s async/await pattern and
the 'Task' type utilities. The server continiously checks for disconnected clients and
removes their entry (which is done in parallel with TCP connection handling logic to 
avoid unnecessary waits).

The client side uses a TUI (Terminal User Interface) for interaction, which requires 
careful handling of the 'Console' since the client handles socket connection 
and terminal REPL in separate threads, and terminal console is inherently thread-unsafe. I have
used a 'BlockingCollection<T>' for the Console message queue that is accessed with primitive
locks that suffices to avoid mutation of the 'Console' by multiple threads.
Use /exit to close the client connection, /list to list the connected clients, /pm <msg> <user> to send a private message to another user. Use arrow keys (Up-Down) to scroll messages in the TUI.
