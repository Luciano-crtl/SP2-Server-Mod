SP2 Server Mod {version}  (TEST BUILD)
======================================

Custom matches for SimplePlanes 2 hosts: fixed spawns, one or two teams, a craft for
each team, lives and a kill limit, on-screen feedback, and a spectator camera for the
players who are out.

This is a test build, shared with testers only. Parts of it have never run in a real
multiplayer match. Please read TEST-PLAN.txt and send back the log (see REPORTING).


WHO NEEDS THE MOD
-----------------
* The HOST (the game that runs the server) needs it: it counts lives, ends the match,
  refuses a new craft to a player who is out, checks the crafts and writes the messages.
* Players WITH the mod also get: the match bars on the left of the screen, spawn markers
  before the start, the spectator camera when they are out, and their team's craft
  swapped in automatically.
* Players WITHOUT the mod can play the same match. They see the game's own on-screen
  messages (start, who was shot down, lives left, the winner) and, for a team whose craft
  is on the SimplePlanes website, a clickable link in their chat. They are not given the
  craft: they load it themselves.


REQUIREMENTS
------------
* SimplePlanes 2 (written and tested against game version 0.1.100).
* BepInEx 5.4.23.5 x64 (Mono) installed in the game folder (the official release).


INSTALL
-------
1. Close the game.
2. Unzip this archive INTO THE GAME FOLDER (the one that has the SimplePlanes 2 exe and
   the BepInEx folder). You should end up with
       BepInEx\plugins\SP2ServerMod\SP2ServerMod.dll
3. Start the game. Open BepInEx\LogOutput.log and look for the lines
       SP2 Server Mod {version} loaded.
       [COMPAT] everything the mod uses was found
   If the second line says something is missing, the game has changed: send the log.

Do not put the DLL in BepInEx\scripts as well (it would load twice).
It lives happily next to the Armor Mod and other mods.


MAKING A MATCH
--------------
1. Start a flight. In the flight menu press the blue "SP2 Server Mod" button: a window
   opens at the top right.
2. Press "New match". Then set, in its section: the name, "Two teams (TDM)" (off = every
   player for themselves), "Time limit", "Lives" (0 = no limit), "Kill limit" (0 = none).
3. Spawns: fly to where a team should start and press "Add spawn here" in that team's
   section. (Within 10 m of the ground it is a ground start; higher up it is an air start
   with your current speed.) Keep spawns at least 10 m apart. Team deathmatch needs at
   least one spawn for each team. "Vertical beam" shows a tall beam at a spawn so you
   can find it; "Go to spawn" takes you there.
4. Crafts for a team (optional). A team can have SEVERAL crafts (up to 8) and every
   player picks one of them; without any, players fly their own craft.
   - "Add the craft I'm flying": the craft you are flying is added to the team's crafts.
     It is saved to BepInEx\config\sp2servermod\crafts. A player with the mod needs the
     same file in that folder, so give them the files.
   - "Add craft from the website": paste the link of a craft published on the
     SimplePlanes website. Players with the mod download it by themselves.
   - "Add craft from my list": the file name of a craft in your own craft list.
   - "Remove craft N" / "Remove all crafts".
   A player with the mod picks theirs in the window ("Your team's crafts", at the top)
   or by clicking the "Craft" bar on the left of the flight screen (it picks the next
   one). Before the match starts the craft is swapped at once; once it is running a new
   pick is the craft you get the next time you spawn. The server accepts any craft of
   the team.
5. Everything is saved at once to BepInEx\config\sp2servermod\<match>.match.xml.
6. Start it: Activities menu > Combat > your match. Players join the activity and pick a
   team. The match must be created from the game of a player who has the mod.

If the match is not in the activity list, the panel says why in its section ("NOT LISTED":
for example a team without a spawn).


SETTINGS
--------
The panel has "Match display" (what you see) and "Server rules (when you host)":
  Referee                  ends the match at the kill limit or when one player/team is left
  No spawn without lives   a player who is out cannot come back; no joining a running match with lives
  Announce on screen       the game's own messages, seen by everybody
  Craft link in the chat   the website links of the team's crafts, for players without the mod
  Craft check              off / log only / enforce. Starts on "log only": it only writes
                           "[craft check]" lines. Switch to "enforce" (removes a craft that is
                           not one of the team's) once the log says "the hash matches" for your crafts.
All of it is also in BepInEx\config\com.yourname.sp2servermod.cfg.

Keys (changeable in the .cfg):  Insert = copy a <Spawn> line for where you are,
End = read the match files again, "." and "," = next / previous player while spectating.


REPORTING
---------
Send BepInEx\LogOutput.log from the host AND from each player involved, right after the
problem (the log is overwritten each time the game starts). Say who was host, who had the
mod, what you did and what you expected. Lines of interest all contain "SP2 Server Mod",
"Loadout", "Referee", "[craft check]", "Spectator" or "[COMPAT]". Screenshots help.


KNOWN LIMITS
------------
* Only the game's deathmatch matches (free for all, or two teams).
* A craft is never forced on a player without the mod: the game loads each player's craft
  from their own computer. They get the website link, or can copy a teammate's craft from
  the player list.
* No spectator for players without the mod.
* Expect rough edges: this is the first build that is tested by other people.


UNINSTALL
---------
Delete BepInEx\plugins\SP2ServerMod. Your matches stay in BepInEx\config\sp2servermod.
