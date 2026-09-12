# DesMeter

A combat meter that runs inside the game. It reads the parse from IINACT over plugin IPC and draws
it in an ImGui window, so there is no ACT process, no browser and no overlay host to keep alive.

**Requires [IINACT](https://github.com/marzent/IINACT).** DesMeter does no parsing of its own; it is
the display. If IINACT is not running, the meter says so.

`/desmeter` shows and hides it. The cog opens settings.

Early days, and the detailed breakdown is still being built.
